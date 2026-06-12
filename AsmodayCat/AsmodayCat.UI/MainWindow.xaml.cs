using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using AsmodayCat.UI.Views;

namespace AsmodayCat.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NavList.SelectedIndex = 0;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item) return;

        ContentFrame.Content = item.Tag?.ToString() switch
        {
            "Dashboard"  => App.Services.GetRequiredService<DashboardView>(),
            "Hardware"   => App.Services.GetRequiredService<HardwareView>(),
            "AgentRules" => App.Services.GetRequiredService<AgentRulesView>(),
            "Terminal"   => App.Services.GetRequiredService<TerminalView>(),
            _            => null
        };
    }
}
