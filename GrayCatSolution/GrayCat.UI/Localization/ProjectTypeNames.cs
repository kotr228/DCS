namespace GrayCat.UI.Localization;

using System.Globalization;
using System.Windows.Data;
using GrayCat.Shared.Models;

/// <summary>Readable, translated names for project types ("StaticHtml" → "Static website (HTML)").</summary>
public static class ProjectTypeNames
{
    public static string Display(ProjectType type) => Loc.T(type switch
    {
        ProjectType.StaticHtml => "Static website (HTML)",
        ProjectType.React => "React website (Vite)",
        ProjectType.NextJs => "Next.js website",
        ProjectType.NodeJsApi => "Website with a Node.js server",
        ProjectType.ReactNative => "Mobile app (React Native, Expo)",
        _ => type.ToString()
    });

    public static string Display(object? value) => value is ProjectType type ? Display(type) : value?.ToString() ?? string.Empty;
}

/// <summary>For combo boxes listing project types.</summary>
public sealed class ProjectTypeNameConverter : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c) => ProjectTypeNames.Display(value);
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
