using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using JolieCatEngine.Scripting.CSharp;

namespace JolieCatEngine.Editor
{
    public sealed class NativeViewportHost : HwndHost
    {
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int BLACK_BRUSH = 4;
        private const string WindowClassName = "JolieCatViewportWindowClass";

        private static bool s_classRegistered;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            EnsureWindowClassRegistered();

            var hwndHost = CreateWindowEx(
                0,
                WindowClassName,
                string.Empty,
                WS_CHILD | WS_VISIBLE,
                0, 0,
                0, 0,
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            return new HandleRef(this, hwndHost);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            DestroyWindow(hwnd.Handle);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            NativeBridge.Engine_ResizeViewport((int)sizeInfo.NewSize.Width, (int)sizeInfo.NewSize.Height);
        }

        private static void EnsureWindowClassRegistered()
        {
            if (s_classRegistered)
            {
                return;
            }

            var user32 = GetModuleHandle("user32.dll");
            var defWindowProc = GetProcAddress(user32, "DefWindowProcW");

            var windowClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = defWindowProc,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = IntPtr.Zero,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = GetStockObject(BLACK_BRUSH),
                lpszMenuName = null,
                lpszClassName = WindowClassName,
                hIconSm = IntPtr.Zero,
            };

            RegisterClassEx(ref windowClass);
            s_classRegistered = true;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpszClassName,
            string lpszWindowName,
            int style,
            int x, int y,
            int width, int height,
            IntPtr hwndParent,
            IntPtr hMenu,
            IntPtr hInst,
            IntPtr pvParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int fnObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    }
}
