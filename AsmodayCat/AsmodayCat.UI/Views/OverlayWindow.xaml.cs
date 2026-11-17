using AsmodayCat.UI.Localization;
using System.Windows;
using System.Windows.Input;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.Views;

public partial class OverlayWindow : Window
{
    private readonly IpcClient _ipc;

    public OverlayWindow(IpcClient ipc)
    {
        InitializeComponent();
        _ipc = ipc;
    }

    private void Window_Deactivated(object sender, EventArgs e) => Hide();

    private void QuickInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); return; }
        if (e.Key == Key.Enter)  { _ = SendQueryAsync(); e.Handled = true; }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) =>
        _ = SendQueryAsync();

    private async Task SendQueryAsync()
    {
        var text = QuickInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        ResponseBlock.Text = Loc.T("overlay.thinking");
        QuickInput.Clear();

        var answer = new System.Text.StringBuilder();
        await foreach (var chunk in _ipc.StreamCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.Chat,
            // Separate short conversation: doesn't mix with the main chat or its history
            Parameters = { ["Message"] = text, ["Quick"] = "true", ["AllowWeb"] = "true" }
        }))
        {
            if (!string.IsNullOrEmpty(chunk.Error))
            {
                answer.Append("\n" + Loc.F("chat.errorInline", chunk.Error));
                ResponseBlock.Text = answer.ToString();
                break;
            }
            if (!string.IsNullOrEmpty(chunk.Chunk))
            {
                answer.Append(chunk.Chunk);
                ResponseBlock.Text = answer.ToString();
            }
            if (chunk.Done) break;
        }
    }
}
