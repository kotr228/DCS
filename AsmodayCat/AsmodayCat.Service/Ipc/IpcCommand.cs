namespace AsmodayCat.Service.Ipc;

public enum IpcCommandType
{
    GetStatus,
    StartAgent,
    StopAgent,
    ChangeHardwareModel,
    GetSystemLoad,
    KillSwitch,
    Chat,
    PullModel,
    GetPullStatus,
    ListModels,
    GetDashboardStats,
    ClearContext,
    GetModelPool,
    UnloadModel,
    GetHardwareConfig,
    SaveHardwareConfig,
    GetAgentRules,
    AddAgentRule,
    RemoveAgentRule
}

public class IpcCommand
{
    public IpcCommandType Type { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class IpcResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Payload { get; set; }
}
