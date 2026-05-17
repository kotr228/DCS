using System.Net;
using System.Windows;
using BlackCat.Core.Data;
using BlackCat.Shared.Models;
namespace BlackCat.UI;

public partial class AddPeerNodeDialog : Window
{
    private readonly PeerNodeRepository _peerNodeRepository;
    public PeerNode? CreatedPeerNode { get; private set; }

    public AddPeerNodeDialog(PeerNodeRepository peerNodeRepository)
    {
        InitializeComponent();
        _peerNodeRepository = peerNodeRepository;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Валідація Black-ID
            var blackID = BlackIDTextBox.Text.Trim();
            if (string.IsNullOrEmpty(blackID))
            {
                MessageBox.Show("Введіть Black-ID вузла!", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                BlackIDTextBox.Focus();
                return;
            }

            if (!BlackID.IsValid(blackID))
            {
                MessageBox.Show($"Невірний формат Black-ID!\n\n" +
                              $"Очікуваний формат: ROLE-CITY-NAME-CODE\n" +
                              $"Приклад: OFFICE-KYIV-PC1-A7B9",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                BlackIDTextBox.Focus();
                return;
            }

            // Перевірити чи не існує вже
            var existing = _peerNodeRepository.GetPeerNodeByBlackID(blackID);
            if (existing != null)
            {
                var result = MessageBox.Show(
                    $"Вузол з Black-ID '{blackID}' вже існує в телефонній книзі!\n\n" +
                    $"Назва: {existing.DisplayName}\n" +
                    $"Адреса: {existing.Address}:{existing.Port}\n\n" +
                    $"Оновити існуючий запис?",
                    "Вузол вже існує",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                    return;

                // Оновити існуючий
                UpdateExistingNode(existing);
                return;
            }

            // Валідація IP адреси
            var ipAddress = IPAddressTextBox.Text.Trim();
            if (string.IsNullOrEmpty(ipAddress))
            {
                MessageBox.Show("Введіть IP адресу!", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                IPAddressTextBox.Focus();
                return;
            }

            if (!IPAddress.TryParse(ipAddress, out _))
            {
                MessageBox.Show($"Невірний формат IP адреси!\n\n" +
                              $"Приклад: 192.168.1.100",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                IPAddressTextBox.Focus();
                return;
            }

            // Валідація назви
            var displayName = DisplayNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(displayName))
            {
                MessageBox.Show("Введіть назву для відображення!", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DisplayNameTextBox.Focus();
                return;
            }

            // Створити новий вузол
            var peerNode = new PeerNode
            {
                BlackID = blackID,
                Address = ipAddress,
                Port = (int)(PortNumeric?.Value ?? 9999),
                DisplayName = displayName,
                Description = DescriptionTextBox.Text.Trim(),
                IsTrusted = IsTrustedCheckBox.IsChecked == true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // Зберегти в БД
            int id = _peerNodeRepository.AddPeerNode(peerNode);
            peerNode.Id = id;

            CreatedPeerNode = peerNode;

            MessageBox.Show(
                $"✅ Вузол додано до телефонної книги!\n\n" +
                $"Black-ID: {blackID}\n" +
                $"Адреса: {ipAddress}:{peerNode.Port}\n" +
                $"Довіряти: {(peerNode.IsTrusted ? "Так" : "Ні")}",
                "Успіх",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка збереження вузла:\n\n{ex.Message}",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateExistingNode(PeerNode existing)
    {
        try
        {
            existing.Address = IPAddressTextBox.Text.Trim();
            existing.Port = (int)(PortNumeric?.Value ?? 9999);
            existing.DisplayName = DisplayNameTextBox.Text.Trim();
            existing.Description = DescriptionTextBox.Text.Trim();
            existing.IsTrusted = IsTrustedCheckBox.IsChecked == true;

            _peerNodeRepository.UpdatePeerNode(existing);

            CreatedPeerNode = existing;

            MessageBox.Show(
                $"✅ Вузол оновлено!\n\n" +
                $"Black-ID: {existing.BlackID}\n" +
                $"Нова адреса: {existing.Address}:{existing.Port}",
                "Успіх",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка оновлення вузла:\n\n{ex.Message}",
                "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();
    private void CloseButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
