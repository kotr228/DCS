namespace AsmodayCat.Shared.Interfaces;

public interface IApprovalService
{
    // Returns true only if the user explicitly approved; false on deny, timeout or no UI.
    Task<bool> RequestAsync(string source, string title, string details, CancellationToken cancellationToken = default);
}
