namespace GrayCat.Core.Constants;
public static class AppConstants
{
    public const string AppName = "GrayCat";
    public const string AppVersion = "1.0.0";
    public const string ServiceName = "GrayCatGenerationService";
    public const string ServiceDisplayName = "GrayCat Generation Service";
    public const string ServiceDescription = "Background service for generating web and mobile projects";

    public static class Paths
    {
        public static readonly string AppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CatSuite", "GrayCat");

        public static readonly string Logs = Path.Combine(AppData, "Logs");
        public static readonly string Projects = Path.Combine(AppData, "Projects");
        public static readonly string Templates = Path.Combine(AppData, "Templates");
    }

    public static class ServiceEndpoints
    {
        public const string BaseUrl = "http://localhost:5123";
        public const string GenerateProject = "/api/generation/generate";
        public const string ValidateProject = "/api/generation/validate";
        public const string GetStatus = "/api/generation/status";
    }
}