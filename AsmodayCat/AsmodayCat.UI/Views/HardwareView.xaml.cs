using System.Windows.Controls;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class HardwareView : UserControl
{
    public HardwareView(HardwareViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Refresh from the service each time the tab is shown
        Loaded += (_, _) => vm.LoadConfigCommand.Execute(null);
    }
}
