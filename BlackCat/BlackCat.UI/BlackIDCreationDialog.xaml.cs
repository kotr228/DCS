using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using BlackCat.Core.Services;
using BlackCat.Shared.Models;
using MahApps.Metro.Controls;

namespace BlackCat.UI;

public partial class BlackIDCreationDialog : MetroWindow
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

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var role = (RoleComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "MAIN";
            var city = (CityComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "KYIV";
            var name = NameTextBox.Text.ToUpperInvariant().Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Введіть назву!", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Видалити недозволені символи
            name = Regex.Replace(name, @"[^A-Z0-9]", "");

            if (name.Length < 2)
            {
                MessageBox.Show("Назва занадто коротка (мінімум 2 символи)!", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Створити Black-ID
            CreatedBlackID = _blackIDService.GenerateID(role, city, name);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка створення Black-ID: {ex.Message}", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
