namespace AsmodayCat.Shared.Models;

public class HardwareConfigDto
{
    public string PreferredDevice      { get; set; } = "Auto";
    public int    ContextWindowSize    { get; set; } = 4096;
    public int    MaxVramAllocationMb  { get; set; } = 0;
    public int    CpuThreads           { get; set; } = 4;
    public bool   AllowCpuFallback     { get; set; } = true;
}
