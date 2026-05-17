using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using BlackCat.Core.Services;
using BlackCat.Shared.Models;
namespace BlackCat.UI;

public partial class BlackIDCreationDialog : Window
{
    private readonly BlackIDService _blackIDService;
    public BlackID? CreatedBlackID { get; private set; }

    public BlackIDCreationDialog(BlackIDService blackIDService)
    {
        InitializeComponent();
        _blackIDService = blackIDService;

        // Підписатись на зміни для preview
        RoleComboBox.SelectionChanged += UpdatePreview;
        CityComboBox.SelectionChanged += UpdatePreview;
        NameTextBox.TextChanged += UpdatePreview;

        UpdatePreview(null, null);
    }

    private void UpdatePreview(object? sender, EventArgs? e)
    {
        try
        {
            // Перевірка, чи елементи вже ініціалізовані
            if (RoleComboBox == null || CityComboBox == null || NameTextBox == null || PreviewTextBlock == null)
                return;

            var role = (RoleComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "MAIN";
            var city = (CityComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "KYIV";
            var name = NameTextBox.Text.ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "SERVER";
            }

            // Видалити недозволені символи
            name = Regex.Replace(name, @"[^A-Z0-9]", "");

            if (name.Length > 20)
            {
                name = name.Substring(0, 20);
            }

            PreviewTextBlock.Text = $"{role}-{city}-{name}-XXXX";
        }
        catch
        {
            // Ігнорувати помилки під час ініціалізації
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var role = (RoleComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "MAIN";
            var city = (CityComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "KYIV";
            var name = NameTextBox.Text?.ToUpperInvariant().Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Введіть назву!", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            // Видалити недозволені символи
            name = Regex.Replace(name, @"[^A-Z0-9]", "");

            if (name.Length < 2)
            {
                MessageBox.Show("Назва занадто коротка (мінімум 2 символи)!", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            // Відладкова інформація
            System.Diagnostics.Debug.WriteLine($"Creating Black-ID: Role={role}, City={city}, Name={name}");

            // Створити Black-ID
            if (_blackIDService == null)
            {
                MessageBox.Show("BlackIDService не ініціалізовано!", "Критична помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            CreatedBlackID = _blackIDService.GenerateID(role, city, name);

            if (CreatedBlackID == null)
            {
                MessageBox.Show("Не вдалося створити Black-ID (результат null)!", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Created Black-ID: {CreatedBlackID.FullID}");

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            var errorMessage = $"Помилка створення Black-ID:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
            System.Diagnostics.Debug.WriteLine(errorMessage);
            MessageBox.Show(errorMessage, "Критична помилка",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
