namespace AsmodayCat.Shared.Models;

public class NodeResourceStatus
{
    public double CpuLoad { get; set; }
    public long RamFree { get; set; }
    public double GpuLoad { get; set; }
    public long VramFree { get; set; }
}
