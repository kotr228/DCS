using System.Windows.Controls;
using System.Windows.Input;
using AsmodayCat.UI.ViewModels;

namespace AsmodayCat.UI.Views;

public partial class ChatView : UserControl
{
    public ChatView(ChatViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
                vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
