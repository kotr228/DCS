using System;
using System.Runtime.InteropServices;
using System.Text;

namespace JolieCatEngine.Scripting.CSharp
{
    public static class NativeBridge
    {
        private const string NativeLibrary = "JolieCatEngine.Core.dll";

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Engine_Initialize();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Engine_Shutdown();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Engine_GetVersion();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Engine_InitializeViewport(IntPtr hwnd, int width, int height);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Engine_StartRenderLoop();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint Engine_CreateEntity();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Engine_SetTransform(
            uint entityId,
            float positionX, float positionY, float positionZ,
            float rotationX, float rotationY, float rotationZ,
            float scaleX, float scaleY, float scaleZ);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Engine_ResizeViewport(int width, int height);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int Engine_GetAssets(StringBuilder outBuffer, int maxLength);
    }
}
