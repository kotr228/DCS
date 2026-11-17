using System.Windows.Controls;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class MemoryView : UserControl
{
    public MemoryView(MemoryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Fresh data each time the tab is opened
        Loaded += async (_, _) => await vm.LoadAsync();
    }
}
