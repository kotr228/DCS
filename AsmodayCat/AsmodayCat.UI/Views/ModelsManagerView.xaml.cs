using System.Windows.Controls;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class ModelsManagerView : UserControl
{
    public ModelsManagerView(ModelsManagerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _ = vm.RefreshCommand.ExecuteAsync(null);
    }
}
