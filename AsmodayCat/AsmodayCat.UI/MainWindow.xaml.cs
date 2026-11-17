using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using AsmodayCat.UI.Hotkey;
using AsmodayCat.UI.Views;

namespace AsmodayCat.UI;

public partial class MainWindow : Window
{
    private GlobalHotkeyManager? _hotkey;
    private GlobalHotkeyManager? _voiceHotkey;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizedLayout();
        UpdateTexts();
        AsmodayCat.UI.Localization.Loc.LanguageChanged += UpdateTexts;
        NavList.SelectedIndex = 0;
    }

    // A maximized borderless window extends past the screen by the frame size;
    // pad the content back in so nothing is cut off, and swap the button icon.
    private void UpdateMaximizedLayout()
    {
        var max = WindowState == WindowState.Maximized;
        RootBorder.BorderThickness = new Thickness(max ? 0 : 1);
        MaximizeIcon.Kind = max
            ? MaterialDesignThemes.Wpf.PackIconKind.WindowRestore
            : MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize;
        MaximizeButton.ToolTip = AsmodayCat.UI.Localization.Loc.T(max ? "common.restore" : "common.maximize");
    }

    // Texts set from code; the rest of the window binds to the language in XAML
    private void UpdateTexts()
    {
        VersionText.Text = AsmodayCat.UI.Localization.Loc.F("main.version", AsmodayCat.UI.Services.AppVersion.Display);
        UpdateMaximizedLayout();
    }

    // A borderless window maximizes over the whole monitor, taskbar included.
    // Answer WM_GETMINMAXINFO with the monitor's work area instead.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
        mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
        mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
        mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
        System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
        return IntPtr.Zero;   // not handled: WPF still applies MinWidth/MinHeight
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        _hotkey = new GlobalHotkeyManager(this);
        _hotkey.HotkeyPressed += OnGlobalHotkey;
        _hotkey.Register();

        // Ctrl+Shift+Space: talk to the assistant from anywhere
        _voiceHotkey = new GlobalHotkeyManager(this, 9002,
            GlobalHotkeyManager.MOD_CONTROL | GlobalHotkeyManager.MOD_SHIFT, GlobalHotkeyManager.VK_SPACE);
        _voiceHotkey.HotkeyPressed += OnVoiceHotkey;
        _voiceHotkey.Register();
    }

    private void OnVoiceHotkey() => StartVoiceInput();

    // Voice input from the hotkey or the tray menu: show the chat and start listening
    public void StartVoiceInput()
    {
        if (!IsVisible) App.Services.GetRequiredService<AsmodayCat.UI.Services.TrayService>().ShowMainWindow();

        // Show the chat so the recognized text and the answer are visible
        var chatItem = NavList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == "Chat");
        if (chatItem is not null) NavList.SelectedItem = chatItem;

        var vm = App.Services.GetRequiredService<AsmodayCat.UI.ViewModels.ChatViewModel>();
        if (vm.ToggleListenCommand.CanExecute(null)) vm.ToggleListenCommand.Execute(null);
    }

    private void OnGlobalHotkey()
    {
        var overlay = App.Services.GetRequiredService<OverlayWindow>();
        overlay.Show();
        overlay.Activate();
    }

    // Closing the window hides it to the tray; "Вийти" in the tray menu really exits
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var tray = App.Services.GetRequiredService<AsmodayCat.UI.Services.TrayService>();
        if (!tray.IsExiting)
        {
            e.Cancel = true;
            Hide();
            tray.OnMainWindowHidden();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey?.Dispose();
        _voiceHotkey?.Dispose();
        base.OnClosed(e);
    }

    // ── Chrome handlers ───────────────────────────────────────────────────

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void AppCloseButton_Click(object sender, RoutedEventArgs e)
        => Close();   // hides to the tray (see OnClosing)

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
            "Memory"     => App.Services.GetRequiredService<MemoryView>(),
            _            => null
        };
    }
}
