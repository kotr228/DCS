namespace GrayCat.UI.Views;

using GrayCat.UI.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GrayCat.Core.Constants;
using GrayCat.Core.Generation;
using GrayCat.Core.Models.Generation;
using GrayCat.Core.Validation;
using GrayCat.UI.Controls;
using GrayCat.UI.Services;
using GrayCat.UI.ViewModels;
using Microsoft.Win32;

public partial class MainWindow : ThemedWindow
{
    private const double GridSize = 20.0;

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private BlockViewModel? _draggedBlock;
    private Point _dragStartPoint;
    private Point _paletteDragStart;
    private bool _isDragging;
    private bool _showGrid;
    private bool _closingConfirmed;

    public static readonly DependencyProperty WindowTitleProperty =
        DependencyProperty.Register(nameof(WindowTitle), typeof(string), typeof(MainWindow), new PropertyMetadata("GrayCat Studio"));

    public string WindowTitle
    {
        get => (string)GetValue(WindowTitleProperty);
        set => SetValue(WindowTitleProperty, value);
    }

    public MainWindow() : this(null)
    {
    }

    /// <param name="viewModel">Replaces the default view model (tests pass one with their own settings folders).</param>
    internal MainWindow(MainViewModel? viewModel)
    {
        InitializeComponent();
        if (viewModel != null)
            DataContext = viewModel;
        LanguageBox.SelectedIndex = Loc.Instance.Lang == "uk" ? 0 : 1;
        VersionText.Text = $"GrayCat Studio {AppConstants.AppVersion}";
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ProjectName) or nameof(MainViewModel.HasUnsavedChanges) or nameof(MainViewModel.IsProjectOpen))
                UpdateTitle();
        };
        UpdateTitle();

        // Live page: shows the current project, click selects, edits refresh it.
        LivePage.ProjectSource = () => ViewModel.CurrentProject;
        LivePage.FolderSource = () => ViewModel.WorkFolder; // unpacked .graycat: images resolve from here
        LivePage.SelectedIdSource = () => ViewModel.SelectedBlock?.Id;
        LivePage.PageSource = () => ViewModel.CurrentPageSlug;
        LivePage.BlockClicked += id =>
            ViewModel.SelectedBlock = id == null ? null : ViewModel.Blocks.FirstOrDefault(b => b.Id == id);
        ViewModel.ContentChanged += (_, _) => LivePage.RequestRefresh();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedBlock))
                LivePage.Select(ViewModel.SelectedBlock?.Id);
        };

        Loaded += async (_, _) =>
        {
            await ViewModel.RefreshRecentProjectsAsync();
            await OpenFromCommandLineAsync();
        };
        Closed += (_, _) => ViewModel.Shutdown();

        // Tests pass their own view model; only the real app remembers where the window was.
        if (viewModel == null)
        {
            RestorePlacement();
            Closing += (_, e) => { if (!e.Cancel) SavePlacement(); };
        }
    }

    private void RestorePlacement()
    {
        var p = UiSettings.Load().MainWindow;
        if (p == null || p.Width < MinWidth || p.Height < MinHeight)
            return;

        // Only if the window would still be reachable (a monitor may have been unplugged).
        var visible = p.Left + 100 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                      p.Top + 50 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight &&
                      p.Left + p.Width > SystemParameters.VirtualScreenLeft + 100 &&
                      p.Top >= SystemParameters.VirtualScreenTop - 10;
        if (!visible)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left;
        Top = p.Top;
        Width = p.Width;
        Height = p.Height;
        if (p.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty || bounds.Width <= 0)
            return;
        var settings = UiSettings.Load();
        settings.MainWindow = new UiSettings.WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized
        };
        settings.Save();
    }

    /// <summary>Supports "GrayCat.UI.exe path\to\graycat.project.json" (e.g. double-click on the file).</summary>
    private async Task OpenFromCommandLineAsync()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length < 2 || !File.Exists(args[1]))
            return;
        try
        {
            await ViewModel.LoadProjectAsync(Path.GetFullPath(args[1]));
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not open the project:\n\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateTitle()
    {
        WindowTitle = ViewModel.IsProjectOpen
            ? $"{ViewModel.ProjectName}{(ViewModel.HasUnsavedChanges ? " •" : "")} - GrayCat Studio"
            : "GrayCat Studio";
    }

    // ========== FILE MENU ==========

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewProjectWindow { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var result = await ViewModel.CreateNewProjectAsync(dialog.ViewModel.CreateRequest(), dialog.ViewModel.Template);
            if (!result.Success)
                ShowError(Loc.T("Could not create the project"), result);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not create the project:\n\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"{Loc.T("GrayCat project")} (*{GrayCatDocument.Extension})|*{GrayCatDocument.Extension}|" +
                     $"{Loc.T("GrayCat 1.0 project")} ({ProjectScaffolder.ProjectFileName})|{ProjectScaffolder.ProjectFileName}",
            Title = Loc.T("Open Project")
        };
        if (dialog.ShowDialog(this) != true)
            return;

        if (!await ViewModel.FlushAsync())
        {
            ThemedMessageBox.Show(this, Loc.T("The current project could not be saved, so it was kept open."), Loc.T("Open Project"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await ViewModel.LoadProjectAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not open the project:\n\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (!await ViewModel.SaveProjectAsync())
            ThemedMessageBox.Show(this, ViewModel.StatusMessage, Loc.T("Save"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void SaveProjectAs_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.DocumentPath is not { } current)
            return;
        var dialog = new SaveFileDialog
        {
            Filter = $"{Loc.T("GrayCat project")} (*{GrayCatDocument.Extension})|*{GrayCatDocument.Extension}",
            DefaultExt = GrayCatDocument.Extension,
            AddExtension = true,
            InitialDirectory = Path.GetDirectoryName(current),
            FileName = Path.GetFileNameWithoutExtension(current) + " 2",
            Title = Loc.T("Save Project As")
        };
        if (dialog.ShowDialog(this) != true)
            return;

        if (GenerationValidator.ValidateProjectName(Path.GetFileNameWithoutExtension(dialog.FileName)) is { } error)
        {
            ThemedMessageBox.Show(this, error, Loc.T("Save Project As"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!await ViewModel.SaveProjectAsAsync(dialog.FileName))
            ThemedMessageBox.Show(this, ViewModel.StatusMessage, Loc.T("Save Project As"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ProjectFolder is { } folder && Directory.Exists(folder))
        {
            OpenInShell(folder);
        }
        else if (ViewModel.DocumentPath is { } document)
        {
            // No code yet: show the .graycat file instead.
            try { Process.Start("explorer.exe", $"/select,\"{document}\""); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { OpenInShell(Path.GetDirectoryName(document)!); }
        }
    }

    private void Associate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociation.Register();
            ThemedMessageBox.Show(this, Loc.T("Double-clicking a .graycat file now opens it in GrayCat Studio."), Loc.T("File association"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not register .graycat files:\n\n{0}", ex.Message), Loc.T("File association"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RecentProject_Click(object sender, RoutedEventArgs e)
    {
        var path = (sender as FrameworkElement)?.DataContext as string ?? (sender as FrameworkElement)?.Tag as string;
        if (path == null)
            return;
        e.Handled = true;

        if (!File.Exists(path))
        {
            ThemedMessageBox.Show(this, Loc.F("The project file no longer exists:\n{0}", path), Loc.T("Open Recent"), MessageBoxButton.OK, MessageBoxImage.Warning);
            await ViewModel.RefreshRecentProjectsAsync();
            return;
        }
        if (!await ViewModel.FlushAsync())
            return;
        try
        {
            await ViewModel.LoadProjectAsync(path);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not open the project:\n\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closingConfirmed || !ViewModel.HasUnsavedChanges)
            return;

        // Save pending changes before the window goes away.
        e.Cancel = true;
        if (await ViewModel.SaveProjectAsync() ||
            ThemedMessageBox.Show(this, Loc.F("{0}\n\nClose without saving?", ViewModel.StatusMessage), "GrayCat Studio",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _closingConfirmed = true;
            Close();
        }
    }

    // ========== EDIT MENU ==========

    private void Undo_Click(object sender, RoutedEventArgs e) => ViewModel.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => ViewModel.Redo();
    private void Cut_Click(object sender, RoutedEventArgs e) => ViewModel.CutSelected();
    private void Copy_Click(object sender, RoutedEventArgs e) => ViewModel.CopySelected();
    private void Paste_Click(object sender, RoutedEventArgs e) => ViewModel.Paste();

    private void DuplicateSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBlock != null)
            ViewModel.DuplicateBlock(ViewModel.SelectedBlock);
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e) => ConfirmDelete(ViewModel.SelectedBlock);

    // ========== PROJECT MENU ==========

    private async void GenerateCode_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsProjectOpen || ViewModel.IsBusy)
            return;

        var invalid = ViewModel.Blocks.Count(b => !BlockValidator.ValidateBlock(b.Model).IsValid);
        if (invalid > 0 &&
            ThemedMessageBox.Show(this, Loc.F("{0} block(s) have validation problems (see Project → Validate All Blocks).\n\nGenerate anyway?", invalid),
                Loc.T("Generate Code"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = await ViewModel.GenerateCodeAsync();
            if (!result.Success)
            {
                ShowError(Loc.T("Code generation failed"), result);
                return;
            }

            var warnings = result.Warnings.Count > 0 ? $"\n\nNotes:\n• {string.Join("\n• ", result.Warnings)}" : "";
            var answer = ThemedMessageBox.Show(this,
                Loc.F("Code generated in:\n{0}\n\n{1} files written in {2} ms.\n", result.ProjectPath, result.FilesCreated, result.DurationMs) +
                Loc.F("{0}{1}\n\nOpen the project folder?", RunHint(ViewModel.CurrentProject?.Type), warnings),
                Loc.T("Generate Code"), MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes && result.ProjectPath != null)
                OpenInShell(result.ProjectPath);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("Code generation failed:\n\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RunApp_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsProjectOpen || ViewModel.IsBusy)
            return;

        try
        {
            var result = await ViewModel.RunAppAsync();
            if (result.Success && result.Target != null)
                OpenInShell(result.Target);
            else if (!result.Success)
                ThemedMessageBox.Show(this, result.Message, Loc.T("Run App"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not run the app:\n\n{0}", ex.Message), Loc.T("Run App"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopApp_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsAppRunning)
            ViewModel.StopApp();
    }

    /// <summary>Asks for an image and copies it into the project's assets folder.</summary>
    private string? PickImageFile()
    {
        var pattern = string.Join(";", ProjectAssets.ImageExtensions.Select(x => "*" + x));
        var dialog = new OpenFileDialog { Title = Loc.T("Choose an image"), Filter = $"Images ({pattern})|{pattern}" };
        if (dialog.ShowDialog(this) != true)
            return null;
        try
        {
            return ViewModel.ImportAsset(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not copy the image:\n\n{0}", ex.Message), "GrayCat Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private void Device_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LivePage == null) return;
        LivePage.SetDeviceWidth(DeviceBox.SelectedIndex switch { 1 => 820, 2 => 390, _ => null });
        if (DeviceBox.SelectedIndex != 0 && DesignModeButton.IsChecked != true)
            DesignModeButton.IsChecked = true;
    }

    private async void BuildProduction_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsProjectOpen || ViewModel.IsBusy)
            return;
        try
        {
            var result = await ViewModel.BuildForProductionAsync();
            if (!result.Success)
            {
                ThemedMessageBox.Show(this, result.Message, Loc.T("Build for Production"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (ThemedMessageBox.Show(this, result.Message + Loc.T("\n\nOpen the folder?"), Loc.T("Build for Production"),
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes && result.Target != null)
                OpenInShell(result.Target);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("The build failed:\n\n{0}", ex.Message), Loc.T("Build for Production"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is not ComboBoxItem { Tag: string lang } || lang == Loc.Instance.Lang)
            return;
        Loc.Instance.Set(lang);
        var settings = UiSettings.Load();
        settings.Language = lang;
        settings.Save();
    }

    private void EditorMode_Changed(object sender, RoutedEventArgs e)
    {
        if (LivePage == null || LayoutView == null)
            return; // still loading the XAML
        var design = DesignModeButton.IsChecked == true;
        LivePage.Visibility = design ? Visibility.Visible : Visibility.Collapsed;
        LayoutView.Visibility = design ? Visibility.Collapsed : Visibility.Visible;
        if (design)
            LivePage.RequestRefresh();
    }

    private void Pages_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsProjectOpen)
            new PagesWindow(ViewModel) { Owner = this }.ShowDialog();
    }

    private async void ExportZip_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ProjectFolder is not { } folder)
            return;

        var dialog = new SaveFileDialog
        {
            Title = Loc.T("Save the project as a ZIP file"),
            Filter = $"{Loc.T("ZIP archive")} (*.zip)|*.zip",
            FileName = $"{Path.GetFileName(folder)}.zip",
            InitialDirectory = Path.GetDirectoryName(folder)
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await ViewModel.ExportZipAsync(dialog.FileName);
            ThemedMessageBox.Show(this, Loc.F("Saved {0}\n\nInstalled packages (node_modules) and build output are not included; run npm install after unpacking.", dialog.FileName),
                Loc.T("Export ZIP"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not create the ZIP file:\n\n{0}", ex.Message), Loc.T("Export ZIP"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RunLog_Click(object sender, RoutedEventArgs e)
    {
        new RunLogWindow(ViewModel) { Owner = this }.Show();
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsProjectOpen)
            return;
        try
        {
            if (await ViewModel.WritePreviewAsync() is { } path)
                OpenInShell(path);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not open the preview:\n\n{0}", ex.Message), Loc.T("Preview"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsProjectOpen)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("Choose a folder for the static site (index.html, styles.css, script.js)")
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var folder = dialog.FolderName;
        if (File.Exists(Path.Combine(folder, "index.html")) &&
            ThemedMessageBox.Show(this, Loc.T("This folder already has an index.html. Replace it?"), Loc.T("Export"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (await ViewModel.ExportStaticSiteAsync(folder) is { } index &&
                ThemedMessageBox.Show(this, Loc.F("Static site exported to:\n{0}\n\nOpen it in the browser?", folder), Loc.T("Export"),
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                OpenInShell(index);
            }
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("Export failed:\n\n{0}", ex.Message), Loc.T("Export"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Arrange_Click(object sender, RoutedEventArgs e) => ViewModel.ArrangeBlocks();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentProject is not { } project)
            return;

        var dialog = new ProjectSettingsWindow(project, PickImageFile) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ViewModel.SetSetting(SettingKeys.Language, dialog.SiteLanguage);
        ViewModel.SetSetting(SettingKeys.Favicon, dialog.Favicon);
        ViewModel.SetSetting(SiteThemes.PrimaryKey, dialog.PrimaryColor);
        ViewModel.SetSetting(SiteThemes.AccentKey, dialog.AccentColor);
        ViewModel.SetSetting(SiteThemes.FontKey, dialog.FontName);
        if (ViewModel.UpdateSettings(dialog.ProjectName, dialog.Author, dialog.Description, dialog.ProjectType))
            ViewModel.StatusMessage = Loc.F("Project type changed to {0}. Use Generate Code to write the new project.", dialog.ProjectType);
    }

    private void ValidateAll_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsProjectOpen || ViewModel.Blocks.Count == 0)
        {
            ThemedMessageBox.Show(this, Loc.T("No blocks to validate."), Loc.T("Validation"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var problems = new List<string>();
        var valid = 0;
        foreach (var block in ViewModel.Blocks)
        {
            var (isValid, errors) = BlockValidator.ValidateBlock(block.Model);
            if (isValid)
            {
                valid++;
                continue;
            }
            var label = string.IsNullOrWhiteSpace(block.DisplayText) ? block.Type : $"{block.Type} \"{Truncate(block.DisplayText, 30)}\"";
            problems.Add($"\n{label}:");
            problems.AddRange(errors.Select(err => $"  • {err}"));
        }

        var message = Loc.F("✓ Valid blocks: {0}\n✗ Blocks with problems: {1}", valid, ViewModel.Blocks.Count - valid);
        if (problems.Count > 0)
            ThemedMessageBox.Show(this, message + "\n" + string.Join("\n", problems), Loc.T("Validation Results"), MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            ThemedMessageBox.Show(this, message + Loc.T("\n\nAll blocks are valid!"), Loc.T("Validation Results"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ========== HELP MENU ==========

    private void Documentation_Click(object sender, RoutedEventArgs e)
    {
        ThemedMessageBox.Show(this,
            Loc.T("1. File → New Project: pick a name, folder, project type and template.\n") +
            Loc.T("   The project is saved as one .graycat file (pages, blocks, theme and your images).\n") +
            Loc.T("   Code is written to the folder with the same name next to it, only when you generate, run or build.\n") +
            Loc.T("2. Drag blocks from the library onto the canvas (or click them).\n") +
            Loc.T("   Blocks appear on the page top-to-bottom, in canvas order.\n") +
            Loc.T("3. Select a block to edit it in the right-hand panel. Changes save automatically.\n") +
            Loc.T("   Use Browse… to add your own images: they are copied into the project's assets folder.\n") +
            Loc.T("   Drag the corner handle of a selected block to resize it.\n") +
            Loc.T("4. The Design tab shows the real page; click a block there to select it.\n") +
            Loc.T("   Layout lets you arrange blocks freely (top-to-bottom order = page order).\n") +
            Loc.T("5. Run App (F5) generates the project, installs packages and starts it in your browser.\n") +
            Loc.T("   While it runs, every change you make updates the running app. Stop with Shift+F5.\n") +
            Loc.T("   Generate Code (Ctrl+G) only writes the files.\n") +
            Loc.T("6. Pages: pick the page to edit in the toolbar; Pages… adds, renames and deletes pages.\n") +
            Loc.T("   Project → Settings and Theme sets colours and the font for the whole site.\n\n") +
            Loc.T("Project types:\n") +
            Loc.T("• Static website (HTML) - open index.html, no build needed\n") +
            Loc.T("• React website (Vite) - npm install, npm run dev\n") +
            Loc.T("• Next.js website - with a form API (npm install, npm run dev)\n") +
            Loc.T("• Website with a Node.js server - Express serves the site and stores form submissions\n") +
            Loc.T("• Mobile app (React Native, Expo) - npm install, npx expo start\n\n") +
            Loc.T("Shortcuts: Ctrl+N/O/S, Ctrl+Shift+S (save as), Ctrl+Z/Y (undo/redo), Ctrl+G, F5, Ctrl+C/X/V/D, Del, arrows (Shift = 10px)."),
            Loc.T("How to use GrayCat Studio"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        ThemedMessageBox.Show(this,
            Loc.F("GrayCat Studio {0}\n\n", AppConstants.AppVersion) +
            Loc.T("Visual website and app builder with block-based design.\n") +
            Loc.T("Generates static HTML, React, Next.js, Node.js and React Native projects.\n\n") +
            Loc.T("Built with WPF and .NET 8."),
            Loc.T("About GrayCat Studio"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ========== BLOCK PALETTE ==========

    private void Block_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _paletteDragStart = e.GetPosition(this);
    }

    private void Block_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button { Tag: string blockType } button)
            return;

        var delta = e.GetPosition(this) - _paletteDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (!ViewModel.IsProjectOpen)
            return;

        DragDrop.DoDragDrop(button, blockType, DragDropEffects.Copy);
    }

    private void Block_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string blockType })
            return;

        if (!ViewModel.IsProjectOpen)
        {
            ThemedMessageBox.Show(this, Loc.T("Create or open a project first."), "GrayCat Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Inserted right after the selected block, or at the end of the page when nothing is selected.
        ViewModel.AddBlockAfter(blockType, ViewModel.SelectedBlock);
    }

    // ========== CANVAS ==========

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.StringFormat) is not string blockType || sender is not Canvas canvas)
            return;

        var position = e.GetPosition(canvas);
        ViewModel.AddBlock(blockType, _showGrid ? Snap(position.X) : position.X, _showGrid ? Snap(position.Y) : position.Y);
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not Controls.DraggableBlock)
        {
            if (element is System.Windows.Controls.Primitives.Thumb)
                return; // resize handle: let the thumb handle the drag
            element = VisualTreeHelper.GetParent(element);
        }

        if (element is Controls.DraggableBlock block && block.DataContext is BlockViewModel vm)
        {
            _draggedBlock = vm;
            _dragStartPoint = e.GetPosition(block);
            _isDragging = true;
            (sender as Canvas)?.CaptureMouse();
            ViewModel.SelectedBlock = vm;
            Focus();
            e.Handled = true;
        }
        else
        {
            ViewModel.SelectedBlock = null;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _draggedBlock == null || sender is not Canvas canvas || !canvas.IsMouseCaptured)
            return;

        var current = e.GetPosition(canvas);
        var step = _showGrid ? GridSize : 10.0;
        var newX = Math.Round((current.X - _dragStartPoint.X) / step) * step;
        var newY = Math.Round((current.Y - _dragStartPoint.Y) / step) * step;

        newX = Math.Max(0, Math.Min(newX, Math.Max(0, canvas.ActualWidth - _draggedBlock.Width)));
        newY = Math.Max(0, newY);

        if (newX != _draggedBlock.PositionX) _draggedBlock.PositionX = newX;
        if (newY != _draggedBlock.PositionY) _draggedBlock.PositionY = newY;
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;
        _isDragging = false;
        (sender as Canvas)?.ReleaseMouseCapture();
        _draggedBlock = null;
        e.Handled = true;
    }

    // ========== CONTEXT MENU ==========

    private void EditBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: BlockViewModel block })
            ViewModel.SelectedBlock = block;
    }

    private void ValidateBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: BlockViewModel block })
            return;

        var (isValid, errors) = BlockValidator.ValidateBlock(block.Model);
        if (isValid)
            ThemedMessageBox.Show(this, Loc.F("✓ {0} block is valid!", Loc.Block(block.Type)), Loc.T("Validation"), MessageBoxButton.OK, MessageBoxImage.Information);
        else
            ThemedMessageBox.Show(this, Loc.F("Problems in the {0} block:\n\n• {1}", Loc.Block(block.Type), string.Join("\n• ", errors)), Loc.T("Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DuplicateBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: BlockViewModel block })
            ViewModel.DuplicateBlock(block);
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: BlockViewModel block })
            ConfirmDelete(block);
    }

    // ========== UI HELPERS ==========

    private void GridOverlay_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            _showGrid = checkBox.IsChecked == true;
            CanvasSurface.Background = _showGrid ? CreateGridBrush() : Brushes.White;
            ViewModel.StatusMessage = _showGrid ? Loc.T("Snapping blocks to a 20px grid") : Loc.T("Grid snapping off");
        }
    }

    private void ConfirmDelete(BlockViewModel? block)
    {
        if (block == null)
            return;
        if (ThemedMessageBox.Show(this, Loc.F("Delete the {0} block?", Loc.Block(block.Type)), Loc.T("Confirm Delete"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ViewModel.RemoveBlock(block);
        }
    }

    private void ShowError(string title, GenerationResult result)
    {
        var details = result.Errors.Count > 0 ? string.Join("\n", result.Errors.Select(err => "• " + err)) : result.Message;
        ThemedMessageBox.Show(this, $"{title}:\n\n{details}", "GrayCat Studio", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string RunHint(GrayCat.Shared.Models.ProjectType? type) => type switch
    {
        GrayCat.Shared.Models.ProjectType.StaticHtml => Loc.T("Open index.html in a browser."),
        GrayCat.Shared.Models.ProjectType.React => Loc.T("Run: npm install, then npm run dev"),
        GrayCat.Shared.Models.ProjectType.NextJs => Loc.T("Run: npm install, then npm run dev"),
        GrayCat.Shared.Models.ProjectType.NodeJsApi => Loc.T("Run: npm install, then npm start (http://localhost:3000)"),
        GrayCat.Shared.Models.ProjectType.ReactNative => Loc.T("Run: npm install, then npx expo start"),
        _ => ""
    };

    private static double Snap(double value) => Math.Round(value / GridSize) * GridSize;

    private static Brush CreateGridBrush()
    {
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(GridSize, 0)));
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, GridSize)));
        var drawing = new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(232, 236, 240)), 1), lines);
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, GridSize, GridSize),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, GridSize, GridSize),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        var background = new DrawingGroup();
        background.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, GridSize, GridSize))));
        background.Children.Add(drawing);
        brush.Drawing = background;
        brush.Freeze();
        return brush;
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";

    private void OpenInShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, Loc.F("Could not open {0}:\n{1}", path, ex.Message), "GrayCat Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ========== KEYBOARD SHORTCUTS ==========

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // Leave typing in text boxes alone.
        if (Keyboard.FocusedElement is TextBox or ComboBox { IsEditable: true })
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                SaveProject_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
            {
                SaveProjectAs_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            SaveProjectAs_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.N when ctrl: NewProject_Click(this, new RoutedEventArgs()); break;
            case Key.O when ctrl: OpenProject_Click(this, new RoutedEventArgs()); break;
            case Key.S when ctrl: SaveProject_Click(this, new RoutedEventArgs()); break;
            case Key.G when ctrl: GenerateCode_Click(this, new RoutedEventArgs()); break;
            case Key.Z when ctrl: ViewModel.Undo(); break;
            case Key.Y when ctrl: ViewModel.Redo(); break;
            case Key.Z when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift): ViewModel.Redo(); break;
            case Key.F5 when Keyboard.Modifiers == ModifierKeys.Shift: StopApp_Click(this, new RoutedEventArgs()); break;
            case Key.F5: RunApp_Click(this, new RoutedEventArgs()); break;
            case Key.C when ctrl && ViewModel.SelectedBlock != null: ViewModel.CopySelected(); break;
            case Key.X when ctrl && ViewModel.SelectedBlock != null: ViewModel.CutSelected(); break;
            case Key.V when ctrl && ViewModel.CanPaste: ViewModel.Paste(); break;
            case Key.D when ctrl && ViewModel.SelectedBlock != null: ViewModel.DuplicateBlock(ViewModel.SelectedBlock); break;
            case Key.Delete when ViewModel.SelectedBlock != null: ConfirmDelete(ViewModel.SelectedBlock); break;
            case Key.Escape: ViewModel.SelectedBlock = null; break;
            case Key.Up or Key.Down or Key.Left or Key.Right when ViewModel.SelectedBlock != null:
                MoveSelected(e.Key, Keyboard.Modifiers == ModifierKeys.Shift ? 10.0 : 1.0);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void MoveSelected(Key key, double step)
    {
        var block = ViewModel.SelectedBlock!;
        switch (key)
        {
            case Key.Up: block.PositionY -= step; break;
            case Key.Down: block.PositionY += step; break;
            case Key.Left: block.PositionX -= step; break;
            case Key.Right: block.PositionX += step; break;
        }
    }
}
