using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Adjustments;
using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using JolieCat.UI.Views;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Non-destructive adjustment layers: Layer &gt; New Adjustment Layer and re-editing
    /// one later. They reuse the destructive adjustments' dialogs; the dialog's values
    /// drive the layer's <see cref="AdjustmentSettings"/> live instead of a pixel preview.
    /// </summary>
    public partial class MainViewModel
    {
        /// <summary>Layer &gt; New Adjustment Layer &gt; <paramref name="kind"/>: adds the
        /// layer above the active one and opens its settings. Cancel removes it again.</summary>
        [RelayCommand]
        private void NewAdjustmentLayer(AdjustmentKind kind)
        {
            if (IsSaving) return;

            var layer = Layers.BeginNewAdjustmentLayer(AdjustmentSettings.Default(kind));
            if (EditAdjustmentSettings(layer, layer.Adjustment!) is not null)
                Layers.CommitNewAdjustmentLayer();
            else
                Layers.CancelNewAdjustmentLayer();
        }

        /// <summary>Layer &gt; Adjustment Layer Settings (or double-click its thumbnail):
        /// re-opens the active adjustment layer's dialog. Undoable.</summary>
        private bool CanEditAdjustmentLayer() => ActiveDocument?.Layers.ActiveLayer?.Model.IsAdjustment == true;

        [RelayCommand(CanExecute = nameof(CanEditAdjustmentLayer))]
        private void EditAdjustmentLayer()
        {
            if (IsSaving || Layers.ActiveLayer?.Model is not { Adjustment: { } before } layer) return;

            if (EditAdjustmentSettings(layer, before) is { } after)
                Layers.CommitAdjustmentEdit(layer, before, after);
            else
                Layers.PreviewAdjustment(layer, before); // cancelled: put the old settings back
        }

        /// <summary>Shows the dialog matching the adjustment's kind, previewing every
        /// change on <paramref name="layer"/>. Returns the confirmed settings, or null if
        /// cancelled.</summary>
        private AdjustmentSettings? EditAdjustmentSettings(Layer layer, AdjustmentSettings initial)
        {
            var owner = Application.Current.MainWindow;
            Func<AdjustmentSettings> read;
            Window dialog;
            INotifyPropertyChanged source;

            switch (initial.Kind)
            {
                case AdjustmentKind.BrightnessContrast or AdjustmentKind.HueSaturation:
                {
                    var hue = initial.Kind == AdjustmentKind.HueSaturation;
                    var vm = new SimpleAdjustmentViewModel(hue ? SimpleAdjustmentKind.HueSaturation : SimpleAdjustmentKind.BrightnessContrast)
                    {
                        Slider1Value = Value(initial, 0, 0),
                        Slider2Value = Value(initial, 1, 0),
                        Slider3Value = Value(initial, 2, 0),
                    };
                    read = () => new AdjustmentSettings(initial.Kind,
                        hue ? new[] { vm.Slider1Value, vm.Slider2Value, vm.Slider3Value } : new[] { vm.Slider1Value, vm.Slider2Value });
                    dialog = new SimpleAdjustmentDialog { DataContext = vm };
                    source = vm;
                    break;
                }

                case AdjustmentKind.Levels:
                {
                    using var below = CompositeBelow(layer);
                    var vm = new LevelsViewModel(below)
                    {
                        InputBlack = (int)Value(initial, 0, 0),
                        InputWhite = (int)Value(initial, 1, 255),
                        Gamma = Value(initial, 2, 1),
                        OutputBlack = (int)Value(initial, 3, 0),
                        OutputWhite = (int)Value(initial, 4, 255),
                    };
                    read = () => new AdjustmentSettings(AdjustmentKind.Levels,
                        new[] { vm.InputBlack, vm.InputWhite, vm.Gamma, vm.OutputBlack, (double)vm.OutputWhite });
                    dialog = new LevelsDialog { DataContext = vm };
                    source = vm;
                    break;
                }

                case AdjustmentKind.Curves:
                {
                    var vm = new CurvesViewModel();
                    var points = Enumerable.Range(0, initial.Values.Count / 2)
                        .Select(i => (initial.Values[2 * i], initial.Values[2 * i + 1])).ToList();
                    vm.LoadPoints(points);
                    read = () => new AdjustmentSettings(AdjustmentKind.Curves, vm.Points.SelectMany(p => new[] { p.X, p.Y }));
                    dialog = new CurvesDialog { DataContext = vm };
                    source = vm;
                    break;
                }

                case AdjustmentKind.Posterize or AdjustmentKind.Threshold:
                {
                    var vm = new FilterOptionsViewModel(initial.Kind == AdjustmentKind.Posterize ? FilterKind.Posterize : FilterKind.Threshold);
                    vm.Amount = Value(initial, 0, vm.Amount);
                    read = () => new AdjustmentSettings(initial.Kind, new[] { Math.Round(vm.Amount) });
                    dialog = new FilterDialog { DataContext = vm };
                    source = vm;
                    break;
                }

                case var kind when GenericAdjustmentViewModel.Handles(kind):
                {
                    var vm = new GenericAdjustmentViewModel(initial);
                    read = vm.ToSettings;
                    dialog = new GenericAdjustmentDialog { DataContext = vm };
                    source = vm;
                    break;
                }

                default:
                    return initial; // Invert has no settings
            }

            void Preview(object? sender, PropertyChangedEventArgs e) => Layers.PreviewAdjustment(layer, read());
            source.PropertyChanged += Preview;
            dialog.Owner = owner;
            var confirmed = dialog.ShowDialog() == true;
            source.PropertyChanged -= Preview;

            return confirmed ? read() : null;
        }

        /// <summary>Image &gt; Adjustments &gt; Exposure / Black &amp; White / Color Balance /
        /// Photo Filter / Gradient Map on the active layer's pixels, previewed live.</summary>
        [RelayCommand]
        private void OpenGenericAdjustment(AdjustmentKind kind)
        {
            if (IsSaving || Layers.ActiveLayer is null || Layers.ActiveLayer.Model.IsGroup || Layers.ActiveLayer.Model.IsAdjustment) return;

            var initial = AdjustmentSettings.Default(kind);
            Canvas.BeginAdjustment(initial.DisplayName);
            var options = new GenericAdjustmentViewModel(initial);

            void UpdatePreview(object? sender, PropertyChangedEventArgs e) => Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new GenericAdjustmentDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied) Canvas.CommitAdjustment();
            else Canvas.CancelAdjustment();
        }

        private static double Value(AdjustmentSettings settings, int index, double fallback) =>
            index < settings.Values.Count ? settings.Values[index] : fallback;

        /// <summary>The image beneath <paramref name="layer"/> - the Levels dialog's histogram source.</summary>
        private SKBitmap CompositeBelow(Layer layer)
        {
            var scene = Layers.Scene;
            var index = 0;
            while (index < scene.Layers.Count && !ReferenceEquals(scene.Layers[index], layer)) index++;

            var bitmap = new SKBitmap(Layers.DocumentWidth, Layers.DocumentHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Erase(SKColors.Transparent);
            SceneCompositor.DrawLayersInto(bitmap, scene, 0, index);
            return bitmap;
        }
    }
}
