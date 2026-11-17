using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JolieCat3D.UI
{
    /// <summary>glTF export options dialog.</summary>
    public sealed class GltfExportWindow : Window
    {
        private readonly CheckBox _selected = new() { Content = "Selected objects only", Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 4, 0, 4) };
        private readonly CheckBox _hidden = new() { Content = "Include hidden objects (LODs, UCX_ colliders)", IsChecked = true, Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 4, 0, 4) };
        private readonly CheckBox _animation = new() { Content = "Include animations (actions)", IsChecked = true, Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 4, 0, 4) };

        public bool SelectedOnly => _selected.IsChecked == true;
        public bool IncludeHidden => _hidden.IsChecked == true;
        public bool IncludeAnimation => _animation.IsChecked == true;

        public GltfExportWindow(bool hasSelection)
        {
            Title = "Export glTF";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21));
            _selected.IsEnabled = hasSelection;

            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(_selected);
            panel.Children.Add(_hidden);
            panel.Children.Add(_animation);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Export...", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(10, 3, 10, 3) };
            ok.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
        }
    }
}
