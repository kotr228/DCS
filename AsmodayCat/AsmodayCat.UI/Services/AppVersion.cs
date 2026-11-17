using System.Reflection;

namespace AsmodayCat.UI.Services;

public static class AppVersion
{
    // "4.0.0" (from Directory.Build.props / publish.ps1 -Version)
    public static string Display { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";
}
