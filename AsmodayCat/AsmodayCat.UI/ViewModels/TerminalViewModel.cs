using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsmodayCat.UI.ViewModels;

public partial class TerminalViewModel : ObservableObject
{
    public ObservableCollection<string> Lines { get; } = [];

    [ObservableProperty] private bool _autoScroll = true;

    public void AppendLine(string line)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Lines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (Lines.Count > 500)
                Lines.RemoveAt(0);
        });
    }

    [RelayCommand]
    private void Clear() => Lines.Clear();
}
