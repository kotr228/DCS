using DocControlNetworkCore.Models;
using DocControlNetworkCore.Services;
using System;
using System.ServiceProcess;
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
                _discoveryService?.Stop();
                _commandLayer?.Stop();
                _peerRegistry?.Stop();

                _discoveryService?.Dispose();
                _commandLayer?.Dispose();
                _peerRegistry?.Dispose();

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
        }

        private void OnPeerAdded(PeerIdentity peer)
        {
            Log($"➕ Вузол приєднався: {peer}");
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
