using System.Windows;
using System.Windows.Controls;
using BlackCat.Core.Services;
using System.Collections.Generic;
using System.Linq;
namespace BlackCat.UI;

public partial class ActivePortsDialog : Window
{
    public int SelectedPort { get; private set; }
    public string? SelectedProcessName { get; private set; }
    public List<int>? SelectedPorts { get; private set; }

    public ActivePortsDialog(List<ConnectionInfo> connections)
    {
        InitializeComponent();

        // Сортувати за процесом та портом
        ConnectionsDataGrid.ItemsSource = connections
            .OrderBy(c => c.ProcessName)
            .ThenBy(c => c.LocalPort)
            .ToList();
    }

    private void ConnectionsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConnectionsDataGrid.SelectedItems.Count > 0)
        {
            SelectButton.IsEnabled = true;

            if (ConnectionsDataGrid.SelectedItems.Count == 1)
            {
                var connection = ConnectionsDataGrid.SelectedItem as ConnectionInfo;
                SelectionInfoTextBlock.Text = $"Вибрано: {connection?.ProcessName} - порт {connection?.LocalPort}";
            }
            else
            {
                SelectionInfoTextBlock.Text = $"Вибрано {ConnectionsDataGrid.SelectedItems.Count} портів";
            }
        }
        else
        {
            SelectButton.IsEnabled = false;
            SelectionInfoTextBlock.Text = "Не вибрано";
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsDataGrid.SelectedItems.Count == 1)
        {
            // Один порт
            var connection = ConnectionsDataGrid.SelectedItem as ConnectionInfo;
            if (connection != null)
            {
                SelectedPort = connection.LocalPort;
                SelectedProcessName = connection.ProcessName;
            }
        }
        else if (ConnectionsDataGrid.SelectedItems.Count > 1)
        {
            // Кілька портів
            SelectedPorts = ConnectionsDataGrid.SelectedItems
                .Cast<ConnectionInfo>()
                .Select(c => c.LocalPort)
                .Distinct()
                .ToList();

            // Взяти ProcessName з першого
            var first = ConnectionsDataGrid.SelectedItems[0] as ConnectionInfo;
            SelectedProcessName = first?.ProcessName;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();
    private void CloseButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
