namespace GrayCat.UI.Controls;

using GrayCat.UI.Localization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using GrayCat.Core.Constants;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

/// <summary>
/// The page as it will look, rendered inside the editor. Clicking a block selects it;
/// edits are pushed into the page without reloading it.
/// </summary>
public class LivePreview : Grid
{
    private readonly WebView2 _webView = new();
    private readonly TextBlock _message;
    private readonly DispatcherTimer _refreshTimer;
    private Task? _initTask;
    private bool _ready;
    private bool _pageLoaded;
    private string? _loadedFolder;
    private string? _pageFile;

    /// <summary>The user clicked a block in the page (null = clicked outside any block).</summary>
    public event Action<string?>? BlockClicked;

    public Func<ProjectModel?>? ProjectSource { get; set; }
    public Func<string?>? FolderSource { get; set; }
    public Func<string?>? SelectedIdSource { get; set; }
    public Func<string>? PageSource { get; set; }

    public LivePreview()
    {
        Background = Brushes.White;
        _message = new TextBlock
        {
            Text = Loc.T("Loading preview…"),
            Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(40)
        };
        Children.Add(_webView);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += async (_, _) =>
        {
            _refreshTimer.Stop();
            await RefreshNowAsync();
        };

        Loaded += (_, _) => _initTask ??= InitializeAsync();
    }

    /// <summary>Simulates a device: null = full width, otherwise the page width in pixels.</summary>
    public void SetDeviceWidth(double? width)
    {
        _webView.Width = width ?? double.NaN;
        _webView.HorizontalAlignment = width == null ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        Background = width == null ? Brushes.White : new SolidColorBrush(Color.FromRgb(226, 232, 240));
    }

    /// <summary>Schedules a refresh (several quick edits collapse into one).</summary>
    public void RequestRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public async void Select(string? id)
    {
        if (!_ready || !_pageLoaded) return;
        try
        {
            await _webView.ExecuteScriptAsync($"window.gcSelect && window.gcSelect({JsonSerializer.Serialize(id)}, true)");
        }
        catch (InvalidOperationException)
        {
            // WebView is closing.
        }
    }


    private async Task InitializeAsync()
    {
        try
        {
            var dataFolder = Path.Combine(AppConstants.Paths.AppData, "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            await _webView.EnsureCoreWebView2Async(environment);

            var core = _webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.WebMessageReceived += OnWebMessage;
            // Only our own preview page may load in the editor; links are disabled by the page script anyway.
            core.NavigationStarting += (_, e) =>
            {
                if (_pageFile != null && !e.Uri.StartsWith(new Uri(_pageFile).AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                    e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;

            _ready = true;
            await RefreshNowAsync();
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException)
        {
            Children.Remove(_webView);
            Children.Add(_message);
            _message.Text = Loc.T("The live preview needs the Microsoft Edge WebView2 Runtime.\n") +
                            Loc.T("Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and restart GrayCat Studio.\n\n") +
                            Loc.T("You can still use the Layout tab and Project → Static HTML Preview.");
        }
    }

    private async Task RefreshNowAsync()
    {
        if (!_ready) return;
        var project = ProjectSource?.Invoke();
        if (project == null) return;
        var folder = FolderSource?.Invoke();
        var selected = SelectedIdSource?.Invoke();
        var page = PageSource?.Invoke() ?? string.Empty;

        try
        {
            if (_pageFile != null && !_pageLoaded && folder == _loadedFolder)
                return; // page still loading; its "ready" message triggers another refresh

            if (_pageLoaded && folder == _loadedFolder)
            {
                // Swap the body in place: no flicker, scroll position kept.
                var body = ProjectScaffolder.BuildEditorBody(project, folder, page);
                await _webView.ExecuteScriptAsync(
                    $"window.gcUpdate && window.gcUpdate({JsonSerializer.Serialize(body)}, {JsonSerializer.Serialize(selected)})");
                return;
            }

            // First load (or another project): write the page next to nothing sensitive and navigate to it,
            // so "assets/..." images resolve against the project folder through <base>.
            var previewDir = Path.Combine(Path.GetTempPath(), "GrayCat", "editor");
            Directory.CreateDirectory(previewDir);
            _pageFile = Path.Combine(previewDir, "page.html");
            await File.WriteAllTextAsync(_pageFile, ProjectScaffolder.BuildEditorHtml(project, folder, page));
            _pageLoaded = false;
            _loadedFolder = folder;
            _webView.CoreWebView2.Navigate(new Uri(_pageFile).AbsoluteUri);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _message.Text = Loc.F("Preview could not be updated: {0}", ex.Message);
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var type = doc.RootElement.GetProperty("type").GetString();
            if (type == "ready")
            {
                _pageLoaded = true;
                // Edits made while the page was loading.
                RequestRefresh();
            }
            else if (type == "select")
            {
                var id = doc.RootElement.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                    ? idElement.GetString()
                    : null;
                BlockClicked?.Invoke(id);
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Ignore malformed messages.
        }
    }
}
