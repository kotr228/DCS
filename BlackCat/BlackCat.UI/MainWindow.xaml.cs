using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BlackCat.Core;
using BlackCat.Core.Configuration;
using BlackCat.Core.Data;
using BlackCat.Core.Services;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;
using BlackCat.UI.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;
using NetworkProtocol = BlackCat.Shared.Enums.ProtocolType;

namespace BlackCat.UI;

public partial class MainWindow : Window
{
    private FirewallCoordinator? _coordinator;
    private TunnelManager? _tunnelManager;
    private RelayServerService? _embeddedRelayServer;

    // P2P manual signaling
    private System.Net.Sockets.UdpClient? _p2pUdpSocket;
    private CancellationTokenSource? _p2pPunchCts;
    private BlackCat.Shared.Models.BlackID? _ourBlackID;

    // Маркери типів пакетів
    private static readonly byte[] NodeInfoMarker     = { 0xBC, 0x1D }; // node info exchange
    private static readonly byte[] FileTransferMarker = { 0xBC, 0x1E }; // file transfer

    // Шляхи до тимчасових папок
    private static readonly string TempDataDir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_data");
    private static readonly string TempFilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_files");
    private readonly DispatcherTimer _updateTimer;
    private readonly RuleRepository _ruleRepository;
    private readonly ProcessLookupService _processLookupService;
    private readonly BlackIDRepository _blackIDRepository;
    private readonly BlackCatDatabase _database;
    private readonly HandshakeService _handshakeService;
    private readonly ConnectionMonitorService _connectionMonitor;
    private readonly ConnectionEventRepository _eventRepository;
    private readonly BlackIDService _blackIDService;

    // Chart ViewModel and background data collector
    private readonly TrafficChartViewModel _chartVm;
    private BackgroundTrafficCollector? _trafficCollector;

    // Тунелі Black-ID
    private readonly ObservableCollection<TunnelNodeItem> _tunnelNodes = new();
    private TunnelNodeItem? _selectedTunnel;
    private readonly PeerNodeRepository _peerNodeRepository;

    // DCS directory mirror integration
    private DcsIntegrationService?  _dcsIntegration;
    private BlackCatCommandService? _commandService;

