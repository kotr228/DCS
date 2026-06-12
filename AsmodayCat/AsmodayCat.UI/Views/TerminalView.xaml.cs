using System.Collections.Specialized;
using System.Windows.Controls;
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

        vm.Lines.CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_vm.AutoScroll) return;
        if (LogList.Items.Count == 0) return;
        LogList.ScrollIntoView(LogList.Items[^1]);
    }
}
