using System.Runtime.InteropServices;

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
    }
}