    public MainWindow()
    {
        InitializeComponent();

        _ruleRepository = new RuleRepository();
        _processLookupService = new ProcessLookupService();

        // Ініціалізувати database та repository для Black-ID
        _database = new BlackCatDatabase("blackcat.db");
        _blackIDRepository = new BlackIDRepository(_database);
        _peerNodeRepository = new PeerNodeRepository(_database);
        _eventRepository = new ConnectionEventRepository(_database);

        // Ініціалізувати сервіси для з'єднань
        _blackIDService = new BlackIDService();
        _handshakeService = new HandshakeService(_blackIDService, _peerNodeRepository, _eventRepository);
        _connectionMonitor = new ConnectionMonitorService(_peerNodeRepository, _eventRepository);

        // Створити папки для тимчасових файлів
        Directory.CreateDirectory(TempDataDir);
        Directory.CreateDirectory(TempFilesDir);

        // Ініціалізувати ViewModel графіку
        _chartVm = new TrafficChartViewModel();

        // Налаштування графіків та статистики через ViewModel
        InitializeCharts();

        // Прив'язати таблицю статистики до ViewModel
        ProcessStatsDataGrid.ItemsSource = _chartVm.ProcessStats;

        // Підписка на зміни stat-label рядків з ViewModel
        _chartVm.PropertyChanged += OnChartVmPropertyChanged;

        // Налаштування тунелів
        TunnelsDataGrid.ItemsSource = _tunnelNodes;
        LoadTunnels();

        // Таймер оновлення UI
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000) // Оновлювати кожну секунду
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        // Завантажити правила
        LoadRules();
    }

    private void InitializeCharts()
    {
        // Bind chart to ViewModel's data sources
        PacketsChart.Series = _chartVm.Series;
        PacketsChart.AxisX[0].LabelFormatter = _chartVm.LabelFormatter;
    }

    private void LoadRules()
    {
        var rules = _ruleRepository.GetAllRules();
        RulesDataGrid.ItemsSource = rules;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AddLog("Запуск брандмауера...");

            // TODO: Зчитати з налаштувань
            string masterSecret = "YourSecretPasswordHere";

            _coordinator = new FirewallCoordinator(masterSecret);
            _coordinator.LogMessage += OnLogMessage;
            _coordinator.StatisticsUpdated += OnStatisticsUpdated;

            await _coordinator.StartAsync();

            // Ініціалізувати TunnelManager
            var ourBlackID = _blackIDRepository.GetActiveBlackID();
            if (ourBlackID != null)
            {
                _tunnelManager = new TunnelManager(
                    _blackIDService,
                    _handshakeService,
                    _connectionMonitor,
                    _eventRepository,
                    masterSecret,
                    listenPort: 9999
                );

                // Підписатись на події
                _tunnelManager.ConnectionEstablished += OnTunnelConnectionEstablished;
                _tunnelManager.ConnectionLost += OnTunnelConnectionLost;
                _tunnelManager.ConnectionFailed += OnTunnelConnectionFailed;
                _tunnelManager.DataReceived += OnTunnelDataReceived;
                _tunnelManager.IncomingConnectionRequest += OnIncomingConnectionRequest;
                _tunnelManager.UPnPStatusChanged        += OnUPnPStatusChanged;
                _tunnelManager.FirewallStatusChanged    += (_, msg) => Dispatcher.Invoke(() => AddLog(msg));
                _tunnelManager.NatDiagnosticReady       += OnNatDiagnosticReady;
                _tunnelManager.RelayStatusChanged       += (_, msg) => Dispatcher.Invoke(() => AddLog(msg));

                // DCS directory mirror integration
                var dcsTransferRepo = new DcsTransferRepository(_database);
                _dcsIntegration = new DcsIntegrationService(
                    _tunnelManager, _eventRepository, dcsTransferRepo, ourBlackID.FullID);
                _dcsIntegration.FileReceived += OnDcsMirrorFileReceived;

                _commandService = new BlackCatCommandService(_dcsIntegration);
                _commandService.Start();

                // Запустити сервер для прийому вхідних з'єднань
                _ourBlackID = ourBlackID;
                await _tunnelManager.StartServerAsync(ourBlackID);

                AddLog($"✅ BlackCat запущено. Використовуй вкладку 🕳️ P2P для з'єднання.");

                // Показати IP — спочатку локальну, потім реальну публічну через ipify.org
                _ = Task.Run(async () =>
                {
                    var netInfo = new NetworkInfoService();
                    var localIP = netInfo.GetLocalIPAddress();
                    Dispatcher.Invoke(() => MyIPLabel.Text = $"{localIP} (локальна)");

                    // Чекаємо до 15 секунд поки ipify.org поверне справжню публічну IP
                    for (int i = 0; i < 15; i++)
                    {
                        await Task.Delay(1000);
                        var extIP = _tunnelManager?.ExternalIP;
                        if (!string.IsNullOrEmpty(extIP))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                MyIPLabel.Text = $"{extIP} (інтернет)";
                                AddLog($"🌐 Ваша публічна IP: {extIP}  ← поділіться для підключення через інтернет");
                            });
                            break;
                        }
                    }
                });
            }
            else
            {
                AddLog("⚠️ Black-ID не налаштовано - тунелі недоступні");
            }

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;

            // Background collector: P/Invoke runs off UI thread, points fed into ViewModel queue
            _trafficCollector = new BackgroundTrafficCollector(
                _chartVm.Enqueue,
                _processLookupService,
                () => _coordinator?.Statistics);
            _trafficCollector.Start();
            _chartVm.Start();

            _updateTimer.Start();

            AddLog("✅ Брандмауер запущено");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Адреса вже використовується"))
        {
            // Помилка Raw Socket - показати детальну інформацію
            AddLog($"⚠️ {ex.Message}");
            MessageBox.Show(ex.Message,
                "Попередження", MessageBoxButton.OK, MessageBoxImage.Warning);

            AddLog("💡 Перевірте файл check_network_processes.md для вирішення проблеми");
            AddLog("📊 Програма працює в тестовому режимі");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Критична помилка запуску: {ex.Message}");
            MessageBox.Show($"Критична помилка запуску: {ex.Message}\n\nБрандмауер не може бути запущено.",
                "Критична помилка", MessageBoxButton.OK, MessageBoxImage.Error);

            // Очистити coordinator при критичній помилці
            _coordinator?.Dispose();
            _coordinator = null;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AddLog("Зупинка брандмауера...");

            _embeddedRelayServer?.Dispose();
            _embeddedRelayServer = null;

            _commandService?.Stop();
            _commandService?.Dispose();
            _commandService = null;
            _dcsIntegration?.Dispose();
            _dcsIntegration = null;

            _tunnelManager?.Dispose();
            _tunnelManager = null;

            _connectionMonitor?.Stop();

            _coordinator?.Stop();
            _coordinator?.Dispose();
            _coordinator = null;

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;

            _trafficCollector?.Stop();
            _trafficCollector?.Dispose();
            _trafficCollector = null;
            _chartVm.Stop();

            _updateTimer.Stop();

            AddLog("✅ Брандмауер зупинено");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Помилка зупинки: {ex.Message}");
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_coordinator)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true)
        {
            // Налаштування збережені
            AddLog("✅ Налаштування збережено");
        }
    }

    private void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var rulesWindow = new RulesManagementWindow(_coordinator)
        {
            Owner = this
        };

        rulesWindow.ShowDialog();

        // Перезавантажити список правил
        LoadRules();
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    private void OnChartVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // ViewModel fires on the UI thread (DispatcherTimer) — no Invoke needed
        switch (e.PropertyName)
        {
            case nameof(TrafficChartViewModel.TotalPackets):
                TotalPacketsText.Text    = _chartVm.TotalPackets;    break;
            case nameof(TrafficChartViewModel.AllowedPackets):
                AllowedPacketsText.Text  = _chartVm.AllowedPackets;  break;
            case nameof(TrafficChartViewModel.BlockedPackets):
                BlockedPacketsText.Text  = _chartVm.BlockedPackets;  break;
            case nameof(TrafficChartViewModel.TunneledPackets):
                TunneledPacketsText.Text = _chartVm.TunneledPackets; break;
            case nameof(TrafficChartViewModel.SpeedText):
                SpeedText.Text           = _chartVm.SpeedText;       break;
            case nameof(TrafficChartViewModel.UptimeText):
                UptimeText.Text          = _chartVm.UptimeText;      break;
        }
    }

    private void OnLogMessage(object? sender, string message)
    {
        Dispatcher.Invoke(() => AddLog(message));
    }

    private void OnStatisticsUpdated(object? sender, FirewallStatistics stats)
    {
        // Оновлення буде в таймері
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (_coordinator == null) return;

        var stats = _coordinator.Statistics;

        // Stat labels are updated by TrafficChartViewModel at 20 Hz via its own timer.
        // The 1-second timer here handles only lightweight tunnel-panel updates.

        UpdateTunnelStatus(stats.TunnelStatus);

        if (_selectedTunnel != null && _selectedTunnel.IsConnected)
        {
            if (_selectedTunnel.LastHandshake != DateTime.MinValue)
                _selectedTunnel.ConnectionTime = DateTime.Now - _selectedTunnel.LastHandshake;
            TunnelUptime.Text        = _selectedTunnel.ConnectionTime.ToString(@"hh\:mm\:ss");
            TunnelSentBytes.Text     = FormatBytes(_selectedTunnel.SentBytes);
            TunnelReceivedBytes.Text = FormatBytes(_selectedTunnel.ReceivedBytes);
        }
    }

    private void UpdateTunnelStatus(TunnelStatus status)
    {
        switch (status)
        {
            case TunnelStatus.Disconnected:
                TunnelStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(244, 135, 113));
                TunnelStatusText.Text = "Відключено";
                break;
            case TunnelStatus.Connecting:
                TunnelStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 220, 170));
                TunnelStatusText.Text = "Підключення...";
                break;
            case TunnelStatus.Connected:
                TunnelStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(106, 153, 85));
                TunnelStatusText.Text = "Підключено";
                break;
            case TunnelStatus.Error:
                TunnelStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0));
                TunnelStatusText.Text = "Помилка";
                break;
        }
    }

    // UpdateProcessTraffic and UpdateProcessStatistics are removed.
    // All chart and process-stats logic now lives in TrafficChartViewModel,
    // fed by BackgroundTrafficCollector running on a background Task.

    private void AddLog(string message)
    {
        LogTextBox.AppendText($"{message}\n");
        LogTextBox.ScrollToEnd();
    }

    #region Tunnel Management

    /// <summary>
    /// Завантажити список збережених тунелів
    /// </summary>
    private void LoadTunnels()
    {
        _tunnelNodes.Clear();

        // Відобразити поточний Black-ID - спочатку з БД, потім з coordinator
        try
        {
            var savedBlackID = _blackIDRepository.GetActiveBlackID();
            if (savedBlackID != null)
            {
                CurrentBlackIDLabel.Text = savedBlackID.FullID;
                System.Diagnostics.Debug.WriteLine($"Loaded Black-ID from database: {savedBlackID.FullID}");
            }
            else if (_coordinator?.CurrentBlackID != null)
            {
                CurrentBlackIDLabel.Text = _coordinator.CurrentBlackID.FullID;
                System.Diagnostics.Debug.WriteLine($"Loaded Black-ID from coordinator: {_coordinator.CurrentBlackID.FullID}");
            }
            else
            {
                CurrentBlackIDLabel.Text = "Не налаштовано (створіть в Налаштуваннях)";
                System.Diagnostics.Debug.WriteLine("No Black-ID found");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading Black-ID: {ex.Message}");
            CurrentBlackIDLabel.Text = "Помилка завантаження";
        }

        // Завантажити збережені вузли з БД
        try
        {
            var peerNodes = _peerNodeRepository.GetAllPeerNodes();

            foreach (var peer in peerNodes.Where(p => p.IsActive))
            {
                var tunnelItem = new TunnelNodeItem
                {
                    BlackID = peer.BlackID,
                    IPAddress = peer.Address,
                    Port = peer.Port,
                    DisplayName = peer.DisplayName,
                    IsTrusted = peer.IsTrusted,
                    IsConnected = false,
                    StatusDisplay = "Відключено"
                };

                _tunnelNodes.Add(tunnelItem);
            }

            System.Diagnostics.Debug.WriteLine($"Loaded {_tunnelNodes.Count} peer nodes from database");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading peer nodes: {ex.Message}");
        }
    }

    /// <summary>
    /// Обробка зміни вибору тунелю
    /// </summary>
    private void TunnelsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TunnelsDataGrid.SelectedItem is TunnelNodeItem tunnel)
        {
            _selectedTunnel = tunnel;
            LoadTunnelDetails(tunnel);
            RemoveTunnelButton.IsEnabled = true;
            ConnectTunnelButton.IsEnabled = !tunnel.IsConnected;
            DisconnectTunnelButton.IsEnabled = tunnel.IsConnected;
        }
        else
        {
            ClearTunnelDetails();
            RemoveTunnelButton.IsEnabled = false;
            ConnectTunnelButton.IsEnabled = false;
            DisconnectTunnelButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// Завантажити деталі тунелю в панель
    /// </summary>
    private void LoadTunnelDetails(TunnelNodeItem tunnel)
    {
        TunnelBlackIDTextBox.Text = tunnel.BlackID;
        TunnelIPTextBox.Text = tunnel.IPAddress;
        TunnelPortTextBox.Text = tunnel.Port.ToString();

        UpdateTunnelConnectionStatus(tunnel);

        TunnelSentBytes.Text = FormatBytes(tunnel.SentBytes);
        TunnelReceivedBytes.Text = FormatBytes(tunnel.ReceivedBytes);
        TunnelUptime.Text = tunnel.ConnectionTime.ToString(@"hh\:mm\:ss");
        TunnelLastHandshake.Text = tunnel.LastHandshake != DateTime.MinValue
            ? tunnel.LastHandshake.ToString("dd.MM.yyyy HH:mm:ss")
            : "Ніколи";
    }

    /// <summary>
    /// Очистити панель деталей
    /// </summary>
    private void ClearTunnelDetails()
    {
        TunnelBlackIDTextBox.Text = string.Empty;
        TunnelIPTextBox.Text = string.Empty;
        TunnelPortTextBox.Text = "9999";
        TunnelConnectionStatus.Text = "Не підключено";
        TunnelConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        TunnelSentBytes.Text = "0 B";
        TunnelReceivedBytes.Text = "0 B";
        TunnelUptime.Text = "00:00:00";
        TunnelLastHandshake.Text = "Ніколи";
    }

    /// <summary>
    /// Оновити статус підключення тунелю
    /// </summary>
    private void UpdateTunnelConnectionStatus(TunnelNodeItem tunnel)
    {
        if (tunnel.IsConnected)
        {
            TunnelConnectionStatus.Text = "Підключено ✓";
            TunnelConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(106, 153, 85));
        }
        else if (tunnel.IsConnecting)
        {
            TunnelConnectionStatus.Text = "Підключення...";
            TunnelConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 220, 170));
        }
        else
        {
            TunnelConnectionStatus.Text = "Не підключено";
            TunnelConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        }
    }

    /// <summary>
    /// Форматування байтів в читабельний вигляд
    /// </summary>
    private string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        else if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        else if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        else
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    /// <summary>
    /// Додати новий тунель
    /// </summary>
    private void AddTunnelButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new AddPeerNodeDialog(_peerNodeRepository)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.CreatedPeerNode != null)
            {
                // Перезавантажити список з БД
                LoadTunnels();

                AddLog($"✅ Додано вузол: {dialog.CreatedPeerNode.BlackID} ({dialog.CreatedPeerNode.DisplayName})");
            }
        }
        catch (Exception ex)
        {
            AddLog($"❌ Помилка додавання вузла: {ex.Message}");

            MessageBox.Show($"Помилка додавання вузла:\n{ex.Message}",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Видалити вибраний тунель
    /// </summary>
    private void RemoveTunnelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTunnel == null)
            return;

        try
        {
            var result = MessageBox.Show(
                $"Видалити вузол з телефонної книги?\n\n" +
                $"Black-ID: {_selectedTunnel.BlackID}\n" +
                $"Назва: {_selectedTunnel.DisplayName}\n" +
                $"Адреса: {_selectedTunnel.IPAddress}:{_selectedTunnel.Port}\n\n" +
                $"Цю дію НЕ можна скасувати!",
                "Підтвердження видалення",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Знайти вузол в БД за Black-ID
                var peerNode = _peerNodeRepository.GetPeerNodeByBlackID(_selectedTunnel.BlackID);
                if (peerNode != null)
                {
                    _peerNodeRepository.DeletePeerNode(peerNode.Id);

                    AddLog($"🗑️ Видалено вузол: {_selectedTunnel.BlackID}");

                    // Перезавантажити список з БД
                    LoadTunnels();

                    ClearTunnelDetails();
                    _selectedTunnel = null;

                    MessageBox.Show($"✅ Вузол видалено з телефонної книги.",
                        "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"⚠️ Вузол не знайдено в базі даних.",
                        "Попередження", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            AddLog($"❌ Помилка видалення вузла: {ex.Message}");

            MessageBox.Show($"Помилка видалення вузла:\n{ex.Message}",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Підключитися до вибраного тунелю
    /// </summary>
    private async void ConnectTunnelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTunnel == null || _tunnelManager == null)
        {
            MessageBox.Show("TunnelManager не ініціалізовано.\n\nПереконайтесь що:\n• Брандмауер запущено\n• Black-ID налаштовано в Налаштуваннях",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _selectedTunnel.IsConnecting = true;
            _selectedTunnel.StatusDisplay = "Підключення...";
            UpdateTunnelConnectionStatus(_selectedTunnel);
            ConnectTunnelButton.IsEnabled = false;

            AddLog($"🔌 Підключення до {_selectedTunnel.BlackID}...");
            AddLog($"   IP: {_selectedTunnel.IPAddress}:{_selectedTunnel.Port}");

            // Отримати наш Black-ID
            var ourBlackID = _blackIDRepository.GetActiveBlackID();
            if (ourBlackID == null)
            {
                throw new InvalidOperationException("Black-ID не налаштовано");
            }

            // Знайти PeerNode в БД
            var peerNode = _peerNodeRepository.GetPeerNodeByBlackID(_selectedTunnel.BlackID);
            if (peerNode == null)
            {
                throw new InvalidOperationException($"Вузол {_selectedTunnel.BlackID} не знайдено в базі даних");
            }

            if (string.IsNullOrEmpty(peerNode.Address))
                throw new InvalidOperationException(
                    "Немає збереженого P2P endpoint.\n\nОбміняйтесь кодами у вкладці 🕳️ P2P.");

            if (!System.Net.IPEndPoint.TryParse($"{peerNode.Address}:{peerNode.Port}", out var storedEp))
                throw new InvalidOperationException("Збережений endpoint некоректний.");

            // Перепідключення через UDP hole punch до збереженого endpoint
            _p2pUdpSocket?.Dispose();
            _p2pUdpSocket = new System.Net.Sockets.UdpClient(
                System.Net.Sockets.AddressFamily.InterNetwork);

            AddLog($"🕳️ Перепідключення до {peerNode.BlackID} ({storedEp}) — 30 сек...");

            bool success = await _tunnelManager.ManualHolePunchAsync(
                peerNode.BlackID, storedEp, _p2pUdpSocket, durationSeconds: 30);

            if (!success)
            {
                _p2pUdpSocket?.Dispose();
                _p2pUdpSocket = null;
                throw new InvalidOperationException(
                    "Hole punch не вдався — IP міг змінитись після розриву.\n\n" +
                    "Обміняйтесь новими кодами у вкладці 🕳️ P2P.");
            }

            _p2pUdpSocket = null; // тепер у тунелі

            // Успішне підключення обробляється в події OnTunnelConnectionEstablished
        }
        catch (Exception ex)
        {
            _selectedTunnel.IsConnecting = false;
            _selectedTunnel.IsConnected = false;
            _selectedTunnel.StatusDisplay = "Помилка";
            UpdateTunnelConnectionStatus(_selectedTunnel);

            AddLog($"❌ Помилка підключення: {ex.Message}");

            MessageBox.Show(ex.Message, "Помилка підключення", MessageBoxButton.OK, MessageBoxImage.Error);

            ConnectTunnelButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Від'єднатися від вибраного тунелю
    /// </summary>
    private void DisconnectTunnelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTunnel == null || _tunnelManager == null)
            return;

        try
        {
            AddLog($"🔌 Від'єднання від {_selectedTunnel.BlackID}...");

            // Реальне від'єднання через TunnelManager
            _tunnelManager.DisconnectFromNode(_selectedTunnel.BlackID);

            // Оновлення UI буде в події OnTunnelConnectionLost
        }
        catch (Exception ex)
        {
            AddLog($"❌ Помилка від'єднання: {ex.Message}");

            MessageBox.Show($"Помилка від'єднання:\n{ex.Message}",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Обробка події успішного підключення
    /// </summary>
    private void OnTunnelConnectionEstablished(object? sender, TunnelConnectionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var tunnel = _tunnelNodes.FirstOrDefault(t => t.BlackID == e.PeerBlackID);
            if (tunnel != null)
            {
                tunnel.IsConnecting = false;
                tunnel.IsConnected = true;
                tunnel.StatusDisplay = "Підключено";
                tunnel.LastHandshake = DateTime.Now;
                tunnel.ConnectionTime = TimeSpan.Zero;

                if (tunnel == _selectedTunnel)
                {
                    UpdateTunnelConnectionStatus(tunnel);
                    LoadTunnelDetails(tunnel);
                    ConnectTunnelButton.IsEnabled = false;
                    DisconnectTunnelButton.IsEnabled = true;
                }

                AddLog($"✅ Підключено до {e.PeerBlackID}");
                AddLog($"   🔒 Handshake пройшов успішно!");
                AddLog($"   Session ID: {e.SessionId}");

                // Зберегти подію в БД
                try
                {
                    _eventRepository.LogEvent(new BlackCat.Shared.Models.ConnectionEvent
                    {
                        RemoteBlackID    = e.PeerBlackID,
                        RemoteIP         = tunnel.IPAddress,
                        RemotePort       = tunnel.Port,
                        InitiatorBlackID = _ourBlackID?.FullID,
                        TargetBlackID    = e.PeerBlackID,
                        EventType        = ConnectionEventType.Connected,
                        Direction        = ConnectionDirection.Outbound,
                        Message          = $"P2P з'єднання встановлено з {e.PeerBlackID} ({tunnel.IPAddress}:{tunnel.Port})",
                        IsAuthenticated  = true,
                        Timestamp        = DateTime.UtcNow
                    });
                }
                catch (Exception logEx) { AddLog($"⚠️ Event log error: {logEx.Message}"); }

                // Надіслати тестовий файл через 1 секунду після handshake
                var peerId = e.PeerBlackID;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    await SendHelloFileAsync(peerId);
                });
            }
        });
    }

    /// <summary>
    /// Обробка події втрати з'єднання
    /// </summary>
    private void OnTunnelConnectionLost(object? sender, TunnelConnectionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var tunnel = _tunnelNodes.FirstOrDefault(t => t.BlackID == e.PeerBlackID);
            if (tunnel != null)
            {
                tunnel.IsConnected = false;
                tunnel.IsConnecting = false;
                tunnel.StatusDisplay = "Відключено";
                tunnel.ConnectionTime = TimeSpan.Zero;

                if (tunnel == _selectedTunnel)
                {
                    UpdateTunnelConnectionStatus(tunnel);
                    LoadTunnelDetails(tunnel);
                    ConnectTunnelButton.IsEnabled = true;
                    DisconnectTunnelButton.IsEnabled = false;
                }

                AddLog($"⚠️ З'єднання втрачено: {e.PeerBlackID}");

                // Зберегти подію в БД
                try
                {
                    _eventRepository.LogEvent(new BlackCat.Shared.Models.ConnectionEvent
                    {
                        RemoteBlackID = e.PeerBlackID,
                        RemoteIP      = tunnel?.IPAddress ?? string.Empty,
                        RemotePort    = tunnel?.Port ?? 0,
                        EventType     = ConnectionEventType.Disconnected,
                        Direction     = ConnectionDirection.Outbound,
                        Message       = $"З'єднання з {e.PeerBlackID} розірвано",
                        Timestamp     = DateTime.UtcNow
                    });
                }
                catch { }
            }
        });
    }

    /// <summary>
    /// Обробка події невдалого підключення
    /// </summary>
    private void OnTunnelConnectionFailed(object? sender, TunnelConnectionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var tunnel = _tunnelNodes.FirstOrDefault(t => t.BlackID == e.PeerBlackID);
            if (tunnel != null)
            {
                tunnel.IsConnecting = false;
                tunnel.IsConnected = false;
                tunnel.StatusDisplay = "Помилка";

                if (tunnel == _selectedTunnel)
                {
                    UpdateTunnelConnectionStatus(tunnel);
                    ConnectTunnelButton.IsEnabled = true;
                }

                AddLog($"❌ Помилка підключення до {e.PeerBlackID}: {e.Error}");
            }
        });
    }

    /// <summary>
    /// Обробка отримання даних через тунель
    /// </summary>
    private void OnTunnelDataReceived(object? sender, TunnelDataEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Node info exchange
            if (e.Data.Length > NodeInfoMarker.Length
                && e.Data[0] == NodeInfoMarker[0]
                && e.Data[1] == NodeInfoMarker[1])
            {
                HandleNodeInfoPacket(e.SourceIP, e.Data);
                return;
            }

            // File transfer
            if (e.Data.Length > FileTransferMarker.Length
                && e.Data[0] == FileTransferMarker[0]
                && e.Data[1] == FileTransferMarker[1])
            {
                HandleFilePacket(e.SourceIP, e.Data);
                return;
            }

            // DCS directory mirror packets (marker 0xBC 0x1F)
            if (e.Data.Length > 2 && e.Data[0] == 0xBC && e.Data[1] == 0x1F)
            {
                var peerID = !string.IsNullOrEmpty(e.PeerBlackID)
                    ? e.PeerBlackID
                    : _tunnelNodes.FirstOrDefault(t => t.IPAddress == e.SourceIP)?.BlackID ?? e.SourceIP;
                _dcsIntegration?.HandleIncomingPacket(peerID, e.Data);
                return;
            }

            var tunnel = _tunnelNodes.FirstOrDefault(t => t.IPAddress == e.SourceIP);
            if (tunnel != null) tunnel.ReceivedBytes += e.Data.Length;
        });
    }

    private void OnDcsMirrorFileReceived(object? sender, DcsFileReceivedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(e.DirName)) return;

            var relative = string.IsNullOrEmpty(e.RelativePath) ? e.FileName : e.RelativePath;
            var destDir  = Path.Combine(TempFilesDir, e.SourceBlackID, e.DirName);
            var destFile = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.WriteAllBytes(destFile, e.Data);

            Dispatcher.Invoke(() => AddLog($"📁 Дзеркало отримано: {e.DirName}/{relative} від {e.SourceBlackID}"));
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AddLog($"❌ Помилка зберігання дзеркала: {ex.Message}"));
        }
    }

    private void OnUPnPStatusChanged(object? sender, bool success)
    {
        Dispatcher.Invoke(() =>
        {
            if (success)
                AddLog("✅ UPnP: порт відкрито — ви доступні для вхідних інтернет-підключень");
            // Якщо не спрацював — детальне повідомлення дасть OnNatDiagnosticReady
        });
    }

    private void OnNatDiagnosticReady(object? sender, BlackCat.Core.Services.NatDiagnosticEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!e.BehindNat)
            {
                // Пряме підключення до інтернету — Windows Firewall достатньо
                AddLog($"✅ Пряме з'єднання з інтернетом ({e.PublicIP}) — вхідні підключення доступні");
            }
            else
            {
                // Машина за NAT
                AddLog($"ℹ️ Мережа: локальна IP {e.LocalIP}, публічна {e.PublicIP}");
                AddLog($"⚠️ Ви за NAT-роутером. Для ПРИЙОМУ підключень потрібно одне з:");
                AddLog($"   1. Роутер з підтримкою UPnP (автоматично, якщо UPnP увімкнено)");
                AddLog($"   2. Port Forwarding: {e.PublicIP}:{e.ListenPort} → {e.LocalIP}:{e.ListenPort}");
                AddLog($"   ✔ Для ВИХІДНИХ підключень (підключатись до інших) — роутер не потрібен");
            }
        });
    }

    /// <summary>
    /// Показати toast-повідомлення з кнопками Прийняти / Відхилити при вхідному підключенні
    /// </summary>
    private void OnIncomingConnectionRequest(object? sender, BlackCat.NetworkCore.IncomingConnectionRequestEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AddLog($"🔔 Вхідний запит від {e.PeerBlackID} ({e.PeerIP})");

            var toast = new IncomingConnectionWindow(e);
            toast.Show();
        });
    }

    #endregion

    private async Task StartEmbeddedRelayServerAsync(int serverPort, BlackCat.Shared.Models.BlackID ourBlackID)
    {
        try
        {
            _embeddedRelayServer = new RelayServerService(serverPort);
            _embeddedRelayServer.Log += (_, msg) => Dispatcher.Invoke(() => AddLog($"[Relay] {msg}"));

            Dispatcher.Invoke(() => AddLog($"📡 Запуск relay-сервера на порту {serverPort}..."));

            // Запустити сервер у фоні (не блокуємо — реєстрація клієнта має йти після Start)
            _ = Task.Run(() => _embeddedRelayServer.StartAsync());

            // Почекати поки сервер підніметься
            await Task.Delay(500);

            // Зареєструвати ЦЮ машину у власному relay через localhost
            // Без цього інші клієнти не знайдуть нас у relay за нашим Black-ID
            if (_tunnelManager != null)
            {
                _tunnelManager.SetRelayServer("127.0.0.1", serverPort);
                bool selfRegistered = await _tunnelManager.StartRelayAsync(ourBlackID);
                Dispatcher.Invoke(() => AddLog(selfRegistered
                    ? $"✅ Цю машину зареєстровано у власному relay як {ourBlackID.FullID}"
                    : $"⚠️ Не вдалося зареєструватися у власному relay"));
            }

            // Спробувати UPnP для відкриття порту relay назовні
            try
            {
                using var natMgr = new NatManager(serverPort, "BlackCat Relay Server");
                bool upnpOk = await natMgr.TryOpenPortAsync();
                if (upnpOk)
                    Dispatcher.Invoke(() => AddLog($"✅ UPnP: порт {serverPort} відкрито автоматично"));
                else
                    Dispatcher.Invoke(() => AddLog($"⚠️ UPnP не спрацював — відкрийте порт {serverPort} вручну на роутері, або спробуйте запустити relay на ІНШІЙ машині (MAIN)."));
            }
            catch (Exception upnpEx)
            {
                Dispatcher.Invoke(() => AddLog($"⚠️ UPnP: {upnpEx.Message}"));
            }

            // Отримати справжню публічну IP через ipify (чекати до 10 секунд)
            string? publicIP = null;
            for (int i = 0; i < 10; i++)
            {
                publicIP = _tunnelManager?.ExternalIP;
                if (!string.IsNullOrEmpty(publicIP)) break;
                await Task.Delay(1000);
            }
            if (string.IsNullOrEmpty(publicIP))
                publicIP = "?.?.?.?";

            var relayAddress = $"{publicIP}:{serverPort}";
            Dispatcher.Invoke(() =>
            {
                AddLog($"📡 Relay-сервер активний. Адреса для передачі: {relayAddress}");
                AddLog($"   ⚠️ Адреса доступна лише якщо порт {serverPort} відкрито (UPnP або вручну)");
                AddLog($"   Інший користувач: Налаштування → Relay → введіть '{relayAddress}' → Зберегти → Перезапустити");
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AddLog($"❌ Relay-сервер: {ex.Message}"));
        }
    }

    // ─── P2P MANUAL SIGNALING ─────────────────────────────────────────────────

    private async void P2PGetCodeButton_Click(object sender, RoutedEventArgs e)
    {
        P2PGetCodeButton.IsEnabled = false;
        P2PCopyButton.IsEnabled    = false;
        P2PMyCodeText.Text         = "⏳ Запит до STUN...";
        P2PMyCodeText.Foreground   = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));

        try
        {
            _p2pUdpSocket?.Dispose();
            _p2pUdpSocket = new System.Net.Sockets.UdpClient(
                System.Net.Sockets.AddressFamily.InterNetwork);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var ep = await BlackCat.NetworkCore.StunClient
                         .GetPublicEndpointAsync(_p2pUdpSocket, cts.Token);

            if (ep == null)
            {
                P2PMyCodeText.Text       = "❌ STUN недоступний — перевірте інтернет";
                P2PMyCodeText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF4, 0x87, 0x71));
                _p2pUdpSocket.Dispose();
                _p2pUdpSocket = null;
            }
            else
            {
                P2PMyCodeText.Text       = ep.ToString();
                P2PMyCodeText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0xB0));
                P2PCopyButton.IsEnabled = true;
                AddLog($"🌐 P2P: твій endpoint = {P2PMyCodeText.Text}");
            }
        }
        catch (Exception ex)
        {
            P2PMyCodeText.Text = $"❌ {ex.Message}";
        }
        finally
        {
            P2PGetCodeButton.IsEnabled = true;
        }
    }

    private void P2PCopyButton_Click(object sender, RoutedEventArgs e)
    {
        var code = P2PMyCodeText.Text;
        if (!string.IsNullOrEmpty(code) && code.Contains(':'))
        {
            Clipboard.SetText(code);
            AddLog("📋 P2P: endpoint скопійовано в буфер обміну");
        }
    }

    private async void P2PConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_p2pPunchCts != null)
        {
            _p2pPunchCts.Cancel();
            return;
        }

        if (_p2pUdpSocket == null)
        {
            AddLog("❌ P2P: спочатку натисни «Отримати код»");
            return;
        }

        var peerCode = P2PPeerCodeTextBox.Text.Trim();
        if (string.IsNullOrEmpty(peerCode))
        {
            AddLog("❌ P2P: введи код від друга в поле ②");
            return;
        }

        if (_tunnelManager == null)
        {
            AddLog("❌ P2P: TunnelManager не запущено — спочатку запусти захист");
            return;
        }

        // Формат: IP:port (просто endpoint)
        if (!System.Net.IPEndPoint.TryParse(peerCode, out var peerEp))
        {
            AddLog("❌ P2P: невалідний код (очікується IP:порт, наприклад 81.162.255.205:54321)");
            return;
        }

        // Тимчасовий ID до авто-обміну інформацією
        var peerBlackID = $"peer_{peerEp.Address}_{peerEp.Port}";

        // Шукаємо існуючий вузол лише за IP (порт змінюється кожну сесію)
        var existingItem = _tunnelNodes.FirstOrDefault(t =>
            t.IPAddress == peerEp.Address.ToString());
        bool wasNew = existingItem == null;
        if (existingItem == null)
        {
            existingItem = new TunnelNodeItem
            {
                BlackID       = peerBlackID,
                DisplayName   = peerCode,
                IPAddress     = peerEp.Address.ToString(),
                Port          = peerEp.Port,
                IsTrusted     = true,
                IsConnecting  = true,
                StatusDisplay = "Підключення..."
            };
            _tunnelNodes.Add(existingItem);
        }
        else
        {
            peerBlackID           = existingItem.BlackID; // використовуємо збережений BlackID
            existingItem.Port     = peerEp.Port;          // оновлюємо порт
            existingItem.IsConnecting  = true;
            existingItem.StatusDisplay = "Підключення...";
        }

        _p2pPunchCts = new CancellationTokenSource();
        P2PConnectButton.Content   = "⏹️ Зупинити";
        P2PGetCodeButton.IsEnabled = false;
        P2PProgressBar.Value       = 0;
        P2PStatusText.Text         = "Пробиваємо NAT...";
        P2PStatusText.Foreground   = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

        AddLog($"🕳️ P2P: запуск hole punch → {peerEp} (30 сек)");

        var progress = new Progress<int>(remaining =>
        {
            Dispatcher.Invoke(() =>
            {
                P2PProgressBar.Value = 30 - remaining;
                P2PStatusText.Text   = remaining > 0
                    ? $"Очікування відповіді... залишилось {remaining} сек"
                    : "Час вийшов...";
            });
        });

        bool success;
        try
        {
            success = await _tunnelManager.ManualHolePunchAsync(
                peerBlackID, peerEp, _p2pUdpSocket,
                durationSeconds: 30,
                progress: progress,
                ct: _p2pPunchCts.Token);
        }
        catch (OperationCanceledException)
        {
            success = false;
        }
        finally
        {
            _p2pPunchCts?.Dispose();
            _p2pPunchCts = null;
        }

        P2PConnectButton.Content   = "🚀 З'єднати (30 сек)";
        P2PGetCodeButton.IsEnabled = true;

        if (success)
        {
            P2PProgressBar.Value = 30;
            P2PStatusText.Text   = "✅ З'єднано!";
            AddLog($"✅ P2P: прямий UDP тунель встановлено ({peerEp})");

            // Зберегти у телефонну книгу (шукаємо за IP щоб уникнути дублікатів)
            try
            {
                var dbPeer = _peerNodeRepository.GetAllPeerNodes()
                    .FirstOrDefault(p => p.Address == peerEp.Address.ToString() && p.IsActive);
                if (dbPeer == null)
                {
                    _peerNodeRepository.AddPeerNode(new BlackCat.Shared.Models.PeerNode
                    {
                        BlackID               = peerBlackID,
                        DisplayName           = peerCode,
                        Address               = peerEp.Address.ToString(),
                        Port                  = peerEp.Port,
                        IsTrusted             = true,
                        LastConnectedAt       = DateTime.UtcNow,
                        SuccessfulConnections = 1
                    });
                    AddLog($"📖 Додано до телефонної книги: {peerCode}");
                }
                else
                {
                    // Оновити порт і статистику, не змінюючи BlackID
                    dbPeer.Port                  = peerEp.Port;
                    dbPeer.LastConnectedAt       = DateTime.UtcNow;
                    dbPeer.SuccessfulConnections++;
                    _peerNodeRepository.UpdatePeerNode(dbPeer);
                    // Якщо знайдений запис має інший BlackID — оновити UI елемент
                    if (existingItem.BlackID != dbPeer.BlackID)
                    {
                        _tunnelManager?.RenameUdpTunnel(peerBlackID, dbPeer.BlackID);
                        existingItem.BlackID     = dbPeer.BlackID;
                        existingItem.DisplayName = dbPeer.DisplayName;
                        peerBlackID              = dbPeer.BlackID;
                        // Видалити дублікат якщо реальний BlackID вже є в списку
                        var dup = _tunnelNodes.FirstOrDefault(
                            t => t != existingItem && t.BlackID == existingItem.BlackID);
                        if (dup != null) _tunnelNodes.Remove(dup);
                    }
                }
            }
            catch (Exception ex) { AddLog($"⚠️ Не вдалося зберегти в книгу: {ex.Message}"); }

            // Авто-обмін інформацією про вузол через тунель
            _ = Task.Run(async () =>
            {
                await Task.Delay(400); // дати тунелю осісти
                await SendNodeInfoAsync(peerBlackID);
            });

            _p2pUdpSocket = null; // власність тунелю
            P2PCopyButton.IsEnabled = false;
            P2PMyCodeText.Text      = "Натисни «Отримати код» для нового з'єднання";
            P2PMyCodeText.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        }
        else
        {
            if (wasNew) _tunnelNodes.Remove(existingItem);
            else { existingItem.IsConnecting = false; existingItem.StatusDisplay = "Відключено"; }

            P2PProgressBar.Value     = 0;
            P2PStatusText.Text       = "❌ Не вдалося — спробуй ще раз";
            P2PStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x87, 0x71));
            AddLog("❌ P2P: hole punch невдалий. Причини: другий пристрій не натиснув «З'єднати», " +
                   "часова різниця > 30 сек, або симетричний NAT у провайдера");
        }
    }

    // ─── NODE INFO AUTO-EXCHANGE ──────────────────────────────────────────────

    private async Task SendNodeInfoAsync(string peerBlackID)
    {
        if (_tunnelManager == null) return;
        try
        {
            var ourId = _ourBlackID?.FullID
                      ?? _blackIDRepository.GetActiveBlackID()?.FullID
                      ?? "UNKNOWN";
            var json      = System.Text.Json.JsonSerializer.Serialize(
                                new { blackID = ourId, displayName = ourId });
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
            var packet    = new byte[NodeInfoMarker.Length + jsonBytes.Length];
            NodeInfoMarker.CopyTo(packet, 0);
            jsonBytes.CopyTo(packet, NodeInfoMarker.Length);

            bool sent = await _tunnelManager.SendDataViaRelayAsync(peerBlackID, packet);

            // Зберегти відправлений node info в temp_data
            try
            {
                var metaFile = Path.Combine(TempDataDir, $"nodeinfo_sent_{ourId}.json");
                File.WriteAllText(metaFile, json);
            }
            catch { }

            Dispatcher.Invoke(() => AddLog(sent
                ? $"📤 Надіслано node info → {peerBlackID}"
                : $"⚠️ Node info: тунель не знайдено для '{peerBlackID}'"));
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AddLog($"⚠️ SendNodeInfo error: {ex.Message}"));
        }
    }

    private void HandleNodeInfoPacket(string sourceIP, byte[] data)
    {
        try
        {
            var json      = System.Text.Encoding.UTF8.GetString(
                                data, NodeInfoMarker.Length, data.Length - NodeInfoMarker.Length);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var blackID   = doc.RootElement.GetProperty("blackID").GetString();
            var name      = doc.RootElement.TryGetProperty("displayName", out var dn)
                                ? dn.GetString() : blackID;
            if (string.IsNullOrEmpty(blackID)) return;

            AddLog($"📥 Отримано node info від {sourceIP}: {blackID}");

            // Зберегти отриманий node info в temp_data
            try
            {
                var metaFile = Path.Combine(TempDataDir, $"nodeinfo_recv_{sourceIP.Replace(':', '_')}.json");
                File.WriteAllText(metaFile, json);
            }
            catch { }

            var node = _tunnelNodes.FirstOrDefault(t => t.IPAddress == sourceIP);
            if (node == null)
            {
                AddLog($"⚠️ Node info: вузол з IP {sourceIP} не знайдено в списку");
                return;
            }

            if (node.BlackID == blackID) return; // вже актуальний — ігноруємо

            var oldId      = node.BlackID;
            var wasTempId  = oldId.StartsWith("peer_");

            // Перейменувати тунель ДО відповіді, щоб SendNodeInfoAsync знайшов його
            _tunnelManager?.RenameUdpTunnel(oldId, blackID);

            // Оновити UI одразу (до DB — щоб UNIQUE constraint не блокував)
            node.BlackID     = blackID;
            node.DisplayName = name ?? blackID;
            AddLog($"🆔 Вузол ідентифіковано: {blackID} ({sourceIP})");

            // Видалити дублікат якщо такий BlackID вже був у списку (старий або тимчасовий)
            var dupNode = _tunnelNodes.FirstOrDefault(t => t != node && t.BlackID == blackID);
            if (dupNode != null) _tunnelNodes.Remove(dupNode);

            // Оновити БД з обробкою UNIQUE constraint
            try
            {
                var existingWithRealId = _peerNodeRepository.GetPeerNodeByBlackID(blackID);
                var dbPeer             = _peerNodeRepository.GetPeerNodeByBlackID(oldId);

                if (existingWithRealId != null)
                {
                    // Справжній BlackID вже є в БД (з попередньої сесії)
                    // Видаляємо тимчасовий запис, оновлюємо існуючий
                    if (dbPeer != null && dbPeer.Id != existingWithRealId.Id)
                        _peerNodeRepository.DeletePeerNode(dbPeer.Id);

                    existingWithRealId.Address               = sourceIP;
                    existingWithRealId.Port                  = node.Port;
                    existingWithRealId.DisplayName           = name ?? blackID;
                    existingWithRealId.LastConnectedAt       = DateTime.UtcNow;
                    existingWithRealId.SuccessfulConnections++;
                    _peerNodeRepository.UpdatePeerNode(existingWithRealId);
                }
                else if (dbPeer != null)
                {
                    dbPeer.BlackID     = blackID;
                    dbPeer.DisplayName = name ?? blackID;
                    _peerNodeRepository.UpdatePeerNode(dbPeer);
                }
            }
            catch (Exception dbEx)
            {
                AddLog($"⚠️ DB update error: {dbEx.Message}");
            }

            // Відповісти своєю інформацією (лише якщо мали тимчасовий ID — уникаємо петлі)
            if (wasTempId)
                _ = Task.Run(() => SendNodeInfoAsync(blackID));
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ Node info parse error: {ex.Message}");
        }
    }

    // ─── FILE TRANSFER ────────────────────────────────────────────────────────

    /// <summary>
    /// Сформувати файл-пакет: [marker 2B][filename_len 4B LE][filename UTF8][content]
    /// </summary>
    private static byte[] BuildFilePacket(string filename, byte[] content)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(filename);
        var packet    = new byte[FileTransferMarker.Length + 4 + nameBytes.Length + content.Length];
        int offset    = 0;

        FileTransferMarker.CopyTo(packet, offset); offset += FileTransferMarker.Length;
        BitConverter.GetBytes(nameBytes.Length).CopyTo(packet, offset); offset += 4;
        nameBytes.CopyTo(packet, offset); offset += nameBytes.Length;
        content.CopyTo(packet, offset);

        return packet;
    }

    /// <summary>
    /// Надіслати файл через зашифрований тунель і зберегти копію в temp_data
    /// </summary>
    private async Task SendFileAsync(string peerBlackID, string filename, byte[] content)
    {
        if (_tunnelManager == null) return;
        try
        {
            var packet = BuildFilePacket(filename, content);
            bool sent  = await _tunnelManager.SendDataViaRelayAsync(peerBlackID, packet);

            if (sent)
            {
                // Зберегти копію відправленого файлу в temp_data
                var outPath = Path.Combine(TempDataDir, $"sent_{peerBlackID}_{filename}");
                await File.WriteAllBytesAsync(outPath, content);

                // Оновити статистику тунелю і залогувати в DB
                Dispatcher.Invoke(() =>
                {
                    var tunnel = _tunnelNodes.FirstOrDefault(t => t.BlackID == peerBlackID);
                    if (tunnel != null) tunnel.SentBytes += content.Length;

                    AddLog($"📤 Файл надіслано → {peerBlackID}: {filename} ({content.Length} B)");
                });

                try
                {
                    var tunnelNode = await Dispatcher.InvokeAsync(() =>
                        _tunnelNodes.FirstOrDefault(t => t.BlackID == peerBlackID));
                    _eventRepository.LogEvent(new BlackCat.Shared.Models.ConnectionEvent
                    {
                        RemoteBlackID    = peerBlackID,
                        RemoteIP         = tunnelNode?.IPAddress ?? string.Empty,
                        RemotePort       = tunnelNode?.Port ?? 0,
                        InitiatorBlackID = _ourBlackID?.FullID,
                        EventType        = ConnectionEventType.AuthenticationSuccess,
                        Direction        = ConnectionDirection.Outbound,
                        Message          = $"Файл надіслано: {filename} ({content.Length} B) → {peerBlackID}",
                        IsAuthenticated  = true,
                        BytesSent        = content.Length,
                        Timestamp        = DateTime.UtcNow
                    });
                }
                catch { }
            }
            else
            {
                Dispatcher.Invoke(() =>
                    AddLog($"⚠️ Не вдалося надіслати файл '{filename}' до '{peerBlackID}'"));
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AddLog($"⚠️ SendFile error: {ex.Message}"));
        }
    }

    /// <summary>
    /// Надіслати тестовий hello.txt при встановленні з'єднання
    /// </summary>
    private async Task SendHelloFileAsync(string peerBlackID)
    {
        var ourId    = _ourBlackID?.FullID
                     ?? _blackIDRepository.GetActiveBlackID()?.FullID
                     ?? "UNKNOWN";
        var text     = $"Привіт від {ourId}!\r\n" +
                       $"З'єднання встановлено: {DateTime.Now:dd.MM.yyyy HH:mm:ss}\r\n" +
                       $"Тунель активний ✓";
        var content  = System.Text.Encoding.UTF8.GetBytes(text);
        var filename = $"hello_{ourId}_{DateTime.Now:HHmmss}.txt";

        await SendFileAsync(peerBlackID, filename, content);
    }

    /// <summary>
    /// Обробити отриманий файл-пакет: розпакувати, зберегти в temp_files, дамп в temp_data
    /// </summary>
    private void HandleFilePacket(string sourceIP, byte[] data)
    {
        try
        {
            int offset      = FileTransferMarker.Length;
            int nameLen     = BitConverter.ToInt32(data, offset); offset += 4;
            var filename    = System.Text.Encoding.UTF8.GetString(data, offset, nameLen); offset += nameLen;
            var content     = data.Skip(offset).ToArray();

            // Sanitize filename
            var safeName    = Path.GetFileName(filename);
            if (string.IsNullOrEmpty(safeName)) safeName = "received_file.bin";

            // Зберегти в temp_files (для користувача)
            var savePath    = Path.Combine(TempFilesDir, safeName);
            File.WriteAllBytes(savePath, content);

            // Зберегти метадані в temp_data (для програми)
            var metaPath = Path.Combine(TempDataDir,
                $"recv_{sourceIP}_{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}.meta");
            File.WriteAllText(metaPath,
                $"from={sourceIP}\r\nfilename={safeName}\r\nsize={content.Length}\r\ntimestamp={DateTime.UtcNow:O}\r\n");

            // Оновити статистику тунелю
            var tunnel = _tunnelNodes.FirstOrDefault(t => t.IPAddress == sourceIP);
            if (tunnel != null) tunnel.ReceivedBytes += data.Length;

            // Залогувати отримання файлу в DB
            try
            {
                _eventRepository.LogEvent(new BlackCat.Shared.Models.ConnectionEvent
                {
                    RemoteBlackID   = tunnel?.BlackID,
                    RemoteIP        = sourceIP,
                    RemotePort      = tunnel?.Port ?? 0,
                    TargetBlackID   = _ourBlackID?.FullID,
                    EventType       = ConnectionEventType.AuthenticationSuccess,
                    Direction       = ConnectionDirection.Inbound,
                    Message         = $"Файл отримано: {safeName} ({content.Length} B) від {sourceIP}",
                    IsAuthenticated = true,
                    BytesReceived   = content.Length,
                    Timestamp       = DateTime.UtcNow
                });
            }
            catch { }

            AddLog($"📥 Отримано файл від {sourceIP}: {safeName} ({content.Length} B)");
            AddLog($"   💾 Збережено: {savePath}");
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ File receive error: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _p2pPunchCts?.Cancel();
        _p2pUdpSocket?.Dispose();
        _embeddedRelayServer?.Dispose();
        _commandService?.Stop();
        _commandService?.Dispose();
        _dcsIntegration?.Dispose();
        _tunnelManager?.Dispose();
        _connectionMonitor?.Dispose();
        _coordinator?.Stop();
        _coordinator?.Dispose();
        _ruleRepository?.Dispose();
        _trafficCollector?.Stop();
        _trafficCollector?.Dispose();
        _chartVm.Dispose();
        base.OnClosed(e);
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = System.Windows.WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == System.Windows.WindowState.Maximized)
        {
            WindowState = System.Windows.WindowState.Normal;
            MaximizeButton.Content = "□";
        }
        else
        {
            WindowState = System.Windows.WindowState.Maximized;
            MaximizeButton.Content = "⧉";
        }
    }
    private void AppCloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}

