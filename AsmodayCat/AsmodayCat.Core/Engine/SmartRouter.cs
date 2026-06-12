using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;
using AsmodayCat.Core.Hardware;

namespace AsmodayCat.Core.Engine;

// FR2.2: автоматичний fallback на CPU якщо VRAM переповнена але задача Critical
public class SmartRouter
{
    private readonly ILLMEngine _engine;
    private readonly ResourceManager _resources;

    // Поріг вільного VRAM в MB нижче якого вважаємо що карта переповнена
    private const long VramLowThresholdMb = 512;

    public SmartRouter(ILLMEngine engine, ResourceManager resources)
    {
        _engine = engine;
        _resources = resources;
    }

    public async Task<LlmResponse> RouteAsync(
        LlmRequest request, TaskPriority priority, CancellationToken cancellationToken = default)
    {
        var effectiveDevice = ResolveDevice(request, priority);

        var routed = request with { ExecutionDevice = effectiveDevice };
        return await _engine.GenerateAsync(routed, cancellationToken);
    }

    private ExecutionDevice ResolveDevice(LlmRequest request, TaskPriority priority)
    {
        if (request.ExecutionDevice == ExecutionDevice.CPU)
            return ExecutionDevice.CPU;

        var status = _resources.GetCurrentStatus();
        var vramFreeMb = status.VramFree / 1024 / 1024;

        // VRAM переповнена і задача критична → fallback на CPU
        if (vramFreeMb < VramLowThresholdMb && priority == TaskPriority.High)
            return ExecutionDevice.CPU;

        return request.ExecutionDevice;
    }
}
