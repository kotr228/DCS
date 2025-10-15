namespace GrayCat.Shared.Models
{
    public class ProjectGenerationRequest
    {
        public string ProjectName { get; set; } = "NewGrayCatProject";
        public string OutputPath { get; set; } = string.Empty;
        public ProjectType Type { get; set; }
        public string Author { get; set; } = "Default User"; // Нове поле
    }
}