namespace AsmodayCat.Shared.Models;

// Addresses and folders the service uses; saved in the database, applied after a service restart
public class ConnectionSettingsDto
{
    public string OllamaBaseUrl          { get; set; } = "http://localhost:11434";
    public string StableDiffusionBaseUrl { get; set; } = "http://127.0.0.1:7860";
    public string ChatWorkspacePath      { get; set; } = string.Empty;

    public static readonly string[] Keys = ["Ollama:BaseUrl", "StableDiffusion:BaseUrl", "Chat:WorkspacePath"];
}
