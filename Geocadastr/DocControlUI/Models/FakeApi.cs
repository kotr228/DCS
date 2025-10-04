using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class FakeApi
{
    private List<string> repos = new List<string> { "Repo1", "Repo2", "Repo3" };
    private List<string> branches = new List<string> { "Main", "Dev", "Test" };

    public Task<List<string>> GetRepos()
    {
        return Task.FromResult(repos.ToList());
    }

    public Task<List<string>> GetBranches()
    {
        return Task.FromResult(branches.ToList());
    }

    public Task AddRepo(string name)
    {
        repos.Add(name);
        return Task.CompletedTask;
    }

    public Task AddBranch(string name)
    {
        branches.Add(name);
        return Task.CompletedTask;
    }
}
