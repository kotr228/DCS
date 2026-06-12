namespace AsmodayCat.Shared.Models;

public class SystemStatusDto
{
    // CPU / RAM
    public double CpuLoadPercent { get; set; }
    public long   RamFreeMb      { get; set; }
    public long   RamTotalMb     { get; set; }

    // GPU / VRAM (FR-D1)
    public double GpuLoadPercent   { get; set; }
    public long   VramUsedMb       { get; set; }
    public long   VramTotalMb      { get; set; }

    // Active LLM (FR-D2)
    public string ActiveModelName   { get; set; } = string.Empty;
    public string ActiveModelStatus { get; set; } = "Idle";

    // Network (FR-D3)
    public int AvailableNodesCount { get; set; }

    // Recent activity (FR-D4)
    public List<AgentActivityLogDto> RecentActivity { get; set; } = [];

    // Chart history — 60 data points @ 1 per second (FR-C1/C2)
    public List<ResourceDataPoint> CpuHistory  { get; set; } = [];
    public List<ResourceDataPoint> GpuHistory  { get; set; } = [];
    public List<ResourceDataPoint> RamHistory  { get; set; } = [];
    public List<ResourceDataPoint> VramHistory { get; set; } = [];
    public List<ResourceDataPoint> TpsHistory  { get; set; } = [];
}
