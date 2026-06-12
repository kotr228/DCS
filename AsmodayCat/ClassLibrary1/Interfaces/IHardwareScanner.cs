using AsmodayCat.Shared.Enums;

namespace AsmodayCat.Shared.Interfaces;

public interface IHardwareScanner
{
    IReadOnlyList<ExecutionDevice> GetAvailableDevices();
}
