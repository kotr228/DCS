using System.Windows.Controls;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class HardwareView : UserControl
{
    public HardwareView(HardwareViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
