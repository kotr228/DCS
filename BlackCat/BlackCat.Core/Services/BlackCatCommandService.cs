using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlackCat.Core.Services;

/// <summary>
/// Named pipe server "BlackCatCommandPipe" — receives directory mirror commands from DCS.
/// DCS sends a ShareDirectoryCommand; BlackCat reads the local directory and transmits
/// all files to the named peer via DcsIntegrationService.
/// </summary>
public sealed class BlackCatCommandService : IDisposable
{
    private const string PipeName = "BlackCatCommandPipe";

    private readonly DcsIntegrationService _dcsIntegration;
    private CancellationTokenSource?       _cts;
    private bool                           _disposed;

    public BlackCatCommandService(DcsIntegrationService dcsIntegration)
    {
        _dcsIntegration = dcsIntegration;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunServerAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    private async Task RunServerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: false);
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cmd = JsonSerializer.Deserialize<ShareDirectoryCommand>(line);
                if (cmd == null) continue;

                _ = Task.Run(() =>
                    _dcsIntegration.SendDirectoryAsync(cmd.PeerBlackID, cmd.LocalDirPath, cmd.RemoteDirName));
            }
            catch (OperationCanceledException) { break; }
            catch { /* non-fatal — retry */ }
            finally { pipe?.Dispose(); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

public class ShareDirectoryCommand
{
    public string PeerBlackID   { get; set; } = string.Empty;
    public string LocalDirPath  { get; set; } = string.Empty;
    public string RemoteDirName { get; set; } = string.Empty;
}
