namespace AsmodayCat.Shared.Models;

public class ModelPullProgress
{
    public string ModelId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;   // "pulling", "done", "error"
    public long TotalBytes { get; set; }
    public long CompletedBytes { get; set; }
    public double Percent => TotalBytes > 0 ? (double)CompletedBytes / TotalBytes * 100 : 0;
}
