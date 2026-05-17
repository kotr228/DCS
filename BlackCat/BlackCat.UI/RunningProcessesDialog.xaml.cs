using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;
namespace BlackCat.UI;

public partial class RunningProcessesDialog : Window
{
    public string? SelectedProcessName { get; private set; }

    public RunningProcessesDialog(dynamic processes)
    {
        InitializeComponent();

        // Перетворити на список ProcessDisplayInfo
        var displayList = new List<ProcessDisplayInfo>();

        foreach (var p in processes)
        {
            displayList.Add(new ProcessDisplayInfo
            {
                ProcessName = p.ProcessName,
                ProcessId = p.ProcessId,
                ConnectionCount = p.ConnectionCount,
                Ports = p.Ports,
                PortsDisplay = string.Join(", ", ((List<int>)p.Ports).Take(10)) +
                              (((List<int>)p.Ports).Count > 10 ? "..." : "")
            });
        }

        ProcessesDataGrid.ItemsSource = displayList;
    }

    private void ProcessesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessesDataGrid.SelectedItem is ProcessDisplayInfo info)
        {
            SelectButton.IsEnabled = true;
            SelectionInfoTextBlock.Text = $"Вибрано: {info.ProcessName} ({info.ConnectionCount} з'єднань)";
        }
        else
        {
            SelectButton.IsEnabled = false;
            SelectionInfoTextBlock.Text = "Не вибрано";
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessesDataGrid.SelectedItem is ProcessDisplayInfo info)
        {
            SelectedProcessName = info.ProcessName;
            DialogResult = true;
            Close();
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

/// <summary>
/// Клас для відображення інформації про процес
/// </summary>
public class ProcessDisplayInfo
{
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public int ConnectionCount { get; set; }
    public List<int> Ports { get; set; } = new();
    public string PortsDisplay { get; set; } = string.Empty;
}
