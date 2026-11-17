using System.IO;
using System.Net.Http;

namespace AsmodayCat.UI.Services;

// Large downloads (models, installers) with progress: written to *.part, renamed when complete.
public static class Downloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(60) };

    public static async Task DownloadAsync(
        string url, string target, string what, IProgress<(string, double)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var part = target + ".part";

        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(part))
        {
            var buffer = new byte[256 * 1024];
            long done = 0;
            int n;
            var lastReport = DateTime.MinValue;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0 && DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(300))
                {
                    progress?.Report((what, done * 100.0 / total));
                    lastReport = DateTime.UtcNow;
                }
            }
        }

        File.Move(part, target, overwrite: true);
        progress?.Report((what, 100));
    }
}
