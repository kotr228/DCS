using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DocControlNetworkCore.Services
{
    /// <summary>
    /// Реалізація IFileSystemService для WAN-вузлів, що прийшли через BlackCat-тунель.
    ///
    /// Логіка роботи:
    ///   • GetFileListAsync     — читає кеш з поля PeerIdentity або запитує через тунель
    ///   • GetFileMetadataAsync — пошук у кеші
    ///   • DownloadFileAsync    — чанкова передача через TCP на TcpPort вузла (або заглушка)
    ///   • PingAsync            — TCP-з'єднання до IpAddress:TcpPort
    ///
    /// Якщо вузол недоступний — методи повертають false/null/порожній список.
    /// </summary>
    public class RemoteFileSystemService : IFileSystemService
    {
        private readonly PeerIdentity _peer;
        private readonly PeerRegistryService _registry;

        // Простий кеш файлового списку в пам'яті (оновлюється при виклику GetFileListAsync)
        private FileSystemItemList? _cachedList;
        private DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

        public RemoteFileSystemService(PeerIdentity peer, PeerRegistryService registry)
        {
            _peer     = peer;
            _registry = registry;
        }

        // ─── IFileSystemService ───────────────────────────────────────────────

        public FileSystemType Type       => FileSystemType.Remote;
        public string         SystemId   => _peer.BlackId;
        public string         DisplayName => $"{_peer.UserName}@{_peer.MachineName} [WAN]";

        public bool IsAvailable
        {
            get
            {
                var current = _registry.GetPeer(_peer.InstanceId);
                return current != null &&
                       (DateTime.Now - current.LastSeen) < TimeSpan.FromSeconds(60);
            }
        }

        public async Task<FileSystemItemList> GetFileListAsync(
            string path,
            string filter = "*.*",
            bool   includeSubdirectories = false)
        {
            // Повернути кеш якщо свіжий
            if (_cachedList != null && (DateTime.Now - _cacheTime) < CacheTtl)
                return _cachedList;

            // Спроба отримати список через TCP-команду
            var result = await FetchFileListViaTcpAsync(path, filter, includeSubdirectories);

            if (result.Success)
            {
                _cachedList = result;
                _cacheTime  = DateTime.Now;
            }

            return result;
        }

        public Task<FileSystemItem?> GetFileMetadataAsync(string filePath)
        {
            if (_cachedList == null)
                return Task.FromResult<FileSystemItem?>(null);

            var item = _cachedList.Items.Find(i =>
                string.Equals(i.FullPath, filePath, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<FileSystemItem?>(item);
        }

        public async Task<bool> DownloadFileAsync(
            string remotePath,
            string localPath,
            IProgress<TransferProgress>? progress = null)
        {
            try
            {
                if (!IsAvailable) return false;

                using var tcp = new TcpClient();
                await tcp.ConnectAsync(_peer.IpAddress, _peer.TcpPort);

                using var stream = tcp.GetStream();
                using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);

                // Відправити команду скачування
                var request = JsonSerializer.Serialize(new
                {
                    Command    = "DownloadFile",
                    RemotePath = remotePath
                });
                await writer.WriteLineAsync(request);
                await writer.FlushAsync();

                // Отримати заголовок: { "Size": 1234, "Success": true }
                var headerLine = await ReadLineWithTimeoutAsync(reader, TimeSpan.FromSeconds(10));
                if (headerLine == null) return false;

                using var doc  = JsonDocument.Parse(headerLine);
                bool ok        = doc.RootElement.GetProperty("Success").GetBoolean();
                if (!ok) return false;

                long totalBytes = doc.RootElement.GetProperty("Size").GetInt64();
                var  fileName   = Path.GetFileName(remotePath);

                // Отримати байти чанками 1024 B
                Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? ".");
                using var file = File.Create(localPath);

                var   buffer     = new byte[1024];
                long  received   = 0;
                while (received < totalBytes)
                {
                    int toRead = (int)Math.Min(buffer.Length, totalBytes - received);
                    int read   = await stream.ReadAsync(buffer, 0, toRead);
                    if (read == 0) break;
                    await file.WriteAsync(buffer, 0, read);
                    received += read;

                    progress?.Report(new TransferProgress
                    {
                        FileName         = fileName,
                        BytesTransferred = received,
                        TotalBytes       = totalBytes
                    });
                }

                return received >= totalBytes;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> PingAsync()
        {
            try
            {
                if (!IsAvailable) return false;
                using var tcp = new TcpClient();
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                await tcp.ConnectAsync(_peer.IpAddress, _peer.TcpPort, cts.Token);
                return tcp.Connected;
            }
            catch { return false; }
        }

        // ─── Приватні утиліти ─────────────────────────────────────────────────

        private async Task<FileSystemItemList> FetchFileListViaTcpAsync(
            string path, string filter, bool recursive)
        {
            var empty = new FileSystemItemList { Success = false };
            try
            {
                if (!IsAvailable) return empty;

                using var tcp = new TcpClient();
                await tcp.ConnectAsync(_peer.IpAddress, _peer.TcpPort);

                using var stream = tcp.GetStream();
                using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);

                var request = JsonSerializer.Serialize(new
                {
                    Command              = "GetFileList",
                    Path                 = path,
                    Filter               = filter,
                    IncludeSubdirectories = recursive
                });
                await writer.WriteLineAsync(request);
                await writer.FlushAsync();

                var line = await ReadLineWithTimeoutAsync(reader, TimeSpan.FromSeconds(10));
                if (line == null) return empty;

                var items = JsonSerializer.Deserialize<List<FileSystemItem>>(line);
                return new FileSystemItemList
                {
                    Success = true,
                    Items   = items ?? new List<FileSystemItem>()
                };
            }
            catch (Exception ex)
            {
                return new FileSystemItemList { Success = false, ErrorMessage = ex.Message };
            }
        }

        private static async Task<string?> ReadLineWithTimeoutAsync(
            StreamReader reader, TimeSpan timeout)
        {
            using var cts  = new System.Threading.CancellationTokenSource(timeout);
            try   { return await reader.ReadLineAsync(cts.Token); }
            catch { return null; }
        }
    }
}
