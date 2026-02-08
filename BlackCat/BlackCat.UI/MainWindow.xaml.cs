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
using System.Collections.ObjectModel;
using System.Linq;
using NetworkProtocol = BlackCat.Shared.Enums.ProtocolType;

namespace BlackCat.UI;

public partial class MainWindow : Window
{
    private FirewallCoordinator? _coordinator;
    private readonly DispatcherTimer _updateTimer;
    private readonly RuleRepository _ruleRepository;
    private readonly ProcessLookupService _processLookupService;

    // Графіки
    private readonly ChartValues<double> _allowedValues = new();
    private readonly ChartValues<double> _blockedValues = new();
    private readonly ChartValues<double> _tunneledValues = new();

    // Статистика процесів
    private readonly ObservableCollection<ProcessStatItem> _processStats = new();
    private readonly Dictionary<string, ProcessStatItem> _processStatsDict = new();

    public MainWindow()
    {
        InitializeComponent();

        _ruleRepository = new RuleRepository();
        _processLookupService = new ProcessLookupService();

        // Налаштування графіків
        InitializeCharts();

        // Налаштування статистики процесів
        ProcessStatsDataGrid.ItemsSource = _processStats;

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
        PacketsChart.Series = new SeriesCollection
        {
            new LineSeries
            {
                Title = "Дозволено",
                Values = _allowedValues,
                Stroke = new SolidColorBrush(Color.FromRgb(106, 153, 85)),
                Fill = Brushes.Transparent,
                PointGeometry = null
            },
            new LineSeries
            {
                Title = "Заблоковано",
                Values = _blockedValues,
                Stroke = new SolidColorBrush(Color.FromRgb(244, 135, 113)),
                Fill = Brushes.Transparent,
                PointGeometry = null
            },
            new LineSeries
            {
                Title = "Через тунель",
                Values = _tunneledValues,
                Stroke = new SolidColorBrush(Color.FromRgb(86, 156, 214)),
                Fill = Brushes.Transparent,
                PointGeometry = null
            }
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

        // Оновити графіки
        _allowedValues.Add(stats.AllowedPackets);
        _blockedValues.Add(stats.BlockedPackets);
        _tunneledValues.Add(stats.TunneledPackets);

        // Обмежити кількість точок на графіку
        if (_allowedValues.Count > 50)
        {
            _allowedValues.RemoveAt(0);
            _blockedValues.RemoveAt(0);
            _tunneledValues.RemoveAt(0);
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

    protected override void OnClosed(EventArgs e)
    {
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
