using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using BlackCat.Core;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;
using System.Collections.ObjectModel;
using System.Linq;
using BlackCat.Core.Services;

namespace BlackCat.UI;

public partial class RulesManagementWindow : MetroWindow
{
    private readonly FirewallCoordinator? _coordinator;
    private readonly ProcessLookupService _processLookupService;
    private ObservableCollection<FilterRule> _rules;
    private FilterRule? _selectedRule;
    private bool _isNewRule;

    public RulesManagementWindow(FirewallCoordinator? coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _processLookupService = new ProcessLookupService();
        _rules = new ObservableCollection<FilterRule>();

        RulesDataGrid.ItemsSource = _rules;

        LoadRules();
        ClearForm();
    }

    /// <summary>
    /// Завантажити правила з репозиторію
    /// </summary>
    private void LoadRules()
    {
        _rules.Clear();

        if (_coordinator?.RuleRepository == null)
            return;

        var rules = _coordinator.RuleRepository.GetAllRules();
        foreach (var rule in rules.OrderBy(r => r.Priority))
        {
            _rules.Add(rule);
        }
    }

    /// <summary>
    /// Обробка зміни вибору правила
    /// </summary>
    private void RulesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RulesDataGrid.SelectedItem is FilterRule rule)
        {
            _selectedRule = rule;
            _isNewRule = false;
            LoadRuleToForm(rule);
            SaveButton.IsEnabled = true;
            DeleteButton.IsEnabled = true;
        }
        else
        {
            ClearForm();
            SaveButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// Завантажити дані правила у форму
    /// </summary>
    private void LoadRuleToForm(FilterRule rule)
    {
        RuleNameTextBox.Text = rule.Name;
        RuleIPTextBox.Text = rule.IPAddress ?? string.Empty;
        RulePortNumeric.Value = rule.Port;
        RulePortRangeTextBox.Text = rule.PortRange ?? string.Empty;

        // Протокол
        RuleProtocolComboBox.SelectedIndex = rule.Protocol switch
        {
            ProtocolType.Any => 0,
            ProtocolType.TCP => 1,
            ProtocolType.UDP => 2,
            ProtocolType.ICMP => 3,
            _ => 0
        };

        // Дія
        RuleActionComboBox.SelectedIndex = rule.Action switch
        {
            FilterAction.Allow => 0,
            FilterAction.Block => 1,
            FilterAction.Tunnel => 2,
            _ => 0
        };

        // Напрямок
        RuleDirectionComboBox.SelectedIndex = rule.Direction switch
        {
            TrafficDirection.Both => 0,
            TrafficDirection.Inbound => 1,
            TrafficDirection.Outbound => 2,
            _ => 0
        };

        RuleProcessNameTextBox.Text = rule.ProcessName ?? string.Empty;
        RulePriorityNumeric.Value = rule.Priority;
        RuleEnabledCheckBox.IsChecked = rule.IsEnabled;
        RuleDescriptionTextBox.Text = rule.Description ?? string.Empty;
    }

    /// <summary>
    /// Очистити форму
    /// </summary>
    private void ClearForm()
    {
        RuleNameTextBox.Text = string.Empty;
        RuleIPTextBox.Text = string.Empty;
        RulePortNumeric.Value = 0;
        RulePortRangeTextBox.Text = string.Empty;
        RuleProtocolComboBox.SelectedIndex = 0;
        RuleActionComboBox.SelectedIndex = 0;
        RuleDirectionComboBox.SelectedIndex = 0;
        RuleProcessNameTextBox.Text = string.Empty;
        RulePriorityNumeric.Value = 100;
        RuleEnabledCheckBox.IsChecked = true;
        RuleDescriptionTextBox.Text = string.Empty;

        _selectedRule = null;
    }

    /// <summary>
    /// Додати нове правило
    /// </summary>
    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        _isNewRule = true;
        _selectedRule = null;
        RulesDataGrid.SelectedItem = null;
        ClearForm();
        SaveButton.IsEnabled = true;
        DeleteButton.IsEnabled = false;

        RuleNameTextBox.Focus();
    }

    /// <summary>
    /// Зберегти правило
    /// </summary>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator?.RuleRepository == null)
            return;

        // Валідація
        if (string.IsNullOrWhiteSpace(RuleNameTextBox.Text))
        {
            MessageBox.Show("Введіть назву правила", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Створити або оновити правило
            FilterRule rule;

            if (_isNewRule || _selectedRule == null)
            {
                rule = new FilterRule
                {
                    CreatedAt = DateTime.UtcNow
                };
            }
            else
            {
                rule = _selectedRule;
            }

            // Заповнити дані з форми
            rule.Name = RuleNameTextBox.Text.Trim();
            rule.IPAddress = string.IsNullOrWhiteSpace(RuleIPTextBox.Text) ? null : RuleIPTextBox.Text.Trim();
            rule.Port = (int)RulePortNumeric.Value;
            rule.PortRange = string.IsNullOrWhiteSpace(RulePortRangeTextBox.Text) ? null : RulePortRangeTextBox.Text.Trim();

            // Протокол
            rule.Protocol = (RuleProtocolComboBox.SelectedItem as ComboBoxItem)?.Tag switch
            {
                "6" => ProtocolType.TCP,
                "17" => ProtocolType.UDP,
                "1" => ProtocolType.ICMP,
                _ => ProtocolType.Any
            };

            // Дія
            rule.Action = (RuleActionComboBox.SelectedItem as ComboBoxItem)?.Tag switch
            {
                "1" => FilterAction.Block,
                "2" => FilterAction.Tunnel,
                _ => FilterAction.Allow
            };

            // Напрямок
            rule.Direction = (RuleDirectionComboBox.SelectedItem as ComboBoxItem)?.Tag switch
            {
                "1" => TrafficDirection.Inbound,
                "2" => TrafficDirection.Outbound,
                _ => TrafficDirection.Both
            };

            rule.ProcessName = string.IsNullOrWhiteSpace(RuleProcessNameTextBox.Text) ? null : RuleProcessNameTextBox.Text.Trim();
            rule.Priority = (int)RulePriorityNumeric.Value;
            rule.IsEnabled = RuleEnabledCheckBox.IsChecked ?? true;
            rule.Description = string.IsNullOrWhiteSpace(RuleDescriptionTextBox.Text) ? null : RuleDescriptionTextBox.Text.Trim();

            // Зберегти в БД
            if (_isNewRule || _selectedRule == null)
            {
                _coordinator.RuleRepository.AddRule(rule);
                MessageBox.Show($"Правило '{rule.Name}' додано", "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _coordinator.RuleRepository.UpdateRule(rule);
                MessageBox.Show($"Правило '{rule.Name}' оновлено", "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // Оновити список та застосувати правила
            LoadRules();
            _coordinator.LoadRules();

            _isNewRule = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не вдалося зберегти правило:\n{ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Видалити правило
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRule == null || _coordinator?.RuleRepository == null)
            return;

        var result = MessageBox.Show(
            $"Видалити правило '{_selectedRule.Name}'?",
            "Підтвердження",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                _coordinator.RuleRepository.DeleteRule(_selectedRule.Id);
                MessageBox.Show($"Правило '{_selectedRule.Name}' видалено", "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                LoadRules();
                _coordinator.LoadRules();
                ClearForm();
                SaveButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося видалити правило:\n{ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Вибрати порт з активних з'єднань
    /// </summary>
    private void SelectActivePortButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var connections = _processLookupService.GetActiveTcpConnections();

            if (!connections.Any())
            {
                MessageBox.Show("Активні з'єднання не знайдено", "Інформація", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Створити діалог вибору
            var dialog = new ActivePortsDialog(connections);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.SelectedPort > 0)
            {
                // Вибір одного порту
                RulePortNumeric.Value = dialog.SelectedPort;
                RulePortRangeTextBox.Text = string.Empty;

                // Якщо вибрано процес, заповнити ProcessName
                if (!string.IsNullOrEmpty(dialog.SelectedProcessName))
                {
                    RuleProcessNameTextBox.Text = dialog.SelectedProcessName;
                }
            }
            else if (dialog.SelectedPorts?.Any() == true)
            {
                // Вибір кількох портів - створити діапазон
                var ports = dialog.SelectedPorts.OrderBy(p => p).ToList();
                RulePortNumeric.Value = 0;
                RulePortRangeTextBox.Text = string.Join(",", ports);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не вдалося отримати список активних портів:\n{ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Вибрати процес з запущених
    /// </summary>
    private void SelectRunningProcessButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var connections = _processLookupService.GetActiveTcpConnections();

            // Групувати за процесами
            var processes = connections
                .GroupBy(c => new { c.ProcessName, c.ProcessId })
                .Select(g => new
                {
                    g.Key.ProcessName,
                    g.Key.ProcessId,
                    ConnectionCount = g.Count(),
                    Ports = g.Select(c => c.LocalPort).Distinct().ToList()
                })
                .OrderBy(p => p.ProcessName)
                .ToList();

            if (!processes.Any())
            {
                MessageBox.Show("Процеси з активними з'єднаннями не знайдено", "Інформація", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Створити діалог вибору
            var dialog = new RunningProcessesDialog(processes);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedProcessName))
            {
                RuleProcessNameTextBox.Text = dialog.SelectedProcessName;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не вдалося отримати список процесів:\n{ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Закрити вікно
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
