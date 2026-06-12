namespace AsmodayCat.Network.BlackCatBridge;

// Mock-інтерфейс перевірки BlackCat: чи дозволено вихід в інтернет для цього типу запиту
public interface IBlackCatSecurityFilter
{
    Task<bool> IsInternetAccessAllowedAsync(string purpose, CancellationToken cancellationToken = default);
}
