namespace GrayCat.UI;

using System.IO;
using System.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure required directories exist
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CatSuite", "GrayCat");

        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(Path.Combine(appData, "Logs"));
        Directory.CreateDirectory(Path.Combine(appData, "Projects"));
    }
}