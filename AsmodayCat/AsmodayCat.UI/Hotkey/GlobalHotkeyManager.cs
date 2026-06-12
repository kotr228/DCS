using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AsmodayCat.UI.Hotkey;

// Global hotkey (Ctrl+Space) via Win32 RegisterHotKey
public class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId  = 9001;

    // MOD_CONTROL = 0x0002, VK_SPACE = 0x20
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_SPACE    = 0x20;

    private HwndSource? _source;
    private readonly Window _owner;

    public event Action? HotkeyPressed;

    public GlobalHotkeyManager(Window owner)
    {
        _owner = owner;
    }

    public void Register()
    {
        var helper = new WindowInteropHelper(_owner);
        _source = HwndSource.FromHwnd(helper.EnsureHandle());
        _source?.AddHook(WndProc);
        RegisterHotKey(_source!.Handle, HotkeyId, MOD_CONTROL, VK_SPACE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source != null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
