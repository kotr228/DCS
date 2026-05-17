using System.Windows;
using System.Text.RegularExpressions;

namespace BlackCat.UI;

public partial class AddTunnelDialog : Window
{
    public string BlackID { get; private set; } = string.Empty;
    public string IPAddress { get; private set; } = string.Empty;
    public int Port { get; private set; } = 9999;

    public AddTunnelDialog()
    {
        InitializeComponent();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        // Валідація Black-ID
        if (string.IsNullOrWhiteSpace(BlackIDTextBox.Text))
        {
            MessageBox.Show("Введіть Black-ID віддаленого вузла!", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            BlackIDTextBox.Focus();
            return;
        }

        var blackID = BlackIDTextBox.Text.Trim().ToUpperInvariant();

        // Перевірка формату Black-ID: ROLE-CITY-NAME-CODE
        if (!Regex.IsMatch(blackID, @"^[A-Z0-9]+-[A-Z0-9]+-[A-Z0-9]+-[A-Z0-9]+$"))
        {
            MessageBox.Show(
                "Невірний формат Black-ID!\n\n" +
                "Правильний формат: РОЛЬ-МІСТО-НАЗВА-КОД\n" +
                "Приклад: SKLAD-ODESA-SERVER-7X99",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
            BlackIDTextBox.Focus();
            return;
        }

        // Валідація IP адреси
        if (string.IsNullOrWhiteSpace(IPAddressTextBox.Text))
        {
            MessageBox.Show("Введіть IP адресу!", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            IPAddressTextBox.Focus();
            return;
        }

        var ipAddress = IPAddressTextBox.Text.Trim();

        // Перевірка IP або доменного імені
        if (!IsValidIPAddress(ipAddress) && !IsValidDomainName(ipAddress))
        {
            MessageBox.Show(
                "Невірний формат IP адреси або доменного імені!\n\n" +
                "Приклади:\n" +
                "• IP адреса: 192.168.1.100\n" +
                "• Доменне ім'я: server.example.com",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
            IPAddressTextBox.Focus();
            return;
        }

        // Валідація порту
        if (!int.TryParse(PortTextBox.Text, out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show("Введіть коректний номер порту (1-65535)!", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            PortTextBox.Focus();
            return;
        }

        // Зберегти дані
        BlackID = blackID;
        IPAddress = ipAddress;
        Port = port;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Перевірка валідності IP адреси
    /// </summary>
    private bool IsValidIPAddress(string ip)
    {
        return System.Net.IPAddress.TryParse(ip, out _);
    }

    /// <summary>
    /// Перевірка валідності доменного імені
    /// </summary>
    private bool IsValidDomainName(string domain)
    {
        // Простий regex для доменного імені
        return Regex.IsMatch(domain, @"^[a-zA-Z0-9][a-zA-Z0-9-]{0,61}[a-zA-Z0-9]?(\.[a-zA-Z0-9][a-zA-Z0-9-]{0,61}[a-zA-Z0-9]?)*\.[a-zA-Z]{2,}$");
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();
    private void CloseButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
