using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using AsmodayCat.UI.Hotkey;
using AsmodayCat.UI.Views;

namespace AsmodayCat.UI;

public partial class MainWindow : Window
{
    private GlobalHotkeyManager? _hotkey;

    public MainWindow()
    {
        InitializeComponent();
        NavList.SelectedIndex = 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotkey = new GlobalHotkeyManager(this);
        _hotkey.HotkeyPressed += OnGlobalHotkey;
        _hotkey.Register();
    }

    private void OnGlobalHotkey()
    {
        var overlay = App.Services.GetRequiredService<OverlayWindow>();
        overlay.Show();
        overlay.Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey?.Dispose();
        base.OnClosed(e);
    }

    // ── Chrome handlers ───────────────────────────────────────────────────

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => DragMove();

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void AppCloseButton_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    // ── Navigation ────────────────────────────────────────────────────────

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item) return;

        ContentFrame.Content = item.Tag?.ToString() switch
        {
            "Dashboard"  => App.Services.GetRequiredService<DashboardView>(),
            "Hardware"   => App.Services.GetRequiredService<HardwareView>(),
            "AgentRules" => App.Services.GetRequiredService<AgentRulesView>(),
            "Terminal"   => App.Services.GetRequiredService<TerminalView>(),
            "Chat"       => App.Services.GetRequiredService<ChatView>(),
            "Models"     => App.Services.GetRequiredService<ModelsManagerView>(),
            _            => null
        };
    }
}
