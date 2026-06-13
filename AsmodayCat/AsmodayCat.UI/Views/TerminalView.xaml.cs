using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class TerminalView : UserControl
{
    private readonly TerminalViewModel _vm;

    public TerminalView(TerminalViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.AllLogs.CollectionChanged += OnLogsChanged;
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_vm.AutoScroll) return;
        Dispatcher.InvokeAsync(Scroller.ScrollToBottom, DispatcherPriority.Background);
    }
}
