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

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;

        private static bool s_classRegistered;

        private bool _isPanning;
        private Point _lastMousePos;
        private float _camX;
        private float _camY;
        private float _camZoom = 1.0f;

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

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_RBUTTONDOWN:
                case WM_MBUTTONDOWN:
                    _isPanning = true;
                    _lastMousePos = GetPointFromLParam(lParam);
                    SetFocus(hwnd);
                    SetCapture(hwnd);
                    handled = true;
                    return IntPtr.Zero;

                case WM_RBUTTONUP:
                case WM_MBUTTONUP:
                    _isPanning = false;
                    ReleaseCapture();
                    handled = true;
                    return IntPtr.Zero;

                case WM_MOUSEMOVE:
                    if (_isPanning)
                    {
                        var currentMousePos = GetPointFromLParam(lParam);
                        var deltaX = currentMousePos.X - _lastMousePos.X;
                        var deltaY = currentMousePos.Y - _lastMousePos.Y;

                        _camX -= (float)(deltaX / _camZoom);
                        _camY -= (float)(deltaY / _camZoom);

                        _lastMousePos = currentMousePos;

                        NativeBridge.Engine_SetEditorCamera(_camX, _camY, _camZoom);
                    }
                    handled = true;
                    return IntPtr.Zero;

                case WM_MOUSEWHEEL:
                {
                    short wheelDelta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    const float zoomStep = 0.1f;
                    _camZoom = Math.Clamp(_camZoom + Math.Sign(wheelDelta) * zoomStep, 0.1f, 10.0f);

                    NativeBridge.Engine_SetEditorCamera(_camX, _camY, _camZoom);
                    handled = true;
                    return IntPtr.Zero;
                }
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        private static Point GetPointFromLParam(IntPtr lParam)
        {
            long value = lParam.ToInt64();
            short x = unchecked((short)(value & 0xFFFF));
            short y = unchecked((short)((value >> 16) & 0xFFFF));
            return new Point(x, y);
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReleaseCapture();
    }
}
