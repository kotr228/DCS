namespace GrayCat.UI.Tests;

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Shared.Models;
using GrayCat.UI.Services;
using GrayCat.UI.ViewModels;
using GrayCat.UI.Views;

/// <summary>
/// Renders the app's windows to PNG files off-screen (never shown on the monitor, never focused),
/// so the design can be reviewed without clicking through the UI.
/// Runs only when GRAYCAT_SNAPSHOT_DIR is set.
/// </summary>
public class WindowSnapshotTests
{
    [Fact]
    public void RenderWindows()
    {
        var dir = Environment.GetEnvironmentVariable("GRAYCAT_SNAPSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Render(dir); }
            catch (Exception ex) { failure = ex; }
            // Release this thread's windows and COM objects here, not later from the finalizer.
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("Rendering failed", failure);
    }

    private static void Render(string dir)
    {
        // The app's resources (theme, window template) without starting the app.
        var app = new App();
        app.InitializeComponent();

        var root = Path.Combine(Path.GetTempPath(), "graycat-snapshots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // Own settings folder: never touch the user's recent-projects list.
        MainViewModel NewViewModel() => new(new ProjectManager(Path.Combine(root, "settings"), Path.Combine(root, "work")));

        // Continue on this (UI) thread like the real app does.
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));

        foreach (var lang in new[] { "en", "uk" })
        {
            GrayCat.UI.Localization.Loc.Instance.Set(lang);
            Snap(new MainWindow(NewViewModel()), dir, $"main-welcome-{lang}.png", 1400, 860);

            var vm = NewViewModel();
            var main = new MainWindow(vm);
            var create = vm.CreateNewProjectAsync(new GenerationRequest { ProjectName = "Northwind-" + lang, OutputPath = root, Type = ProjectType.React },
                ProjectTemplates.Business);
            while (!create.IsCompleted) DoEvents();
            Assert.True(create.Result.Success, create.Result.Message);
            vm.SelectedBlock = vm.Blocks.FirstOrDefault(b => b.Type == "Hero");
            main.LayoutModeButton.IsChecked = true; // the live page is a browser control that does not render to bitmaps
            Snap(main, dir, $"main-project-{lang}.png", 1400, 860);

            var bottom = new MainWindow(vm);
            bottom.LayoutModeButton.IsChecked = true;
            Snap(bottom, dir, $"main-palette-bottom-{lang}.png", 1400, 860, w => ((MainWindow)w).PaletteScroll.ScrollToEnd());

            SnapPopups(vm, dir, lang);
            Snap(new NewProjectWindow(), dir, $"new-project-{lang}.png", 580, 0);
            Snap(new ProjectSettingsWindow(vm.CurrentProject!), dir, $"settings-{lang}.png", 520, 0);
            Snap(new PagesWindow(vm), dir, $"pages-{lang}.png", 620, 440);
            Snap(new ThemedMessageBox(GrayCat.UI.Localization.Loc.F("Delete the {0} block?", "Hero"),
                GrayCat.UI.Localization.Loc.T("Confirm Delete"), MessageBoxButton.YesNo, MessageBoxImage.Question), dir, $"message-{lang}.png", 0, 0);
            vm.Shutdown();
        }
        GrayCat.UI.Localization.Loc.Instance.Set("en");
    }

    /// <summary>Drop-downs live in their own popup windows, so open them and render their content directly.</summary>
    private static void SnapPopups(MainViewModel vm, string dir, string lang)
    {
        var main = new MainWindow(vm)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false, Width = 1400, Height = 860
        };
        main.Show();
        DoEvents();

        // Device combo box, opened
        main.DeviceBox.IsDropDownOpen = true;
        DoEvents();
        RenderPopup((System.Windows.Controls.Primitives.Popup)main.DeviceBox.Template.FindName("PART_Popup", main.DeviceBox), dir, $"popup-combo-{lang}.png");
        main.DeviceBox.IsDropDownOpen = false;

        // File menu in the title bar, opened
        var menu = (System.Windows.Controls.Menu)main.TitleBarContent!;
        var file = (System.Windows.Controls.MenuItem)menu.Items[0];
        file.IsSubmenuOpen = true;
        DoEvents();
        RenderPopup((System.Windows.Controls.Primitives.Popup)file.Template.FindName("PART_Popup", file), dir, $"popup-menu-{lang}.png");
        file.IsSubmenuOpen = false;

        main.Close();
    }

    private static void RenderPopup(System.Windows.Controls.Primitives.Popup popup, string dir, string file)
    {
        var child = (FrameworkElement)popup.Child;
        child.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth), (int)Math.Ceiling(child.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        // White backdrop so the rounded corners and shadow read like on screen.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(244, 242, 241)), null, new Rect(0, 0, child.ActualWidth, child.ActualHeight));
        bitmap.Render(visual);
        bitmap.Render(child);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, file));
        encoder.Save(stream);
    }

    private static void Snap(Window window, string dir, string file, double width, double height, Action<Window>? before = null)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        if (width > 0) window.Width = width;
        if (height > 0) window.Height = height;
        window.Show();
        window.UpdateLayout();
        DoEvents();
        if (before != null)
        {
            before(window);
            window.UpdateLayout();
            DoEvents();
        }

        var w = (int)Math.Ceiling(window.ActualWidth);
        var h = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(dir, file)))
            encoder.Save(stream);
        window.Close();
    }

    private static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
