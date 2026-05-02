using System.Diagnostics;
using System.IO;
using System.Windows;
using BlackCat.Core;
using BlackCat.Core.Data;
using BlackCat.Core.Services;
using BlackCat.Shared.Models;
using MahApps.Metro.Controls;

namespace BlackCat.UI;

public partial class SettingsWindow : MetroWindow
{
    private readonly FirewallCoordinator? _coordinator;
    private readonly BlackIDService _blackIDService;
    private readonly HardwareFingerprintService _hardwareService;
    private readonly BlackIDRepository _blackIDRepository;
    private readonly BlackCatDatabase _database;

    public SettingsWindow(FirewallCoordinator? coordinator = null)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _blackIDService = new BlackIDService();
        _hardwareService = new HardwareFingerprintService();

        // Ініціалізувати database та repository для збереження Black-ID
        _database = new BlackCatDatabase("blackcat.db");
        _blackIDRepository = new BlackIDRepository(_database);

        LoadSettings();
        LoadConnectionInfo();
    }

    private void LoadSettings()
    {
        // Завантажити Black-ID з бази даних
        try
        {
            var savedBlackID = _blackIDRepository.GetActiveBlackID();
            if (savedBlackID != null)
            {
                CurrentBlackIDTextBox.Text = savedBlackID.FullID;
                System.Diagnostics.Debug.WriteLine($"Loaded saved Black-ID: {savedBlackID.FullID}");
            }
            else if (_coordinator?.CurrentBlackID != null)
            {
                // Fallback: якщо в БД немає, але в coordinator є
                CurrentBlackIDTextBox.Text = _coordinator.CurrentBlackID.FullID;
            }
            else
            {
                CurrentBlackIDTextBox.Text = "Не налаштовано";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading Black-ID: {ex.Message}");
            CurrentBlackIDTextBox.Text = "Помилка завантаження";
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

    private void CreateBlackIDButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("CreateBlackIDButton_Click - Started");

            if (_blackIDService == null)
            {
                MessageBox.Show("BlackIDService не ініціалізовано!", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            System.Diagnostics.Debug.WriteLine("Opening BlackIDCreationDialog...");

            // Показати діалог вибору ролі, міста, назви
            var dialog = new BlackIDCreationDialog(_blackIDService);
            dialog.Owner = this;

            System.Diagnostics.Debug.WriteLine("Calling ShowDialog...");
            var result = dialog.ShowDialog();

            System.Diagnostics.Debug.WriteLine($"Dialog result: {result}, CreatedBlackID: {dialog.CreatedBlackID?.FullID ?? "null"}");

            if (result == true && dialog.CreatedBlackID != null)
            {
                System.Diagnostics.Debug.WriteLine("Dialog returned true with valid Black-ID");

                try
                {
                    // ВАЖЛИВО: Зберегти Black-ID в базу даних незалежно від стану coordinator
                    System.Diagnostics.Debug.WriteLine($"Saving Black-ID to database: {dialog.CreatedBlackID.FullID}");
                    _blackIDRepository.SaveBlackID(dialog.CreatedBlackID);
                    System.Diagnostics.Debug.WriteLine("Black-ID saved successfully to database");

                    // Оновити UI
                    CurrentBlackIDTextBox.Text = dialog.CreatedBlackID.FullID;

                    // Також налаштувати coordinator якщо він запущений
                    if (_coordinator != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Configuring Black-ID in coordinator...");

                        _coordinator.ConfigureBlackID(
                            dialog.CreatedBlackID.Role,
                            dialog.CreatedBlackID.City,
                            dialog.CreatedBlackID.Name
                        );

                        MessageBox.Show(
                            $"Створено новий Black-ID:\n{dialog.CreatedBlackID.FullID}\n\n" +
                            "✅ Збережено в базу даних\n" +
                            "✅ Налаштовано брандмауер\n\n" +
                            "Збережіть цей код - він потрібен для з'єднання з іншими вузлами!",
                            "Успіх",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Coordinator is null - saved to database only");

                        MessageBox.Show(
                            $"Створено новий Black-ID:\n{dialog.CreatedBlackID.FullID}\n\n" +
                            "✅ Збережено в базу даних\n\n" +
                            "Збережіть цей код - він потрібен для з'єднання з іншими вузлами!\n" +
                            "ID буде автоматично завантажено при запуску брандмауера.",
                            "Успіх",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                catch (Exception saveEx)
                {
                    var saveErrorMessage = $"Помилка збереження Black-ID:\n\n{saveEx.Message}\n\nStack Trace:\n{saveEx.StackTrace}";
                    System.Diagnostics.Debug.WriteLine(saveErrorMessage);
                    MessageBox.Show(saveErrorMessage, "Критична помилка збереження",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Dialog was cancelled or returned no Black-ID");
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Помилка відкриття діалогу:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
            System.Diagnostics.Debug.WriteLine(errorMessage);
            MessageBox.Show(errorMessage, "Критична помилка",
                MessageBoxButton.OK, MessageBoxImage.Error);
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

    // ============================================
    // ІНФОРМАЦІЯ ДЛЯ З'ЄДНАННЯ
    // ============================================

    private async void LoadConnectionInfo()
    {
        try
        {
            var networkService = new NetworkInfoService();

            // Завантажити Black-ID
            var savedBlackID = _blackIDRepository.GetActiveBlackID();
            if (savedBlackID != null)
            {
                ConnectionBlackIDTextBox.Text = savedBlackID.FullID;
                ConnectionFingerprintTextBox.Text = savedBlackID.HardwareFingerprint;
            }
            else
            {
                ConnectionBlackIDTextBox.Text = "Не налаштовано (створіть Black-ID)";
                ConnectionFingerprintTextBox.Text = "N/A";
            }

            // Завантажити мережеву інформацію
            var networkInfo = await networkService.GetNetworkInfoAsync();

            LocalIPTextBox.Text = networkInfo.LocalIPAddress;
            ExternalIPTextBox.Text = networkInfo.ExternalIPAddress;
            HostNameTextBox.Text = networkInfo.HostName;

            // Порт
            int port = (int)(TunnelPortNumeric?.Value ?? 9999);
            ConnectionPortTextBox.Text = port.ToString();

            // Перевірити статус порту
            bool isPortOpen = networkService.IsPortOpen(port);
            PortStatusTextBox.Text = isPortOpen ? "✅ Доступний" : "⚠️ Можливо зайнятий";
            PortStatusTextBox.Foreground = isPortOpen ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Orange;

            // Повна адреса (локальна)
            FullAddressTextBox.Text = $"{networkInfo.LocalIPAddress}:{port}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка завантаження мережевої інформації:\n{ex.Message}",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RefreshConnectionInfo_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as System.Windows.Controls.Button;
        if (button != null)
        {
            button.IsEnabled = false;
            button.Content = "🔄 Оновлення...";
        }

        await Task.Run(async () => await Task.Delay(500)); // Короткий delay для UI feedback
        LoadConnectionInfo();

        if (button != null)
        {
            await Task.Delay(500);
            button.Content = "🔄 Оновити дані";
            button.IsEnabled = true;
        }

        MessageBox.Show("✅ Інформацію оновлено!", "Успіх",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopyBlackID_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(ConnectionBlackIDTextBox.Text, "Black-ID");
    }

    private void CopyLocalIP_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(LocalIPTextBox.Text, "Локальну IP");
    }

    private void CopyExternalIP_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(ExternalIPTextBox.Text, "Зовнішню IP");
    }

    private void CopyPort_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(ConnectionPortTextBox.Text, "Порт");
    }

    private void CopyFingerprint_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(ConnectionFingerprintTextBox.Text, "HW Fingerprint");
    }

    private void CopyFullAddress_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(FullAddressTextBox.Text, "Повну адресу");
    }

    private void CopyAllConnectionInfo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = $@"═══════════════════════════════════════════
   BLACKCAT FIREWALL - ІНФОРМАЦІЯ ДЛЯ З'ЄДНАННЯ
═══════════════════════════════════════════

🆔 Black-ID:
   {ConnectionBlackIDTextBox.Text}

🏠 Локальна IP адреса (для локальної мережі):
   {LocalIPTextBox.Text}:{ConnectionPortTextBox.Text}

🌐 Зовнішня IP адреса (для Інтернет з'єднання):
   {ExternalIPTextBox.Text}:{ConnectionPortTextBox.Text}

🔌 Порт:
   {ConnectionPortTextBox.Text}

🔑 Hardware Fingerprint:
   {ConnectionFingerprintTextBox.Text}

🖥️ Ім'я хоста:
   {HostNameTextBox.Text}

═══════════════════════════════════════════
   ІНСТРУКЦІЇ
═══════════════════════════════════════════

Для з'єднання в ЛОКАЛЬНІЙ МЕРЕЖІ:
1. Додайте вузол з адресою: {LocalIPTextBox.Text}:{ConnectionPortTextBox.Text}
2. Вкажіть Black-ID: {ConnectionBlackIDTextBox.Text}
3. Позначте 'IsTrusted = true'

Для з'єднання через ІНТЕРНЕТ:
1. Налаштуйте Port Forwarding на роутері: {ConnectionPortTextBox.Text} → {LocalIPTextBox.Text}:{ConnectionPortTextBox.Text}
2. Додайте вузол з адресою: {ExternalIPTextBox.Text}:{ConnectionPortTextBox.Text}
3. Вкажіть Black-ID: {ConnectionBlackIDTextBox.Text}
4. Позначте 'IsTrusted = true'

═══════════════════════════════════════════
Згенеровано: {DateTime.Now:dd.MM.yyyy HH:mm:ss}
BlackCat Firewall - MQE Quantum-Resistant Encryption
═══════════════════════════════════════════";

            System.Windows.Clipboard.SetText(info);

            MessageBox.Show("✅ Вся інформація скопійована в буфер обміну!\n\n" +
                          "Тепер ви можете надіслати її іншому користувачу через захищений канал.",
                "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка копіювання: {ex.Message}", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyToClipboard(string text, string itemName)
    {
        try
        {
            if (string.IsNullOrEmpty(text) || text.Contains("Невідомо") || text.Contains("Не налаштовано"))
            {
                MessageBox.Show($"⚠️ {itemName} недоступна для копіювання.\n\n" +
                              "Спочатку створіть Black-ID або оновіть інформацію.",
                    "Попередження", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            System.Windows.Clipboard.SetText(text);
            MessageBox.Show($"✅ {itemName} скопійовано в буфер обміну!", "Успіх",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка копіювання: {ex.Message}", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
