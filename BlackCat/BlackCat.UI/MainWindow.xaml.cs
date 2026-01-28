using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BlackCat.Core;
using BlackCat.Core.Data;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;
using LiveCharts;
using LiveCharts.Wpf;

namespace BlackCat.UI;

public partial class MainWindow : Window
{
    private FirewallCoordinator? _coordinator;
    private readonly DispatcherTimer _updateTimer;
    private readonly RuleRepository _ruleRepository;

    // Графіки
    private readonly ChartValues<double> _allowedValues = new();
    private readonly ChartValues<double> _blockedValues = new();
    private readonly ChartValues<double> _tunneledValues = new();

    public MainWindow()
    {
        InitializeComponent();

        _ruleRepository = new RuleRepository();

        // Налаштування графіків
        InitializeCharts();

        // Таймер оновлення UI
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
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
        catch (Exception ex)
        {
            AddLog($"❌ Помилка запуску: {ex.Message}");
            MessageBox.Show($"Помилка запуску: {ex.Message}\n\nПереконайтеся, що програма запущена з правами адміністратора.",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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
        MessageBox.Show("Налаштування будуть доступні в наступній версії", "Налаштування", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        // Приклад додавання правила
        var rule = new FilterRule
        {
            Name = "Тестове правило",
            IPAddress = "192.168.1.0/24",
            Port = 0,
            Protocol = ProtocolType.Any,
            Action = FilterAction.Allow,
            Direction = TrafficDirection.Both,
            IsEnabled = true,
            Priority = 100
        };

        int id = _ruleRepository.AddRule(rule);
        rule.Id = id;

        AddLog($"Додано правило: {rule.Name}");
        LoadRules();

        // Перезавантажити правила в координаторі
        _coordinator?.LoadRules();
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
