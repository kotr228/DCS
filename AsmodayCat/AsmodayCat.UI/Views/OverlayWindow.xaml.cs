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

        ResponseBlock.Text = "Thinking...";

        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.Chat,
            Parameters = { ["Message"] = text }
        });

        ResponseBlock.Text = resp?.Success == true && resp.Payload.HasValue
            ? resp.Payload.Value.TryGetProperty("Content", out var c)
                ? c.GetString() ?? string.Empty
                : string.Empty
            : $"Error: {resp?.Error ?? "Service offline"}";

        QuickInput.Clear();
    }
}
