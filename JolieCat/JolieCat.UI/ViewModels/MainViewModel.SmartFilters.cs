using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Actions;
using JolieCat.Core.Effects;
using JolieCat.UI.Views;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>Layer > Smart Filters: the window, and editing one filter's settings.</summary>
    public partial class MainViewModel
    {
        [RelayCommand]
        private void OpenSmartFilters()
        {
            if (IsSaving || Layers.ActiveLayer?.Model is not { } layer) return;
            if (!layer.HasSmartFilters)
            {
                var answer = MessageBox.Show(
                    $"\"{layer.Name}\" isn't set up for Smart Filters yet.\n\nConvert it now? Filters you apply to it afterwards stay editable.",
                    "Smart Filters", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                Layers.ConvertForSmartFiltersCommand.Execute(null);
                if (Layers.ActiveLayer?.Model is not { HasSmartFilters: true } converted) return;
                layer = converted;
            }

            var panel = new SmartFiltersViewModel(layer, Canvas, EditSmartFilter);
            new SmartFiltersDialog(panel) { Owner = Application.Current.MainWindow }.ShowDialog();
        }

        /// <summary>Reopens filter <paramref name="index"/>'s own dialog with its settings;
        /// the canvas previews the whole filter stack with the change. True if changed.</summary>
        internal bool EditSmartFilter(Core.Documents.Layer layer, int index)
        {
            if (index < 0 || index >= layer.SmartFilters.Count) return false;
            var step = layer.SmartFilters[index].Step;

            switch (step.Kind)
            {
                case "Filter" when Enum.TryParse<FilterKind>(step.Command, out var kind):
                {
                    var options = new FilterOptionsViewModel(kind) { Amount = step.Number("Amount"), SecondAmount = step.Number("Second") };
                    if (!options.HasSettings) return false;
                    return EditWith(layer, index, options, new FilterDialog { DataContext = options }, () => StepForFilter(options));
                }

                case "CameraRaw":
                {
                    var s = CameraRawSettingsOf(step);
                    var options = new CameraRawViewModel
                    {
                        Temperature = s.Temperature, Tint = s.Tint, Exposure = s.Exposure, Contrast = s.Contrast,
                        Highlights = s.Highlights, Shadows = s.Shadows, Whites = s.Whites, Blacks = s.Blacks,
                        Clarity = s.Clarity, Vibrance = s.Vibrance, Saturation = s.Saturation,
                    };
                    return EditWith(layer, index, options, new CameraRawDialog { DataContext = options }, () => StepForCameraRaw(options.Settings));
                }

                case "BlurGallery":
                {
                    var s = BlurGallerySettingsOf(step);
                    var options = new BlurGalleryViewModel(s.Iris)
                    {
                        CenterX = s.CenterX * 100, CenterY = s.CenterY * 100, Size = s.Size * 100, SizeY = s.SizeY * 100,
                        Transition = s.Transition * 100, Angle = s.Angle, Blur = s.Blur, Saturation = s.Saturation,
                    };
                    return EditWith(layer, index, options, new BlurGalleryDialog { DataContext = options }, () => StepForBlurGallery(options.Settings, options.Title));
                }

                case "Adjustment" when Enum.TryParse<SimpleAdjustmentKind>(step.Command, out var kind):
                {
                    var options = new SimpleAdjustmentViewModel(kind)
                    {
                        Slider1Value = step.Number("V1"), Slider2Value = step.Number("V2"), Slider3Value = step.Number("V3"),
                    };
                    return EditWith(layer, index, options, new SimpleAdjustmentDialog { DataContext = options }, () => StepForSimpleAdjustment(options, step.Title));
                }

                case "Levels" when layer.SmartFilterSource is { } source:
                {
                    var options = new LevelsViewModel(source)
                    {
                        InputBlack = (int)step.Number("InBlack"), InputWhite = (int)step.Number("InWhite", 255), Gamma = step.Number("Gamma", 1),
                        OutputBlack = (int)step.Number("OutBlack"), OutputWhite = (int)step.Number("OutWhite", 255),
                    };
                    return EditWith(layer, index, options, new LevelsDialog { DataContext = options }, () => StepForLevels(options));
                }

                case "Curves":
                {
                    var options = new CurvesViewModel();
                    var points = ParseCurvePoints(step.Text("Points"));
                    if (points.Count >= 2) options.LoadPoints(points);
                    return EditWith(layer, index, options, new CurvesDialog { DataContext = options }, () => StepForCurves(options));
                }

                default:
                    MessageBox.Show("This filter has no settings to change.", "Smart Filters", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
            }
        }

        private bool EditWith(Core.Documents.Layer layer, int index, INotifyPropertyChanged options, Window dialog, Func<ActionStep> stepOf)
        {
            var original = layer.SmartFilters;
            IReadOnlyList<SmartFilter> WithEdit() =>
                original.Select((f, i) => i == index ? f with { Step = stepOf() } : f).ToList();

            Canvas.BeginAdjustment("Edit Smart Filter");
            SKBitmap Preview(SKBitmap _) => SmartFilterEngine.Render(layer.SmartFilterSource!, WithEdit());
            void Update(object? sender, PropertyChangedEventArgs e) => Canvas.UpdateAdjustmentPreview(Preview);
            options.PropertyChanged += Update;
            Update(null, null!);

            dialog.Owner = Application.Current.MainWindow;
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= Update;
            Canvas.CancelAdjustment();

            if (!applied) return false;
            var edited = WithEdit();
            Canvas.ChangeSmartFilters(layer, edited, $"Edit {edited[index].Title}");
            return true;
        }
    }
}
