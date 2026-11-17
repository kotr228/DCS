using AsmodayCat.UI.Localization;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Forms = System.Windows.Forms;

namespace AsmodayCat.UI.Services;

// Icon next to the clock: AsmodayCat keeps running when its window is closed, so approvals,
// the voice hotkey and agent notifications keep working. "Вийти" in its menu really exits.
public sealed class TrayService : IDisposable
{
    private readonly IpcClient       _ipc;
    private readonly DispatcherTimer _poll;
    private Forms.NotifyIcon?        _icon;
    private string?                  _clickPath;     // what the last balloon opens
    private long                     _lastSeen = -1;
    private bool                     _hintShown;
    private int                      _failedPolls;
    private bool                     _restarting;

    public bool IsExiting { get; private set; }

    public TrayService(IpcClient ipc)
    {
        _ipc  = ipc;
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _poll.Tick += async (_, _) => await PollNotificationsAsync();
    }

    public void Start()
    {
        var menu = new Forms.ContextMenuStrip();
        var open    = menu.Items.Add(string.Empty, null, (_, _) => ShowMainWindow());
        var talk    = menu.Items.Add(string.Empty, null, (_, _) => StartVoice());
        var restart = menu.Items.Add(string.Empty, null, async (_, _) =>
        {
            var ok = await ServiceLauncher.RestartAsync(_ipc);
            Show("AsmodayCat", Loc.T(ok ? "tray.restarted" : "tray.restartFailed"), isError: !ok);
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        var exit = menu.Items.Add(string.Empty, null, (_, _) => Exit());

        // Menu texts follow the interface language
        void SetTexts()
        {
            open.Text    = Loc.T("tray.open");
            talk.Text    = Loc.T("tray.talk");
            restart.Text = Loc.T("tray.restart");
            exit.Text    = Loc.T("common.exit");
        }
        SetTexts();
        Loc.LanguageChanged += SetTexts;

        _icon = new Forms.NotifyIcon
        {
            Icon             = LoadIcon(),
            Text             = $"AsmodayCat {AppVersion.Display}",
            Visible          = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick       += (_, _) => ShowMainWindow();
        _icon.BalloonTipClicked += (_, _) => OpenClickPath();

        _poll.Start();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon)
                return icon;
        }
        catch (Exception) { /* fall back to the default */ }
        return System.Drawing.SystemIcons.Application;
    }

    public void ShowMainWindow()
    {
        var window = App.Services.GetRequiredService<MainWindow>();
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void StartVoice()
    {
        ShowMainWindow();
        App.Services.GetRequiredService<MainWindow>().StartVoiceInput();
    }

    // The main window was closed: keep running, tell the user once where we went
    public void OnMainWindowHidden()
    {
        if (_hintShown) return;
        _hintShown = true;
        Show(Loc.T("tray.hiddenTitle"), Loc.T("tray.hiddenText"));
    }

    public void Show(string title, string text, string? openPath = null, bool isError = false)
    {
        if (_icon is null) return;
        _clickPath = openPath;
        _icon.ShowBalloonTip(5000, title, text, isError ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    private void OpenClickPath()
    {
        if (_clickPath is null) { ShowMainWindow(); return; }
        try
        {
            if (File.Exists(_clickPath) || Directory.Exists(_clickPath))
                Process.Start(new ProcessStartInfo(_clickPath) { UseShellExecute = true });
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    private async Task PollNotificationsAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.GetNotifications, Parameters = { ["After"] = _lastSeen.ToString() }
        });
        if (resp?.Success != true || resp.Payload is not { } payload)
        {
            await RecoverServiceAsync();
            return;
        }
        _failedPolls = 0;

        var lastId = payload.GetProperty("LastId").GetInt64();
        if (_lastSeen >= 0)
        {
            foreach (var n in payload.GetProperty("Items").EnumerateArray())
            {
                Show(n.GetProperty("Title").GetString() ?? "AsmodayCat",
                     n.GetProperty("Text").GetString() ?? string.Empty,
                     n.TryGetProperty("OpenPath", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                     n.GetProperty("IsError").GetBoolean());
            }
        }
        _lastSeen = lastId;    // first call only learns where to start
    }

    // Watchdog: the service stopped answering (crash, killed) — start it again after ~9 s
    private async Task RecoverServiceAsync()
    {
        if (_restarting || ++_failedPolls < 3) return;
        _restarting = true;
        try
        {
            var ok = await ServiceLauncher.EnsureRunningAsync(_ipc);
            _lastSeen = -1;    // a new service numbers its notifications from scratch
            Show("AsmodayCat", Loc.T(ok ? "tray.watchdogOk" : "tray.watchdogFail"), isError: !ok);
        }
        finally
        {
            _failedPolls = 0;
            _restarting  = false;
        }
    }

    public void Exit()
    {
        IsExiting = true;
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _poll.Stop();
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}
