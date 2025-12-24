using DocControlNetworkCore.Models;
using DocControlNetworkCore.Services;
using DocControlService.Data;
using DocControlService.Models;
using DocControlService.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DocControlService.Services
{
    /// <summary>
    /// Координатор між локальною та мережевою файловими системами
    /// </summary>
    public class FileSystemCoordinator
    {
        private readonly DatabaseManager _dbManager;
        private readonly DeviceRepository _deviceRepository;
        private readonly LocalFileSystemService _localFileSystem;
        private readonly ConcurrentDictionary<Guid, RemoteFileSystemService> _remoteFileSystems;

        // Компоненти NetworkCore
        private SelfIdentityService? _identityService;
        private DiscoveryService? _discoveryService;
        private PeerRegistryService? _peerRegistry;
        private CommandLayerService? _commandLayer;
        private FileTransferService? _fileTransfer;
        private SecurityService? _securityService;

        private PeerIdentity? _localIdentity;
        private bool _isNetworkCoreStarted = false;

        /// <summary>
        /// Подія зміни списку віддалених вузлів
        /// </summary>
        public event Action<List<RemoteNode>>? RemoteNodesChanged;

        public FileSystemCoordinator(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
            _deviceRepository = new DeviceRepository(dbManager);
            _localFileSystem = new LocalFileSystemService(dbManager);
            _remoteFileSystems = new ConcurrentDictionary<Guid, RemoteFileSystemService>();
        }

        /// <summary>
        /// Запустити мережеве ядро
        /// </summary>
        public void StartNetworkCore(string sharedDirectory)
        {
            if (_isNetworkCoreStarted)
                return;

            Console.WriteLine("[FileSystemCoordinator] Запуск мережевого ядра...");

            // 1. Ініціалізація ідентифікації
            _identityService = new SelfIdentityService(".");
            _localIdentity = _identityService.GetOrCreateIdentity();

            // 2. Ініціалізація безпеки
            _securityService = new SecurityService(sharedDirectory, whitelistEnabled: false);

            // 3. Ініціалізація реєстру вузлів
            _peerRegistry = new PeerRegistryService(timeoutSeconds: 30);
            _peerRegistry.PeerAdded += OnPeerAdded;
            _peerRegistry.PeerRemoved += OnPeerRemoved;
            _peerRegistry.PeersChanged += OnPeersChanged;
            _peerRegistry.Start();

            // 4. Ініціалізація Discovery Service
            _discoveryService = new DiscoveryService(_localIdentity, _localIdentity.UdpPort);
            _discoveryService.BroadcastIntervalSeconds = 10;
            _discoveryService.PeerDiscovered += OnPeerDiscovered;
            _discoveryService.PeerHeartbeat += (peer) => _peerRegistry?.AddOrUpdatePeer(peer);
            _discoveryService.Start();

            // 5. Ініціалізація Command Layer
            _commandLayer = new CommandLayerService(_localIdentity, sharedDirectory);
            _commandLayer.Start();

            // 6. Ініціалізація File Transfer
            _fileTransfer = new FileTransferService(sharedDirectory);

            _isNetworkCoreStarted = true;
            Console.WriteLine("[FileSystemCoordinator] Мережеве ядро запущено");
        }

        /// <summary>
        /// Зупинити мережеве ядро
        /// </summary>
        public void StopNetworkCore()
        {
            if (!_isNetworkCoreStarted)
                return;

            Console.WriteLine("[FileSystemCoordinator] Зупинка мережевого ядра...");

            _discoveryService?.Stop();
            _commandLayer?.Stop();
            _peerRegistry?.Stop();

            _discoveryService?.Dispose();
            _commandLayer?.Dispose();
            _peerRegistry?.Dispose();

            _isNetworkCoreStarted = false;
            Console.WriteLine("[FileSystemCoordinator] Мережеве ядро зупинено");
        }

        /// <summary>
        /// Отримати локальну файлову систему
        /// </summary>
        public IFileSystemService GetLocalFileSystem()
        {
            return _localFileSystem;
        }

        /// <summary>
        /// Отримати віддалену файлову систему за ID вузла
        /// </summary>
        public IFileSystemService? GetRemoteFileSystem(Guid peerId)
        {
            if (_remoteFileSystems.TryGetValue(peerId, out var remoteFs))
                return remoteFs;

            return null;
        }

        /// <summary>
        /// Отримати всі активні віддалені вузли
        /// </summary>
        public List<RemoteNode> GetRemoteNodes()
        {
            if (_peerRegistry == null)
                return new List<RemoteNode>();

            var peers = _peerRegistry.GetAllPeers();
            return peers.Select(p => new RemoteNode
            {
                InstanceId = p.InstanceId,
                UserName = p.UserName,
                MachineName = p.MachineName,
                IpAddress = p.IpAddress,
                TcpPort = p.TcpPort,
                IsOnline = true,
                LastSeen = p.LastSeen
            }).ToList();
        }

        /// <summary>
        /// Чи запущено мережеве ядро
        /// </summary>
        public bool IsNetworkCoreRunning => _isNetworkCoreStarted;

        /// <summary>
        /// Отримати локальну ідентичність
        /// </summary>
        public PeerIdentity? GetLocalIdentity() => _localIdentity;

        #region Event Handlers

        private void OnPeerDiscovered(PeerIdentity peer)
        {
            Console.WriteLine($"[FileSystemCoordinator] Виявлено вузол: {peer}");
            _peerRegistry?.AddOrUpdatePeer(peer);
        }

        private void OnPeerAdded(PeerIdentity peer)
        {
            Console.WriteLine($"[FileSystemCoordinator] Вузол приєднався: {peer}");

            // Зберегти пристрій в БД
            try
            {
                string deviceName = $"{peer.UserName}@{peer.MachineName} ({peer.IpAddress})";
                _deviceRepository.GetOrCreateDevice(deviceName, defaultAccess: false);
                Console.WriteLine($"[FileSystemCoordinator] Пристрій збережено в БД: {deviceName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileSystemCoordinator] Помилка збереження пристрою: {ex.Message}");
            }

            // Створити RemoteFileSystemService для нового вузла
            if (_commandLayer != null && _fileTransfer != null)
            {
                var remoteFs = new RemoteFileSystemService(peer, _commandLayer, _fileTransfer);
                _remoteFileSystems.TryAdd(peer.InstanceId, remoteFs);
            }

            // Викликати подію для UI
            NotifyRemoteNodesChanged();
        }

        private void OnPeerRemoved(PeerIdentity peer)
        {
            Console.WriteLine($"[FileSystemCoordinator] Вузол відключився: {peer}");

            // Видалити RemoteFileSystemService
            _remoteFileSystems.TryRemove(peer.InstanceId, out _);

            // Викликати подію для UI
            NotifyRemoteNodesChanged();
        }

        private void OnPeersChanged(List<PeerIdentity> peers)
        {
            NotifyRemoteNodesChanged();
        }

        private void NotifyRemoteNodesChanged()
        {
            var nodes = GetRemoteNodes();
            RemoteNodesChanged?.Invoke(nodes);
        }

        #endregion
    }
}
