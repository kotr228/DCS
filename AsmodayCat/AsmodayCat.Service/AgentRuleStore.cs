using System.Text.Json;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service;

public class AgentRuleStore
{
    private static readonly string StorePath =
        Path.Combine(AppContext.BaseDirectory, "agent_rules.json");

    private static readonly JsonSerializerOptions _opts =
        new() { WriteIndented = true };

    private readonly List<AgentRuleDto> _rules = [];

    public AgentRuleStore() { Load(); }

    public IReadOnlyList<AgentRuleDto> GetAll() => _rules.AsReadOnly();

    public AgentRuleDto? Get(Guid id) =>
        _rules.FirstOrDefault(r => r.Id == id);

    public void Add(AgentRuleDto rule)
    {
        rule.IsActive = true;
        _rules.Add(rule);
        Persist();
    }

    public bool Remove(Guid id)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule is null) return false;
        _rules.Remove(rule);
        Persist();
        return true;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var list = JsonSerializer.Deserialize<List<AgentRuleDto>>(json) ?? [];
                _rules.AddRange(list);
            }
        }
        catch { /* corrupt file — start fresh */ }
    }

    private void Persist() =>
        File.WriteAllText(StorePath, JsonSerializer.Serialize(_rules, _opts));
}
