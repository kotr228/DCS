using System.Collections.Concurrent;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service.Approvals;

// Holds risky agent actions until the user approves or denies them in the UI.
// The UI polls GetPendingApprovals; if it hasn't polled recently, nobody can answer,
// so requests are denied at once instead of blocking the agent.
public sealed class ApprovalBroker : IApprovalService
{
    private static readonly TimeSpan Timeout        = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan UiStaleAfter   = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, (ApprovalRequestDto Dto, TaskCompletionSource<bool> Tcs)> _pending = new();
    private readonly ILogger<ApprovalBroker> _logger;
    private long _lastPollTicks;

    public ApprovalBroker(ILogger<ApprovalBroker> logger) => _logger = logger;

    private bool UiConnected =>
        DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastPollTicks), DateTimeKind.Utc) < UiStaleAfter;

    public async Task<bool> RequestAsync(string source, string title, string details, CancellationToken ct = default)
    {
        if (!UiConnected)
        {
            _logger.LogWarning("Denied (UI not open): {Title} — {Details}", title, Shorten(details));
            return false;
        }

        var dto = new ApprovalRequestDto
        {
            Source    = source,
            Title     = title,
            Details   = details,
            ExpiresAt = DateTime.Now + Timeout
        };
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[dto.Id] = (dto, tcs);

        _logger.LogWarning("Waiting for approval: {Title} — {Details}", title, Shorten(details));

        try
        {
            var approved = await tcs.Task.WaitAsync(Timeout, ct);
            _logger.LogInformation("{Decision}: {Title}", approved ? "Approved" : "Denied", title);
            return approved;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Approval timed out: {Title}", title);
            return false;
        }
        finally
        {
            _pending.TryRemove(dto.Id, out _);
        }
    }

    public IReadOnlyList<ApprovalRequestDto> GetPending()
    {
        Interlocked.Exchange(ref _lastPollTicks, DateTime.UtcNow.Ticks);
        return _pending.Values.Select(p => p.Dto).OrderBy(d => d.CreatedAt).ToList();
    }

    public bool Resolve(Guid id, bool approved)
    {
        if (!_pending.TryGetValue(id, out var entry)) return false;
        return entry.Tcs.TrySetResult(approved);
    }

    public void DenyAll()
    {
        foreach (var (_, entry) in _pending) entry.Tcs.TrySetResult(false);
    }

    private static string Shorten(string s) => s.Length > 200 ? s[..200] + "…" : s;
}
