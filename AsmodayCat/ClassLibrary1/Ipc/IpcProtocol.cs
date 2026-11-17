namespace AsmodayCat.Shared.Ipc;

// Shared by Service and UI. Serialized as a number, so only ever append new values.
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
    RemoveAgentRule,
    GetLogs,
    ExecuteCliCommand,
    GetPendingApprovals,
    ResolveApproval,
    CancelChat,
    SetAgentRuleActive,
    GetEngineStatus,
    DeleteModel,
    GetChatSessions,
    GetChatHistory,
    OpenChatSession,
    DeleteChatSession,
    GetMemory,
    DeleteMemory,
    ClearMemory,
    AddMemoryFact,
    ReprocessAgentRule,
    TruncateChat,
    GetNotifications,
    GetConnectionSettings,
    SaveConnectionSettings,
    RestartService,
    CancelPull,
    UpdateAgentRule,
    SetLanguage
}

public class IpcCommand
{
    public IpcCommandType Type { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}