/// <summary>
/// Елемент статистики процесу
/// </summary>
public class ProcessStatItem : System.ComponentModel.INotifyPropertyChanged
{
    private string _processName = string.Empty;
    private int _packetCount;
    private long _totalBytes;
    private string _trafficDisplay = string.Empty;

    public string ProcessName
    {
        get => _processName;
        set
        {
            _processName = value;
            OnPropertyChanged(nameof(ProcessName));
        }
    }

    public int PacketCount
    {
        get => _packetCount;
        set
        {
            _packetCount = value;
            OnPropertyChanged(nameof(PacketCount));
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            _totalBytes = value;
            OnPropertyChanged(nameof(TotalBytes));
        }
    }

    public string TrafficDisplay
    {
        get => _trafficDisplay;
        set
        {
            _trafficDisplay = value;
            OnPropertyChanged(nameof(TrafficDisplay));
        }
    }

    public void UpdateTrafficDisplay()
    {
        if (TotalBytes < 1024)
            TrafficDisplay = $"{TotalBytes} B";
        else if (TotalBytes < 1024 * 1024)
            TrafficDisplay = $"{TotalBytes / 1024.0:F1} KB";
        else if (TotalBytes < 1024 * 1024 * 1024)
            TrafficDisplay = $"{TotalBytes / (1024.0 * 1024.0):F1} MB";
        else
            TrafficDisplay = $"{TotalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Елемент тунелю Black-ID
/// </summary>
public class TunnelNodeItem : System.ComponentModel.INotifyPropertyChanged
{
    private string _blackID = string.Empty;
    private string _ipAddress = string.Empty;
    private int _port = 9999;
    private string _displayName = string.Empty;
    private bool _isTrusted;
    private bool _isConnected;
    private bool _isConnecting;
    private string _statusDisplay = "Не підключено";
    private long _sentBytes;
    private long _receivedBytes;
    private TimeSpan _connectionTime;
    private DateTime _lastHandshake;

    public string BlackID
    {
        get => _blackID;
        set
        {
            _blackID = value;
            OnPropertyChanged(nameof(BlackID));
        }
    }

    public string IPAddress
    {
        get => _ipAddress;
        set
        {
            _ipAddress = value;
            OnPropertyChanged(nameof(IPAddress));
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            _port = value;
            OnPropertyChanged(nameof(Port));
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = value;
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public bool IsTrusted
    {
        get => _isTrusted;
        set
        {
            _isTrusted = value;
            OnPropertyChanged(nameof(IsTrusted));
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged(nameof(IsConnected));
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            _isConnecting = value;
            OnPropertyChanged(nameof(IsConnecting));
        }
    }

    public string StatusDisplay
    {
        get => _statusDisplay;
        set
        {
            _statusDisplay = value;
            OnPropertyChanged(nameof(StatusDisplay));
        }
    }

    public long SentBytes
    {
        get => _sentBytes;
        set
        {
            _sentBytes = value;
            OnPropertyChanged(nameof(SentBytes));
        }
    }

    public long ReceivedBytes
    {
        get => _receivedBytes;
        set
        {
            _receivedBytes = value;
            OnPropertyChanged(nameof(ReceivedBytes));
        }
    }

    public TimeSpan ConnectionTime
    {
        get => _connectionTime;
        set
        {
            _connectionTime = value;
            OnPropertyChanged(nameof(ConnectionTime));
        }
    }

    public DateTime LastHandshake
    {
        get => _lastHandshake;
        set
        {
            _lastHandshake = value;
            OnPropertyChanged(nameof(LastHandshake));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
