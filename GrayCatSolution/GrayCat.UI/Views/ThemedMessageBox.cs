namespace GrayCat.UI.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrayCat.UI.Controls;
using GrayCat.UI.Localization;

/// <summary>Message box in the app's style (own title bar, palette, translated buttons). Same calls as MessageBox.Show.</summary>
public sealed class ThemedMessageBox : ThemedWindow
{
    private MessageBoxResult _result = MessageBoxResult.None;

    internal ThemedMessageBox(string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        Title = string.IsNullOrWhiteSpace(caption) ? "GrayCat Studio" : caption;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        MinWidth = 360;
        MaxWidth = 640;

        var body = new DockPanel { Margin = new Thickness(22, 20, 22, 18) };

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        DockPanel.SetDock(row, Dock.Bottom);
        foreach (var (label, result, isDefault, isCancel) in ButtonsFor(buttons))
        {
            var button = new Button
            {
                Content = Loc.T(label),
                MinWidth = 92,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };
            if (!isDefault && !isCancel)
                button.SetResourceReference(StyleProperty, "QuietButton");
            button.Click += (_, _) =>
            {
                _result = result;
                Close();
            };
            row.Children.Add(button);
        }
        body.Children.Add(row);

        if (Glyph(image) is { } glyph)
        {
            var icon = new TextBlock
            {
                Text = glyph.Symbol,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 26,
                Foreground = new SolidColorBrush(glyph.Color),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 16, 0)
            };
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
        }

        body.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            LineHeight = 20,
            VerticalAlignment = VerticalAlignment.Center
        });

        Content = body;
        // Closing with the title-bar X counts as Cancel / No, like the system message box.
        Closed += (_, _) =>
        {
            if (_result == MessageBoxResult.None)
                _result = buttons switch
                {
                    MessageBoxButton.YesNo => MessageBoxResult.No,
                    MessageBoxButton.OK => MessageBoxResult.OK,
                    _ => MessageBoxResult.Cancel
                };
        };
    }

    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var box = new ThemedMessageBox(text, caption, buttons, image);
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        if (owner != null && owner.IsVisible && !ReferenceEquals(owner, box))
        {
            box.Owner = owner;
            box.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        box.ShowDialog();
        return box._result;
    }

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None) => Show(null, text, caption, buttons, image);

    private static IEnumerable<(string Label, MessageBoxResult Result, bool IsDefault, bool IsCancel)> ButtonsFor(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.YesNo => new[] { ("Yes", MessageBoxResult.Yes, true, false), ("No", MessageBoxResult.No, false, true) },
        MessageBoxButton.YesNoCancel => new[] { ("Yes", MessageBoxResult.Yes, true, false), ("No", MessageBoxResult.No, false, false), ("Cancel", MessageBoxResult.Cancel, false, true) },
        MessageBoxButton.OKCancel => new[] { ("OK", MessageBoxResult.OK, true, false), ("Cancel", MessageBoxResult.Cancel, false, true) },
        _ => new[] { ("OK", MessageBoxResult.OK, true, true) }
    };

    private static (string Symbol, Color Color)? Glyph(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error => ("", Color.FromRgb(179, 54, 43)),
        MessageBoxImage.Warning => ("", Color.FromRgb(181, 101, 29)),
        MessageBoxImage.Question => ("", Color.FromRgb(125, 95, 67)),
        MessageBoxImage.Information => ("", Color.FromRgb(94, 127, 50)),
        _ => null
    };
}
