namespace GrayCat.UI.Services;

using System.Runtime.InteropServices;
using GrayCat.Core.Generation;
using Microsoft.Win32;

/// <summary>Registers .graycat files to open in GrayCat Studio for the current Windows user (no admin rights needed).</summary>
public static class FileAssociation
{
    private const string ProgId = "GrayCat.Project";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        return key?.GetValue(null) is string command && command.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
    }

    public static void Register()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find GrayCat Studio's executable.");

        using (var ext = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{GrayCatDocument.Extension}"))
        {
            ext.SetValue(null, ProgId);
            ext.SetValue("Content Type", "application/x-graycat-project");
        }
        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue(null, "GrayCat Studio project");
            using (var icon = prog.CreateSubKey("DefaultIcon"))
                icon.SetValue(null, $"\"{exe}\",0");
            using (var command = prog.CreateSubKey(@"shell\open\command"))
                command.SetValue(null, $"\"{exe}\" \"%1\"");
        }

        // Tell Explorer the association changed so icons and double-click update right away.
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
