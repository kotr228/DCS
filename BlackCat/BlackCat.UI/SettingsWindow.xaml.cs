using System.Diagnostics;
using System.IO;
using System.Windows;
using BlackCat.Core;
using BlackCat.Core.Services;
using BlackCat.Shared.Models;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace BlackCat.UI;

public partial class SettingsWindow : MetroWindow
{
    private readonly FirewallCoordinator? _coordinator;
    private readonly BlackIDService _blackIDService;
    private readonly HardwareFingerprintService _hardwareService;

    public SettingsWindow(FirewallCoordinator? coordinator = null)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _blackIDService = new BlackIDService();
        _hardwareService = new HardwareFingerprintService();

        LoadSettings();
    }

    private void LoadSettings()
    {
        // Завантажити Black-ID
        if (_coordinator?.CurrentBlackID != null)
        {
            CurrentBlackIDTextBox.Text = _coordinator.CurrentBlackID.FullID;
        }
        else
        {
            CurrentBlackIDTextBox.Text = "Не налаштовано";
        }

        // Завантажити hardware info
        try
        {
            var hwInfo = _hardwareService.GetHardwareInfo();
            CpuIdTextBox.Text = hwInfo.CpuId;
            MotherboardTextBox.Text = hwInfo.MotherboardSerial;
            MacAddressTextBox.Text = hwInfo.MacAddress;
            FingerprintTextBox.Text = hwInfo.Fingerprint;
        }
        catch (Exception ex)
        {
            CpuIdTextBox.Text = $"Помилка: {ex.Message}";
        }

        // TODO: Завантажити інші налаштування з конфігу
    }

    private async void CreateBlackIDButton_Click(object sender, RoutedEventArgs e)
    {
        // Показати діалог вибору ролі, міста, назви
        var dialog = new BlackIDCreationDialog(_blackIDService);
        dialog.Owner = this;

        if (dialog.ShowDialog() == true && dialog.CreatedBlackID != null)
        {
            if (_coordinator != null)
            {
                _coordinator.ConfigureBlackID(
                    dialog.CreatedBlackID.Role,
                    dialog.CreatedBlackID.City,
                    dialog.CreatedBlackID.Name
                );

                CurrentBlackIDTextBox.Text = dialog.CreatedBlackID.FullID;

                await this.ShowMessageAsync("Успіх",
                    $"Створено новий Black-ID:\n{dialog.CreatedBlackID.FullID}\n\n" +
                    "Збережіть цей код - він потрібен для з'єднання з іншими вузлами!",
                    MessageDialogStyle.Affirmative);
            }
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Зберегти налаштування в конфіг файл
        try
        {
            // Тут буде збереження в JSON конфіг
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка збереження: {ex.Message}", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не вдалося відкрити папку: {ex.Message}", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
