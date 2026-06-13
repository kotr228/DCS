using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class HardwareViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<string> DeviceOptions { get; } =
        ["Auto", "GPU", "CPU"];

    [ObservableProperty] private string _preferredDevice    = "Auto";
    [ObservableProperty] private int    _contextWindowSize  = 4096;
    [ObservableProperty] private int    _maxVramAllocationMb = 0;
    [ObservableProperty] private int    _cpuThreads         = 4;
    [ObservableProperty] private bool   _allowCpuFallback   = true;
    [ObservableProperty] private string _statusMessage      = string.Empty;

    public int MaxSystemThreads { get; } = Math.Min(Environment.ProcessorCount * 2, 64);

    public HardwareViewModel(IpcClient ipc)
    {
        _ipc = ipc;
        _ = LoadConfigAsync();
    }

    private async Task LoadConfigAsync()
    {
        var resp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.GetHardwareConfig });

        if (resp?.Success != true || resp.Payload is null) return;

        var dto = resp.Payload.Value.Deserialize<HardwareConfigDto>(_opts);
        if (dto is null) return;

        PreferredDevice      = dto.PreferredDevice;
        ContextWindowSize    = dto.ContextWindowSize;
        MaxVramAllocationMb  = dto.MaxVramAllocationMb;
        CpuThreads           = dto.CpuThreads;
        AllowCpuFallback     = dto.AllowCpuFallback;
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        if (CpuThreads < 1 || CpuThreads > MaxSystemThreads)
        {
            StatusMessage = $"CPU threads must be between 1 and {MaxSystemThreads}";
            return;
        }

        if (ContextWindowSize < 2048 || ContextWindowSize > 32768)
        {
            StatusMessage = "Context window must be between 2048 and 32768";
            return;
        }

        var dto = new HardwareConfigDto
        {
            PreferredDevice      = PreferredDevice,
            ContextWindowSize    = ContextWindowSize,
            MaxVramAllocationMb  = MaxVramAllocationMb,
            CpuThreads           = CpuThreads,
            AllowCpuFallback     = AllowCpuFallback
        };

        var json = JsonSerializer.Serialize(dto);

        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.SaveHardwareConfig,
            Parameters = { ["Config"] = json }
        });

        StatusMessage = resp?.Success == true
            ? "Parameters saved successfully"
            : $"Error: {resp?.Error}";
    }
}
