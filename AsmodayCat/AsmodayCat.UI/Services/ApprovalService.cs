using System.Text.Json;
using System.Windows.Threading;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Views;

namespace AsmodayCat.UI.Services;

// Polls the service for actions waiting for approval and shows them one at a time.
// Polling also tells the service that a UI is open; without it, requests are denied at once.
public sealed class ApprovalService
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    private readonly IpcClient                 _ipc;
    private readonly DispatcherTimer           _timer;
    private readonly Queue<ApprovalRequestDto> _queue = new();
    private readonly HashSet<Guid>             _seen  = [];

    private ApprovalWindow? _current;
    private bool            _polling;

    public ApprovalService(IpcClient ipc)
    {
        _ipc   = ipc;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public void Start() => _timer.Start();

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var resp = await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.GetPendingApprovals });
            if (resp?.Success != true || resp.Payload is not { } payload) return;

            var pending = payload.Deserialize<List<ApprovalRequestDto>>(_opts) ?? [];
            var ids     = pending.Select(p => p.Id).ToHashSet();

            // The service dropped it (timeout, kill switch): close the window
            if (_current is not null && !ids.Contains(_current.Request.Id))
                _current.CloseExpired();

            _seen.IntersectWith(ids);
            foreach (var request in pending.Where(p => _seen.Add(p.Id)))
                _queue.Enqueue(request);

            ShowNext();
        }
        finally
        {
            _polling = false;
        }
    }

    private void ShowNext()
    {
        if (_current is not null || _queue.Count == 0) return;

        var window = new ApprovalWindow(_queue.Dequeue());
        window.Decided += async (id, approved) =>
            await _ipc.SendCommandAsync(new IpcCommand
            {
                Type       = IpcCommandType.ResolveApproval,
                Parameters = { ["Id"] = id.ToString(), ["Approved"] = approved ? "true" : "false" }
            });
        window.Closed += (_, _) =>
        {
            _current = null;
            ShowNext();
        };

        _current = window;
        window.Show();
        window.Activate();
    }
}
