using System.Windows.Controls;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class AgentRulesView : UserControl
{
    public AgentRulesView(AgentRulesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
