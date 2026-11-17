namespace GrayCat.UI.Controls;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

/// <summary>
/// Base for every GrayCat window: own title bar from GrayCatWindowStyle (logo, title, optional
/// <see cref="TitleBarContent"/>, caption buttons) with the normal Windows frame behaviour
/// (snap, double-click to maximize, resize borders, maximize to the work area) from WindowChrome.
/// </summary>
public class ThemedWindow : Window
{
    public static readonly DependencyProperty TitleBarContentProperty =
        DependencyProperty.Register(nameof(TitleBarContent), typeof(object), typeof(ThemedWindow));

    /// <summary>Extra content shown in the title bar, e.g. the main menu.</summary>
    public object? TitleBarContent
    {
        get => GetValue(TitleBarContentProperty);
        set => SetValue(TitleBarContentProperty, value);
    }

    private FrameworkElement? _root;

    public ThemedWindow()
    {
        SetResourceReference(StyleProperty, "GrayCatWindowStyle");

        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this)));

        StateChanged += (_, _) => UpdateMaximizedInset();
        DpiChanged += (_, _) => UpdateMaximizedInset();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _root = GetTemplateChild("RootBorder") as FrameworkElement;
        UpdateMaximizedInset();
    }

    /// <summary>
    /// Windows maximizes a resizable window together with its (invisible) resize frame, so the frame
    /// hangs off every screen edge and would cut off the content. Pull the content in by exactly
    /// that frame size, measured for the monitor's DPI; the window still stops at the taskbar.
    /// </summary>
    private void UpdateMaximizedInset()
    {
        if (_root == null) return;
        if (WindowState != WindowState.Maximized)
        {
            _root.Margin = new Thickness(0);
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        var dpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : 96u;
        if (dpi == 0) dpi = 96;
        var paddedBorder = GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
        var x = (GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + paddedBorder) * 96.0 / dpi;
        var y = (GetSystemMetricsForDpi(SM_CYSIZEFRAME, dpi) + paddedBorder) * 96.0 / dpi;
        _root.Margin = new Thickness(x, y, x, y);
    }

    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
