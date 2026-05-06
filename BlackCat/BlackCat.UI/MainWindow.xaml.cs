using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BlackCat.Core;
using BlackCat.Core.Data;
using BlackCat.Core.Services;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;
using LiveCharts;
using LiveCharts.Wpf;
using LiveCharts.Defaults;
using System.Collections.ObjectModel;
using System.Linq;
using NetworkProtocol = BlackCat.Shared.Enums.ProtocolType;

namespace BlackCat.UI;

public partial class MainWindow : Window
{
    private FirewallCoordinator? _coordinator;
    private TunnelManager? _tunnelManager;
    private readonly DispatcherTimer _updateTimer;
    private readonly RuleRepository _ruleRepository;
    private readonly ProcessLookupService _processLookupService;
    private readonly BlackIDRepository _blackIDRepository;
    private readonly BlackCatDatabase _database;
    private readonly HandshakeService _handshakeService;
    private readonly ConnectionMonitorService _connectionMonitor;
    private readonly ConnectionEventRepository _eventRepository;
    private readonly BlackIDService _blackIDService;

    // Графіки - швидкість (стовпчаста)
    private readonly ChartValues<double> _speedValues = new();

    // Графіки - трафік по програмах (динамічні серії)
    private readonly Dictionary<string, ChartValues<double>> _processTrafficValues = new();
    private readonly Dictionary<string, Color> _processColors = new();
    private readonly List<Color> _availableColors = new()
    {
        Color.FromRgb(106, 153, 85),   // Зелений
        Color.FromRgb(244, 135, 113),  // Червоний
        Color.FromRgb(86, 156, 214),   // Синій
        Color.FromRgb(220, 220, 170),  // Жовтий
        Color.FromRgb(197, 134, 192),  // Фіолетовий
        Color.FromRgb(78, 201, 176),   // М'ятний
        Color.FromRgb(206, 145, 120),  // Помаранчевий
        Color.FromRgb(156, 220, 254)   // Блакитний
    };
    private int _colorIndex = 0;

    // Вісь часу
    private readonly List<string> _timeLabels = new();
    private DateTime _startTime;

    // Статистика процесів
    private readonly ObservableCollection<ProcessStatItem> _processStats = new();
    private readonly Dictionary<string, ProcessStatItem> _processStatsDict = new();
    private readonly Dictionary<string, long> _lastProcessBytes = new();

    // Тунелі Black-ID
    private readonly ObservableCollection<TunnelNodeItem> _tunnelNodes = new();
    private TunnelNodeItem? _selectedTunnel;
    private readonly PeerNodeRepository _peerNodeRepository;

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

        // Налаштування графіків
        InitializeCharts();

        // Налаштування статистики процесів
        ProcessStatsDataGrid.ItemsSource = _processStats;

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
        _startTime = DateTime.Now;

        PacketsChart.Series = new SeriesCollection
        {
            // Стовпчаста діаграма для швидкості
            new ColumnSeries
            {
                Title = "Швидкість (KB/s)",
                Values = _speedValues,
                Fill = new SolidColorBrush(Color.FromArgb(180, 78, 201, 176)),
                MaxColumnWidth = 30,  // ширина стовпчика
                ColumnPadding = 4     // відступ між стовпчиками
            }
        };

        // Налаштування осі X (час)
        PacketsChart.AxisX[0].Labels = _timeLabels;
        PacketsChart.AxisX[0].LabelFormatter = value =>
        {
            var index = (int)value;
            if (index >= 0 && index < _timeLabels.Count)
                return _timeLabels[index];
            return "";
        };
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

                // Запустити сервер для прийому вхідних з'єднань
                await _tunnelManager.StartServerAsync(ourBlackID);

                AddLog($"✅ TunnelManager запущено (порт 9999)");

