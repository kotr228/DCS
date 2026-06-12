namespace AsmodayCat.Shared.Models;

public enum AgentStateStatus { Idle, Thinking, UsingTool }

public class AgentStateDto
{
    public AgentStateStatus Status   { get; set; }
    public string           ToolName { get; set; } = string.Empty;
}
