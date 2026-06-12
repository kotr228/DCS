namespace AsmodayCat.Shared.Interfaces;

public interface IAgentTool
{
    string GetName();
    string GetDescription();
    Task<string> ExecuteAsync(string jsonArguments, CancellationToken cancellationToken = default);
}
