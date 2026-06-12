using System.Windows.Controls;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class DashboardView : UserControl
{
    public DashboardView(DashboardViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
