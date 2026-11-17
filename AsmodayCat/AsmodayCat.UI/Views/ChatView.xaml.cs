using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class ChatView : UserControl
{
    private readonly ChatViewModel _vm;

    public ChatView(ChatViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        // Show the last conversation (and the history list) when the chat opens
        Loaded += async (_, _) => await vm.LoadHistoryAsync();

        // Auto-scroll to bottom when new messages arrive or content grows
        vm.Messages.CollectionChanged += (_, _) =>
            Dispatcher.InvokeAsync(Scroller.ScrollToEnd, DispatcherPriority.Background);
    }

    // Enter sends, Shift+Enter makes a new line, Ctrl+V pastes pictures and copied files as attachments
    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (_vm.SendCommand.CanExecute(null)) _vm.SendCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && _vm.PasteFromClipboard())
        {
            e.Handled = true;   // attached a picture/files; plain text pastes as usual
        }
    }

    private void Files_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Files_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        _vm.AddAttachments(files);
        e.Handled = true;
    }
}
