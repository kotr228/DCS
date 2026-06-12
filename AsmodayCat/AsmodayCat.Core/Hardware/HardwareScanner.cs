using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;

namespace AsmodayCat.Core.Hardware;

public class HardwareScanner : IHardwareScanner
{
    public IReadOnlyList<ExecutionDevice> GetAvailableDevices()
    {
        var devices = new List<ExecutionDevice> { ExecutionDevice.CPU };

        try
        {
            DetectGpus(devices);
        }
        catch
        {
            // Якщо System.Management недоступний — повертаємо лише CPU
        }

        return devices.AsReadOnly();
    }

    private static void DetectGpus(List<ExecutionDevice> devices)
    {
#if WINDOWS
        using var searcher = new System.Management.ManagementObjectSearcher(
            "SELECT Name FROM Win32_VideoController");

        foreach (System.Management.ManagementObject obj in searcher.Get())
        {
            var name = obj["Name"]?.ToString() ?? string.Empty;

            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                !devices.Contains(ExecutionDevice.GPU_Nvidia))
                devices.Add(ExecutionDevice.GPU_Nvidia);

            if ((name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) &&
                !devices.Contains(ExecutionDevice.GPU_AMD))
                devices.Add(ExecutionDevice.GPU_AMD);
        }
#endif
    }
}
