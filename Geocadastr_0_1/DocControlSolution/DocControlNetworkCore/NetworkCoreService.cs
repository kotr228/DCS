using DocControlNetworkCore.Models;
using DocControlNetworkCore.Services;
using DocControlService.Shared;
using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DocControlNetworkCore
{
    /// <summary>
    /// Головний Windows Service для мережевого ядра
    /// </summary>
    public partial class NetworkCoreService : ServiceBase
    {
        private readonly bool _debugMode;
        private SelfIdentityService? _identityService;
        private DiscoveryService? _discoveryService;
        private PeerRegistryService? _peerRegistry;
        private CommandLayerService? _commandLayer;
        private FileTransferService? _fileTransfer;
        private SecurityService? _security;

        private PeerIdentity? _localIdentity;
        private string _sharedDirectory = @"C:\SharedFiles"; // За замовчуванням

        // BlackCat WAN Bridge
        private const string BlackCatPipeName = "BlackCatBridgePipe";
        private CancellationTokenSource? _bridgeCts;
        private Task? _bridgeTask;

        public NetworkCoreService(bool debugMode = false)
        {
            _debugMode = debugMode;
            InitializeComponent();

            ServiceName = "DocControlNetworkCore";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            Log("═══════════════════════════════════════════════════════");
            Log("  DocControl Network Core v1.0");
            Log("  Мережеве ядро для локальної мережі");
            Log("═══════════════════════════════════════════════════════");
            Log("");

            try
            {
                // 1. Ініціалізація ідентифікації
                Log("1. Ініціалізація ідентифікації...");
                _identityService = new SelfIdentityService(".");
                _localIdentity = _identityService.GetOrCreateIdentity();
                Log($"   ✓ Instance ID: {_localIdentity.InstanceId}");
                Log($"   ✓ User: {_localIdentity.UserName}@{_localIdentity.MachineName}");
                Log($"   ✓ IP: {_localIdentity.IpAddress}");
                Log($"   ✓ TCP Port: {_localIdentity.TcpPort}");
                Log($"   ✓ UDP Port: {_localIdentity.UdpPort}");
                Log("");

                // 2. Ініціалізація безпеки
                Log("2. Ініціалізація системи безпеки...");
                _security = new SecurityService(_sharedDirectory, whitelistEnabled: false);
                _security.UnauthorizedAccessAttempt += OnUnauthorizedAccess;
                Log($"   ✓ Базова директорія: {_sharedDirectory}");
                Log("");

                // 3. Ініціалізація реєстру вузлів
                Log("3. Ініціалізація реєстру вузлів...");
                _peerRegistry = new PeerRegistryService(timeoutSeconds: 30);
                _peerRegistry.PeerAdded += OnPeerAdded;
                _peerRegistry.PeerRemoved += OnPeerRemoved;
                _peerRegistry.PeersChanged += OnPeersChanged;
                _peerRegistry.Start();
                Log("   ✓ Peer Registry запущено");
                Log("");

                // 4. Ініціалізація Discovery Service
                Log("4. Ініціалізація Discovery Service...");
                _discoveryService = new DiscoveryService(_localIdentity, _localIdentity.UdpPort);
                _discoveryService.BroadcastIntervalSeconds = 10;
                _discoveryService.PeerDiscovered += OnPeerDiscovered;
                _discoveryService.PeerHeartbeat += OnPeerHeartbeat;
                _discoveryService.Start();
                Log("   ✓ Discovery Service запущено");
                Log("");

                // 5. Ініціалізація Command Layer
                Log("5. Ініціалізація Command Layer...");
                _commandLayer = new CommandLayerService(_localIdentity, _sharedDirectory);
                _commandLayer.CommandReceived += OnCommandReceived;
                _commandLayer.Start();
                Log("   ✓ Command Layer запущено");
                Log("");

                // 6. Ініціалізація File Transfer
                Log("6. Ініціалізація File Transfer...");
                _fileTransfer = new FileTransferService(_sharedDirectory);
                _fileTransfer.DownloadProgress += OnDownloadProgress;
                _fileTransfer.UploadProgress += OnUploadProgress;
                Log("   ✓ File Transfer готовий");
                Log("");

                // 7. BlackCat WAN Bridge (IPC сервер)
                Log("7. Запуск BlackCat Bridge (Named Pipe сервер)...");
                _bridgeCts  = new CancellationTokenSource();
                _bridgeTask = Task.Run(() => RunBlackCatBridgeAsync(_bridgeCts.Token));
                Log($"   ✓ BlackCat Bridge запущено (pipe: {BlackCatPipeName})");
                Log("");

                Log("═══════════════════════════════════════════════════════");
                Log("  ✓ Мережеве ядро успішно запущено!");
                Log("═══════════════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                Log($"❌ Помилка запуску сервісу: {ex.Message}", isError: true);
                throw;
            }
        }

        protected override void OnStop()
        {
            Log("Зупинка мережевого ядра...");

            try
            {
                _bridgeCts?.Cancel();
                try { _bridgeTask?.Wait(TimeSpan.FromSeconds(3)); } catch { }

                _discoveryService?.Stop();
                _commandLayer?.Stop();
                _peerRegistry?.Stop();

                _discoveryService?.Dispose();
                _commandLayer?.Dispose();
                _peerRegistry?.Dispose();
                _bridgeCts?.Dispose();

                Log("✓ Мережеве ядро зупинено");
            }
            catch (Exception ex)
            {
                Log($"Помилка при зупинці: {ex.Message}", isError: true);
            }
        }

        #region Event Handlers

        private void OnPeerDiscovered(PeerIdentity peer)
        {
            Log($"🔍 Виявлено новий вузол: {peer}");
            _peerRegistry?.AddOrUpdatePeer(peer);
        }

        private void OnPeerHeartbeat(PeerIdentity peer)
        {
            _peerRegistry?.AddOrUpdatePeer(peer);

            // Оновити статус пристрою в DocControlService (для підтримки онлайн статусу)
            Task.Run(async () =>
            {
                try
                {
                    await NotifyDocControlService(peer);
                }
                catch (Exception ex)
                {
                    // Ігноруємо помилки heartbeat оновлень (не критично)
                    Log($"[Heartbeat] Помилка оновлення статусу: {ex.Message}", isError: false);
                }
            });
        }

        private void OnPeerAdded(PeerIdentity peer)
        {
            Log($"➕ Вузол приєднався: {peer}");

            // Повідомити DocControlService про новий пристрій
            Task.Run(async () =>
            {
                try
                {
                    Log($"[DEBUG] Спроба відправки в DocControlService: {peer.UserName}@{peer.MachineName}");
                    await NotifyDocControlService(peer);
                }
                catch (Exception ex)
                {
                    Log($"[DEBUG] ❌ Критична помилка в Task.Run: {ex.Message}", isError: true);
                    Log($"[DEBUG] StackTrace: {ex.StackTrace}", isError: true);
                }
            });
        }

        private void OnPeerRemoved(PeerIdentity peer)
        {
            Log($"➖ Вузол відключився: {peer}");
        }

        private void OnPeersChanged(System.Collections.Generic.List<PeerIdentity> peers)
        {
            Log($"📊 Активних вузлів: {peers.Count}");
        }

        private void OnCommandReceived(NetworkCommand command, System.Net.IPEndPoint endpoint)
        {
            Log($"📨 Команда отримана: {command.Type} від {endpoint}");
        }

        private void OnDownloadProgress(string fileName, long current, long total)
        {
            if (current == total)
            {
                Log($"⬇️  Завантаження завершено: {fileName}");
            }
        }

        private void OnUploadProgress(string fileName, long current, long total)
        {
            if (current == total)
            {
                Log($"⬆️  Відправка завершена: {fileName}");
            }
        }

        private void OnUnauthorizedAccess(string resource, string reason)
        {
            Log($"⚠️  Несанкціонований доступ: {resource} - {reason}", isError: true);
        }

        #endregion

        #region Utility Methods

        private void Log(string message, bool isError = false)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logMessage = $"[{timestamp}] {message}";

            if (_debugMode)
            {
                if (isError)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(logMessage);
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine(logMessage);
                }
            }
            else
            {
                try
                {
                    System.Diagnostics.EventLog.WriteEntry(ServiceName, logMessage,
                        isError ? System.Diagnostics.EventLogEntryType.Error : System.Diagnostics.EventLogEntryType.Information);
                }
                catch
                {
                    // Ігноруємо помилки логування
                }
            }
        }

        /// <summary>
        /// Повідомити DocControlService про новий пристрій через Named Pipe
        /// </summary>
        private async Task NotifyDocControlService(PeerIdentity peer)
        {
            try
            {
                Log($"[NetworkCore] 🔍 Step 1: Перевірка чи це власний вузол...");

                // Перевірити чи це не власний вузол
                if (_localIdentity != null &&
                    peer.UserName == _localIdentity.UserName &&
                    peer.MachineName == _localIdentity.MachineName &&
                    peer.IpAddress == _localIdentity.IpAddress)
                {
                    Log($"[NetworkCore] ⏭️  Пропускаємо власний вузол: {peer}");
                    return;
                }

                string deviceName = $"{peer.UserName}@{peer.MachineName} ({peer.IpAddress})";
                Log($"[NetworkCore] 🔍 Step 2: Створення Named Pipe клієнта для {deviceName}");

                using (var pipeClient = new NamedPipeClientStream(".", "DocControlServicePipe", PipeDirection.InOut))
                {
                    Log($"[NetworkCore] 🔍 Step 3: Спроба підключення до pipe (timeout 5000ms)...");

                    // Спроба підключення з таймаутом
                    await pipeClient.ConnectAsync(5000);

                    Log($"[NetworkCore] 🔍 Step 4: Підключено = {pipeClient.IsConnected}");

                    if (!pipeClient.IsConnected)
                    {
                        Log($"[NetworkCore] ⚠️  Не вдалося підключитися до DocControlService", isError: true);
                        return;
                    }

                    Log($"[NetworkCore] 🔍 Step 5: Створення команди AddDevice...");

                    // Створити команду AddDevice
                    var command = new ServiceCommand
                    {
                        Type = DocControlService.Shared.CommandType.AddDevice,
                        Data = JsonSerializer.Serialize(new DeviceModel
                        {
                            Name = deviceName,
                            Access = false // За замовчуванням доступ заборонений
                        })
                    };

                    Log($"[NetworkCore] 🔍 Step 6: Серіалізація команди...");

                    // Відправити команду (ВАЖЛИВО: додаємо \n для StreamReader.ReadLineAsync на сервері)
                    string requestJson = JsonSerializer.Serialize(command);
                    byte[] requestData = Encoding.UTF8.GetBytes(requestJson + "\n");  // Додаємо \n!

                    Log($"[NetworkCore] 🔍 Step 7: Відправка {requestData.Length} байт...");
                    await pipeClient.WriteAsync(requestData, 0, requestData.Length);
                    await pipeClient.FlushAsync();

                    Log($"[NetworkCore] 🔍 Step 8: Очікування відповіді...");

                    // Отримати відповідь (через StreamReader для ReadLineAsync)
                    string responseJson;
                    using (var reader = new StreamReader(pipeClient, Encoding.UTF8, false, 1024, true))
                    {
                        responseJson = await reader.ReadLineAsync();
                    }

                    Log($"[NetworkCore] 🔍 Step 9: Отримано {responseJson?.Length ?? 0} символів, десеріалізація...");
                    var response = JsonSerializer.Deserialize<ServiceResponse>(responseJson);

                    Log($"[NetworkCore] 🔍 Step 10: Обробка відповіді...");
                    if (response != null && response.Success)
                    {
                        Log($"[NetworkCore] ✅ Пристрій передано в DocControlService: {deviceName}");
                    }
                    else
                    {
                        Log($"[NetworkCore] ❌ Помилка додавання пристрою: {response?.Message}", isError: true);
                    }
                }
            }
            catch (System.TimeoutException ex)
            {
                Log($"[NetworkCore] ⏱️  Timeout при підключенні до DocControlService: {ex.Message}", isError: true);
            }
            catch (Exception ex)
            {
                Log($"[NetworkCore] ❌ Помилка відправки в DocControlService: {ex.GetType().Name} - {ex.Message}", isError: true);
                Log($"[NetworkCore] ❌ StackTrace: {ex.StackTrace}", isError: true);
            }
            finally
            {
                Log($"[NetworkCore] 🔍 NotifyDocControlService завершено для {peer.UserName}@{peer.MachineName}");
            }
        }

        // ─── BlackCat WAN Bridge ──────────────────────────────────────────────

        /// <summary>
        /// Нескінченний цикл Named Pipe сервера, що приймає паспорти від BlackCat.
        /// Кожне підключення обробляється окремо — сервер одразу готовий до наступного.
        /// </summary>
        private async Task RunBlackCatBridgeAsync(CancellationToken cancellationToken)
        {
            Log("[Bridge] Pipe сервер запущено");

            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        BlackCatPipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(cancellationToken);

                    // Обробити в окремій задачі, щоб сервер міг прийняти наступне підключення
                    var captured = pipe;
                    pipe = null; // не Dispose у finally
                    _ = Task.Run(() => HandleBridgeConnectionAsync(captured), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"[Bridge] Помилка pipe сервера: {ex.Message}", isError: true);
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    pipe?.Dispose();
                }
            }

            Log("[Bridge] Pipe сервер зупинено");
        }

        private async Task HandleBridgeConnectionAsync(NamedPipeServerStream pipe)
        {
            try
            {
                using (pipe)
                {
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) return;

                    var passport = JsonSerializer.Deserialize<BlackCatPassport>(line);
                    if (passport == null) return;

                    Log($"[Bridge] Паспорт від BlackCat: {passport.BlackId} ({passport.Status})");

                    if (passport.Status == "Offline")
                    {
                        // Знайти та видалити відповідний вузол
                        var existing = FindPeerByBlackId(passport.BlackId);
                        if (existing != null)
                        {
                            _peerRegistry?.RemovePeer(existing.InstanceId);
                            Log($"[Bridge] WAN вузол відключився: {passport.BlackId}");
                        }
                    }
                    else
                    {
                        // Зареєструвати або оновити WAN вузол
                        var peer = new PeerIdentity
                        {
                            InstanceId       = DeriveGuidFromBlackId(passport.BlackId),
                            UserName         = passport.DisplayName,
                            MachineName      = passport.BlackId,
                            IpAddress        = passport.IpAddress,
                            TcpPort          = passport.Port > 0 ? passport.Port : 9999,
                            UdpPort          = 0,
                            LastSeen         = DateTime.Now,
                            ProtocolVersion  = "BlackCat/1.0",
                            IsRemoteTunnel   = true,
                            BlackId          = passport.BlackId
                        };

                        _peerRegistry?.AddOrUpdatePeer(peer);
                        Log($"[Bridge] WAN вузол зареєстровано: {passport.BlackId} ({passport.IpAddress})");

                        // Сповістити DocControlService про новий WAN-пристрій (той самий шлях що й LAN)
                        _ = Task.Run(() => NotifyDocControlService(peer));
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[Bridge] Помилка обробки паспорту: {ex.Message}", isError: true);
            }
        }

        private PeerIdentity? FindPeerByBlackId(string blackId)
        {
            if (_peerRegistry == null) return null;
            var peers = _peerRegistry.GetAllPeers();
            foreach (var p in peers)
                if (p.BlackId == blackId) return p;
            return null;
        }

        private static Guid DeriveGuidFromBlackId(string blackId)
        {
            // Стабільний Guid з рядка через MD5 (детермінований)
            var hash = MD5.HashData(Encoding.UTF8.GetBytes("blackcat:" + blackId));
            return new Guid(hash);
        }

        private void InitializeComponent()
        {
            this.ServiceName = "DocControlNetworkCore";
        }

        /// <summary>
        /// Метод для запуску в Debug режимі через консоль
        /// </summary>
        public void StartDebug(string[] args)
        {
            OnStart(args);

            Console.WriteLine();
            Console.WriteLine("Сервіс запущено. Натисніть клавішу для команд:");
            Console.WriteLine("  Q - Зупинити");
            Console.WriteLine("  S - Статус");
            Console.WriteLine("  P - Показати вузли");
            Console.WriteLine("  B - Відправити broadcast");
            Console.WriteLine();

            bool running = true;
            while (running)
            {
                var key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.Q:
                        Log("Зупинка сервісу...");
                        running = false;
                        break;

                    case ConsoleKey.S:
                        ShowStatus();
                        break;

                    case ConsoleKey.P:
                        ShowPeers();
                        break;

                    case ConsoleKey.B:
                        _discoveryService?.SendImmediateBroadcastAsync().Wait();
                        Log("Broadcast відправлено");
                        break;
                }
            }

            OnStop();
            Console.WriteLine("Натисніть будь-яку клавішу для виходу...");
            Console.ReadKey();
        }

        private void ShowStatus()
        {
            Console.WriteLine();
            Console.WriteLine("═══ СТАТУС СЕРВІСУ ═══");
            Console.WriteLine($"Instance ID: {_localIdentity?.InstanceId}");
            Console.WriteLine($"Користувач: {_localIdentity?.UserName}@{_localIdentity?.MachineName}");
            Console.WriteLine($"IP: {_localIdentity?.IpAddress}");
            Console.WriteLine($"TCP Port: {_localIdentity?.TcpPort}");
            Console.WriteLine($"UDP Port: {_localIdentity?.UdpPort}");
            Console.WriteLine($"Активних вузлів: {_peerRegistry?.GetPeerCount() ?? 0}");
            Console.WriteLine($"Спільна директорія: {_sharedDirectory}");
            Console.WriteLine("══════════════════════");
            Console.WriteLine();
        }

        private void ShowPeers()
        {
            Console.WriteLine();
            Console.WriteLine("═══ АКТИВНІ ВУЗЛИ ═══");

            var peers = _peerRegistry?.GetAllPeers();
            if (peers == null || peers.Count == 0)
            {
                Console.WriteLine("  (немає активних вузлів)");
            }
            else
            {
                foreach (var peer in peers)
                {
                    var lastSeenAgo = DateTime.Now - peer.LastSeen;
                    Console.WriteLine($"  • {peer} (останній сигнал: {lastSeenAgo.TotalSeconds:F0}s тому)");
                }
            }

            Console.WriteLine("═════════════════════");
            Console.WriteLine();
        }

        #endregion
    }
}
