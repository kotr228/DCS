using System.Collections.ObjectModel;

namespace JolieCatEngine.Scripting.CSharp
{
    public static class DebugConsole
    {
        public static ObservableCollection<string> Logs { get; } = new()
        {
            "[Info] Engine initialized.",
            "[Info] Scene 'Untitled' loaded.",
        };

        public static void Log(string message)
        {
            Logs.Add(message);
        }
    }
}
