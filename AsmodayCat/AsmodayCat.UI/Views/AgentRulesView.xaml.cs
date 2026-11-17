using System.Windows.Controls;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class AgentRulesView : UserControl
{
    public AgentRulesView(AgentRulesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Refresh from the service each time the tab is shown
        Loaded += (_, _) => vm.LoadRulesCommand.Execute(null);
    }
}
