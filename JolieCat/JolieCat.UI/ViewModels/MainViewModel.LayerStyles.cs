using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Effects;
using JolieCat.UI.Views;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>Layer &gt; Layer Style: drop shadow, outer glow, stroke and color
    /// overlay, plus Copy/Paste/Clear Layer Style.</summary>
    public partial class MainViewModel
    {
        /// <summary>Style copied with Copy Layer Style (shared across documents).</summary>
        private LayerStyle? _copiedLayerStyle;

        private bool CanStyleLayer() => ActiveDocument?.Layers.ActiveLayer?.Model is { IsAdjustment: false };

        private bool CanClearLayerStyle() => ActiveDocument?.Layers.ActiveLayer?.Model.Style is not null;

        private bool CanPasteLayerStyle() => _copiedLayerStyle is not null && CanStyleLayer();

        /// <summary>Opens the Layer Style dialog for the active layer, previewing every
        /// change live. OK records one undo step; Cancel restores the old style.</summary>
        [RelayCommand(CanExecute = nameof(CanStyleLayer))]
        private void EditLayerStyle()
        {
            if (IsSaving || Layers.ActiveLayer?.Model is not { IsAdjustment: false } layer) return;

            var before = layer.Style;
            var fg = Canvas.PrimaryColor;
            var options = new LayerStyleViewModel(before, new SKColor(fg.R, fg.G, fg.B));

            void UpdatePreview(object? sender, PropertyChangedEventArgs e) => Layers.PreviewLayerStyle(layer, options.ToStyle());
            options.PropertyChanged += UpdatePreview;

            var dialog = new LayerStyleDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied) Layers.CommitLayerStyle(layer, before, options.ToStyle());
            else Layers.PreviewLayerStyle(layer, before);

            NotifyLayerStyleCommands();
        }

        [RelayCommand(CanExecute = nameof(CanClearLayerStyle))]
        private void ClearLayerStyle()
        {
            if (Layers.ActiveLayer?.Model is not { } layer) return;
            Layers.CommitLayerStyle(layer, layer.Style, null);
            NotifyLayerStyleCommands();
        }

        [RelayCommand(CanExecute = nameof(CanClearLayerStyle))]
        private void CopyLayerStyle()
        {
            _copiedLayerStyle = Layers.ActiveLayer?.Model.Style;
            NotifyLayerStyleCommands();
        }

        [RelayCommand(CanExecute = nameof(CanPasteLayerStyle))]
        private void PasteLayerStyle()
        {
            if (_copiedLayerStyle is null || Layers.ActiveLayer?.Model is not { IsAdjustment: false } layer) return;
            Layers.CommitLayerStyle(layer, layer.Style, _copiedLayerStyle);
            NotifyLayerStyleCommands();
        }

        private void NotifyLayerStyleCommands()
        {
            EditLayerStyleCommand.NotifyCanExecuteChanged();
            ClearLayerStyleCommand.NotifyCanExecuteChanged();
            CopyLayerStyleCommand.NotifyCanExecuteChanged();
            PasteLayerStyleCommand.NotifyCanExecuteChanged();
        }
    }
}
