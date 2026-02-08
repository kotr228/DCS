using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
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
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator?.RuleRepository == null)
            return;

        // Валідація
        if (string.IsNullOrWhiteSpace(RuleNameTextBox.Text))
        {
            await this.ShowMessageAsync("Помилка", "Введіть назву правила");
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
                await this.ShowMessageAsync("Успіх", $"Правило '{rule.Name}' додано");
            }
            else
            {
                _coordinator.RuleRepository.UpdateRule(rule);
                await this.ShowMessageAsync("Успіх", $"Правило '{rule.Name}' оновлено");
            }

            // Оновити список та застосувати правила
            LoadRules();
            _coordinator.LoadRules();

            _isNewRule = false;
        }
        catch (Exception ex)
        {
            await this.ShowMessageAsync("Помилка", $"Не вдалося зберегти правило:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Видалити правило
    /// </summary>
    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRule == null || _coordinator?.RuleRepository == null)
            return;

        var result = await this.ShowMessageAsync(
            "Підтвердження",
            $"Видалити правило '{_selectedRule.Name}'?",
            MessageDialogStyle.AffirmativeAndNegative);

        if (result == MessageDialogResult.Affirmative)
        {
            try
            {
                _coordinator.RuleRepository.DeleteRule(_selectedRule.Id);
                await this.ShowMessageAsync("Успіх", $"Правило '{_selectedRule.Name}' видалено");

                LoadRules();
                _coordinator.LoadRules();
                ClearForm();
                SaveButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Помилка", $"Не вдалося видалити правило:\n{ex.Message}");
            }
        }
    }

    /// <summary>
    /// Вибрати порт з активних з'єднань
    /// </summary>
    private async void SelectActivePortButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var connections = _processLookupService.GetActiveTcpConnections();

            if (!connections.Any())
            {
                await this.ShowMessageAsync("Інформація", "Активні з'єднання не знайдено");
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
            await this.ShowMessageAsync("Помилка", $"Не вдалося отримати список активних портів:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Вибрати процес з запущених
    /// </summary>
    private async void SelectRunningProcessButton_Click(object sender, RoutedEventArgs e)
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
                await this.ShowMessageAsync("Інформація", "Процеси з активними з'єднаннями не знайдено");
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
            await this.ShowMessageAsync("Помилка", $"Не вдалося отримати список процесів:\n{ex.Message}");
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