                // Показати статус UPnP
                if (_tunnelManager.IsPortForwardingActive)
                {
                    AddLog($"🌐 UPnP активний! Зовнішня IP: {_tunnelManager.ExternalIP}");
                    AddLog($"💡 Інші користувачі можуть підключатися до вас БЕЗ налаштування роутера!");
                }
                else
                {
                    AddLog($"⚠️ UPnP недоступний - локальні з'єднання працюють");
                    AddLog($"💡 Для Інтернет-з'єднань може знадобитися налаштування роутера");
                }
            }
            else
            {
                AddLog("⚠️ Black-ID не налаштовано - тунелі недоступні");
            }

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;

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

            _tunnelManager?.Dispose();
            _tunnelManager = null;

            _connectionMonitor?.Stop();

            _coordinator?.Stop();
            _coordinator?.Dispose();
            _coordinator = null;

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;

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

        // Оновити цифри
        TotalPacketsText.Text = stats.TotalPackets.ToString("N0");
        AllowedPacketsText.Text = stats.AllowedPackets.ToString("N0");
        BlockedPacketsText.Text = stats.BlockedPackets.ToString("N0");
        TunneledPacketsText.Text = stats.TunneledPackets.ToString("N0");
        SpeedText.Text = $"{stats.BytesPerSecond / 1024:F2} KB/s";

        // Оновити статус тунелю
        UpdateTunnelStatus(stats.TunnelStatus);

        // Оновити час роботи
        UptimeText.Text = $"Час роботи: {stats.Uptime:hh\\:mm\\:ss}";

        // Додати мітку часу
        var elapsed = DateTime.Now - _startTime;
        _timeLabels.Add($"{elapsed:mm\\:ss}");

        // Додати швидкість (стовпчаста діаграма)
        _speedValues.Add(stats.BytesPerSecond / 1024.0);

        // Оновити трафік по програмах
        UpdateProcessTraffic();

        // Обмежити кількість точок на графіку (60 точок = 1 хвилина при оновленні раз на секунду)
        if (_speedValues.Count > 60)
        {
            _speedValues.RemoveAt(0);
            _timeLabels.RemoveAt(0);

            // Видалити старі дані з серій програм
            foreach (var values in _processTrafficValues.Values)
            {
                if (values.Count > 60)
                    values.RemoveAt(0);
            }
        }

        // Оновити статистику процесів
        UpdateProcessStatistics();
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

    /// <summary>
    /// Оновити трафік по програмах на графіку
    /// </summary>
    private void UpdateProcessTraffic()
    {
        try
        {
            // Отримати активні з'єднання
            var connections = _processLookupService.GetActiveTcpConnections();

            // Групувати за процесами
            var processGroups = connections
                .Where(c => !string.IsNullOrEmpty(c.ProcessName))
                .GroupBy(c => c.ProcessName)
                .Select(g => new
                {
                    ProcessName = g.Key,
                    ConnectionCount = g.Count(),
                    // Оцінка трафіку (в реальності треба рахувати байти)
                    EstimatedKBps = g.Count() * 10.0
                })
                .OrderByDescending(p => p.EstimatedKBps)
                .Take(5)  // Топ-5 програм
                .ToList();

            // Список існуючих процесів
            var currentProcesses = processGroups.Select(p => p.ProcessName).ToHashSet();

            // Додати/оновити серії для кожної програми
            foreach (var group in processGroups)
            {
                if (!_processTrafficValues.ContainsKey(group.ProcessName))
                {
                    // Створити нову серію
                    var color = _availableColors[_colorIndex % _availableColors.Count];
                    _colorIndex++;

                    _processColors[group.ProcessName] = color;
                    _processTrafficValues[group.ProcessName] = new ChartValues<double>();

                    // Заповнити попередні значення нулями
                    for (int i = 0; i < _speedValues.Count; i++)
                    {
                        _processTrafficValues[group.ProcessName].Add(0);
                    }

                    // Додати серію на графік
                    PacketsChart.Series.Add(new LineSeries
                    {
                        Title = group.ProcessName,
                        Values = _processTrafficValues[group.ProcessName],
                        Stroke = new SolidColorBrush(color),
                        Fill = Brushes.Transparent,
                        PointGeometry = null,
                        LineSmoothness = 0.3
                    });
                }

                // Додати нове значення
                _processTrafficValues[group.ProcessName].Add(group.EstimatedKBps);
            }

            // Додати нулі для процесів, які зараз не активні
            foreach (var process in _processTrafficValues.Keys.ToList())
            {
                if (!currentProcesses.Contains(process))
                {
                    _processTrafficValues[process].Add(0);
                }
            }

            // Видалити старі неактивні процеси (якщо вони довго не з'являлися)
            var processesToRemove = new List<string>();
            foreach (var process in _processTrafficValues.Keys.ToList())
            {
                // Якщо останні 10 значень = 0, видалити серію
                if (_processTrafficValues[process].Count >= 10)
                {
                    var lastTen = _processTrafficValues[process].Skip(_processTrafficValues[process].Count - 10).ToList();
                    if (lastTen.All(v => v == 0))
                    {
                        processesToRemove.Add(process);
                    }
                }
            }

            foreach (var process in processesToRemove)
            {
                // Знайти і видалити серію з графіка
                var seriesToRemove = PacketsChart.Series.FirstOrDefault(s => s.Title == process);
                if (seriesToRemove != null)
                {
                    PacketsChart.Series.Remove(seriesToRemove);
                }

                _processTrafficValues.Remove(process);
                _processColors.Remove(process);
            }
        }
        catch
        {
            // Ігнорувати помилки
        }
    }

    /// <summary>
    /// Оновити статистику процесів
    /// </summary>
    private void UpdateProcessStatistics()
    {
        try
        {
            // Отримати активні з'єднання
            var connections = _processLookupService.GetActiveTcpConnections();

            // Групувати за процесами та порахувати трафік
            var processGroups = connections
                .Where(c => !string.IsNullOrEmpty(c.ProcessName))
                .GroupBy(c => c.ProcessName)
                .Select(g => new
                {
                    ProcessName = g.Key,
                    ConnectionCount = g.Count(),
                    // Оцінка трафіку на основі кількості з'єднань (приблизно)
                    // В майбутньому можна додати реальний підрахунок байтів
                    EstimatedBytes = g.Count() * 1024L * (new Random().Next(1, 100))
                })
                .OrderByDescending(p => p.ConnectionCount)
                .Take(10)
                .ToList();

            // Оновити колекцію
            foreach (var group in processGroups)
            {
                if (_processStatsDict.TryGetValue(group.ProcessName, out var existingStat))
                {
                    // Оновити існуючий
                    existingStat.PacketCount = group.ConnectionCount;
                    existingStat.TotalBytes = existingStat.TotalBytes + group.EstimatedBytes;
                    existingStat.UpdateTrafficDisplay();
                }
                else
                {
                    // Додати новий
                    var newStat = new ProcessStatItem
                    {
                        ProcessName = group.ProcessName,
                        PacketCount = group.ConnectionCount,
                        TotalBytes = group.EstimatedBytes
                    };
                    newStat.UpdateTrafficDisplay();

                    _processStatsDict[group.ProcessName] = newStat;
                    _processStats.Add(newStat);
                }
            }

            // Відсортувати за трафіком
            var sorted = _processStats.OrderByDescending(p => p.TotalBytes).ToList();
            _processStats.Clear();
            foreach (var item in sorted.Take(10))
            {
                _processStats.Add(item);
            }

            // Очистити словник від видалених
            var toRemove = _processStatsDict.Keys
                .Where(k => !_processStats.Any(p => p.ProcessName == k))
                .ToList();

            foreach (var key in toRemove)
            {
                _processStatsDict.Remove(key);
            }
        }
        catch
        {
            // Ігнорувати помилки отримання статистики
        }
    }

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

            // Реальне підключення через TunnelManager
            bool success = await _tunnelManager.ConnectToNodeAsync(peerNode, ourBlackID);

            if (!success)
            {
                throw new InvalidOperationException("Не вдалося встановити з'єднання");
            }

            // Успішне підключення обробляється в події OnTunnelConnectionEstablished
        }
        catch (Exception ex)
        {
            _selectedTunnel.IsConnecting = false;
            _selectedTunnel.IsConnected = false;
            _selectedTunnel.StatusDisplay = "Помилка";
            UpdateTunnelConnectionStatus(_selectedTunnel);

            AddLog($"❌ Помилка підключення: {ex.Message}");

            MessageBox.Show($"Не вдалося підключитися:\n{ex.Message}\n\nПеревірте:\n• IP адресу та порт\n• Чи запущений BlackCat на віддаленому вузлі\n• Чи правильний Black-ID",
                "Помилка підключення", MessageBoxButton.OK, MessageBoxImage.Error);

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
        // TODO: Обробка отриманих даних
        // Наразі просто логуємо
        Dispatcher.Invoke(() =>
        {
            AddLog($"📨 Отримано дані: {e.Data.Length} байт від {e.SourceIP}");
        });
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _tunnelManager?.Dispose();
        _connectionMonitor?.Dispose();
        _coordinator?.Stop();
        _coordinator?.Dispose();
        _ruleRepository?.Dispose();
        base.OnClosed(e);
    }
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
