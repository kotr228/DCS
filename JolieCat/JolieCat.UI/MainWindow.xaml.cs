using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.Layers;

namespace JolieCat.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Maximized means "fill the screen's work area" - see FitMaximizedToWorkArea.
            SourceInitialized += (_, _) =>
                (System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle))?.AddHook(FitMaximizedToWorkArea);
            DataContext = new MainViewModel();
            InitializeLayersPanel();
            WatchTimelineMode();

            // After the window is up: offer crash recovery, start autosave.
            ContentRendered += (_, _) => ViewModel?.StartSession();
            Views.ShortcutEditor.ApplySaved(this, MainMenu);
            Closing += MainWindow_Closing;
        }

        // ----- Timeline under the canvas (Clipbar Animation projects) -----

        private double _timelineHeight = 300;
        private DocumentViewModel? _watchedDocument;

        /// <summary>Shows the timeline row (at the height it was last dragged to) while
        /// the active document is in Timeline mode, and collapses it otherwise.</summary>
        private void WatchTimelineMode()
        {
            if (ViewModel is not { } vm) return;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ActiveDocument)) Rewatch();
            };
            Rewatch();

            void Rewatch()
            {
                if (_watchedDocument is not null) _watchedDocument.PropertyChanged -= OnDocumentChanged;
                _watchedDocument = vm.ActiveDocument;
                if (_watchedDocument is not null) _watchedDocument.PropertyChanged += OnDocumentChanged;
                UpdateTimelineRow();
            }
        }

        private void OnDocumentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DocumentViewModel.IsTimelineMode) || e.PropertyName == nameof(DocumentViewModel.WorkspaceMode))
                UpdateTimelineRow();
        }

        private void UpdateTimelineRow()
        {
            var show = _watchedDocument?.IsTimelineMode == true;
            if (TimelineRow.ActualHeight > 40) _timelineHeight = TimelineRow.ActualHeight; // keep what the splitter set
            TimelineRow.Height = show ? new GridLength(_timelineHeight) : new GridLength(0);
            TimelineRow.MinHeight = show ? 120 : 0;
        }

        private bool _closeConfirmed;

        /// <summary>Closing asks about unsaved documents first. Saving is async, so the
        /// close is cancelled, the questions run, and the window closes again only if
        /// nothing was cancelled.</summary>
        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closeConfirmed || ViewModel is not { } vm) return;

            var prepare = vm.PrepareToExitAsync();
            if (prepare.IsCompleted)
            {
                // Nothing to ask about (or answered right away): just let this close go on.
                // Calling Close() here, while the window is already closing, would throw.
                e.Cancel = !prepare.Result;
                _closeConfirmed = !e.Cancel;
                return;
            }

            e.Cancel = true;
            if (!await prepare) return;

            _closeConfirmed = true;
            // A fresh close, after this one has finished cancelling.
            await Dispatcher.InvokeAsync(Close, System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Drives dragging and double-click-to-maximize for the custom title bar Border
        /// (MainWindow.xaml sets WindowStyle="None" with a zero-height WindowChrome
        /// CaptionHeight, so none of it is OS-recognized caption - this single handler is
        /// what makes it act like one). A single click starts <see cref="Window.DragMove"/>
        /// - safe to call unconditionally on a maximized window; WPF restores it to
        /// normal size first and continues the drag from the same relative pointer
        /// position, matching how every native title bar itself behaves. A double click
        /// instead toggles Maximized/Normal, so it never also nudges the window from
        /// whatever pixel the second click landed on.
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximizeRestore();
                return;
            }

            DragMove();
        }

        private MainViewModel? ViewModel => DataContext as MainViewModel;

        /// <summary>True while a text field has focus - single-key shortcuts (tool
        /// letters, Space, [ ]) must then type normally instead.</summary>
        private static bool IsTyping() =>
            Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox { IsEditable: true };

        /// <summary>Single-key shortcuts that can't be plain KeyBindings because they
        /// must not fire while typing: tool letters (B, E, V, M ...; Shift+letter
        /// cycles tools sharing a letter), [ and ] for brush size, Space to pan,
        /// Ctrl+T for Free Transform and F1 for the shortcut list.</summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel is not { } vm) return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;

            if (key == Key.F1)
            {
                ShowShortcuts();
                e.Handled = true;
                return;
            }

            if (key == Key.T && modifiers == ModifierKeys.Control)
            {
                vm.Toolbox.SelectTool(Shared.Enums.ToolType.FreeTransform);
                e.Handled = true;
                return;
            }

            if (IsTyping() || modifiers is ModifierKeys.Control or ModifierKeys.Alt) return;

            if (key == Key.Escape && vm.Toolbox.ActiveTool.Type == Shared.Enums.ToolType.CanvasRotate)
            {
                vm.Canvas.ResetViewRotation();
                e.Handled = true;
                return;
            }

            switch (key)
            {
                case Key.Space:
                    if (!e.IsRepeat) CanvasHost.SetSpacePan(true);
                    e.Handled = true;
                    return;

                case Key.OemOpenBrackets when !modifiers.HasFlag(ModifierKeys.Control):
                    vm.Canvas.AdjustBrushSize(-1);
                    e.Handled = true;
                    return;

                case Key.OemCloseBrackets when !modifiers.HasFlag(ModifierKeys.Control):
                    vm.Canvas.AdjustBrushSize(+1);
                    e.Handled = true;
                    return;

                case Key.X when modifiers == ModifierKeys.None:
                    vm.Canvas.SwapColors();
                    e.Handled = true;
                    return;

                case Key.D when modifiers == ModifierKeys.None:
                    vm.Canvas.ResetColors();
                    e.Handled = true;
                    return;

                case Key.Q when modifiers == ModifierKeys.None:
                    vm.Canvas.ToggleQuickMask();
                    e.Handled = true;
                    return;
            }

            // Letter: that tool group's last-used tool. Shift+letter: next tool in the group.
            if (modifiers is ModifierKeys.None or ModifierKeys.Shift && key is >= Key.A and <= Key.Z && !e.IsRepeat)
            {
                if (vm.Toolbox.SelectToolByShortcut(key.ToString(), cycle: modifiers == ModifierKeys.Shift))
                    e.Handled = true;
            }
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) CanvasHost.SetSpacePan(false);
        }

        /// <summary>Space released while another window had focus never reaches us -
        /// disarm panning so the next click doesn't pan unexpectedly.</summary>
        private void Window_Deactivated(object? sender, System.EventArgs e) => CanvasHost.SetSpacePan(false);

        private void ExitMenu_Click(object sender, RoutedEventArgs e) => Close();

        private void FreeTransformMenu_Click(object sender, RoutedEventArgs e) =>
            ViewModel?.Toolbox.SelectTool(Shared.Enums.ToolType.FreeTransform);

        private void ShortcutsMenu_Click(object sender, RoutedEventArgs e) => ShowShortcuts();

        private void EditShortcutsMenu_Click(object sender, RoutedEventArgs e) => Views.ShortcutEditor.Show(this, MainMenu);

        /// <summary>Help &gt; About JolieCat: the version and where the user guide is.</summary>
        private void AboutMenu_Click(object sender, RoutedEventArgs e)
        {
            var nl = System.Environment.NewLine;
            JolieCat.UI.MessageBox.Show(this,
                $"JolieCat {AppVersion}{nl}{nl}" +
                Localization.Loc.T("A raster editor with layers, filters, frame and keyframe animation, and sprite sheets.") + nl + nl +
                Localization.Loc.T("Interface language: the switcher on the menu bar, or Edit > Preferences.") + nl + nl +
                "© 2026 kotr228",
                "About JolieCat", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>The release number, e.g. "4.0.0" (set in Directory.Build.props).</summary>
        public static string AppVersion =>
            typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                is [System.Reflection.AssemblyInformationalVersionAttribute { InformationalVersion: { Length: > 0 } v }, ..]
                ? v.Split('+')[0]
                : typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";

        /// <summary>The Layers panel's adjustment button drops down its menu on a plain click.</summary>
        private void AdjustmentMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { ContextMenu: { } menu } button) return;

            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }

        /// <summary>Double-clicking an adjustment layer's thumbnail opens its settings.</summary>
        private void AdjustmentThumbnail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || ViewModel is not { } vm) return;
            if (sender is not FrameworkElement { DataContext: LayerViewModel layer }) return;

            vm.Layers.ActiveLayer = layer;
            vm.EditAdjustmentLayerCommand.Execute(null);
            e.Handled = true;
        }

        /// <summary>Double-clicking a text layer's "T" thumbnail re-opens the text.</summary>
        private void TextThumbnail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || ViewModel is not { } vm) return;
            if (sender is not FrameworkElement { DataContext: LayerViewModel layer } || !layer.IsText) return;

            vm.Toolbox.SelectTool(layer.Model.Text!.Vertical ? Shared.Enums.ToolType.TextVertical : Shared.Enums.ToolType.TextHorizontal);
            vm.Canvas.EditTextLayer(layer.Model);
            e.Handled = true;
        }

        /// <summary>Ctrl+click on a layer thumbnail selects that layer's pixels, like
        /// Photoshop (Ctrl+Shift adds to the selection, Ctrl+Alt subtracts).</summary>
        private void LayerThumbnail_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is null || sender is not FrameworkElement { DataContext: LayerViewModel row }) return;

            // Double-click a fill layer's thumbnail: its settings.
            if (e.ClickCount == 2 && row.Model.IsFill && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                ViewModel.Layers.ActiveLayer = row;
                ViewModel.EditLayerContentCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            var mode = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? Core.Documents.SelectionCombine.Add
                : (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? Core.Documents.SelectionCombine.Subtract
                : Core.Documents.SelectionCombine.New;
            ViewModel.Canvas.SelectLayerTransparency(row.Model, mode);
            e.Handled = true;
        }

        // ----- Navigator panel: click or drag to move the view -----

        private void Navigator_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is null) return;
            NavigatorSurface.CaptureMouse();
            var p = e.GetPosition(NavigatorSurface);
            ViewModel.Panels.NavigateTo(p.X, p.Y);
            e.Handled = true;
        }

        private void Navigator_MouseMove(object sender, MouseEventArgs e)
        {
            if (ViewModel is null || !NavigatorSurface.IsMouseCaptured) return;
            var p = e.GetPosition(NavigatorSurface);
            ViewModel.Panels.NavigateTo(p.X, p.Y);
        }

        private void Navigator_MouseUp(object sender, MouseButtonEventArgs e) => NavigatorSurface.ReleaseMouseCapture();

        /// <summary>Double-clicking a layer's "SF" badge opens its Smart Filters.</summary>
        private void SmartFilterBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || ViewModel is null) return;
            if (sender is FrameworkElement { DataContext: LayerViewModel row }) ViewModel.Layers.ActiveLayer = row;
            ViewModel.OpenSmartFiltersCommand.Execute(null);
            e.Handled = true;
        }

        /// <summary>Saves the actions after an action is renamed.</summary>
        private void ActionName_LostFocus(object sender, RoutedEventArgs e) => ViewModel?.ActionsPanel.Save();

        /// <summary>Double-clicking a layer's "fx" badge opens its Layer Style.</summary>
        private void LayerStyleBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || ViewModel is not { } vm) return;
            if (sender is not FrameworkElement { DataContext: LayerViewModel layer }) return;

            vm.Layers.ActiveLayer = layer;
            vm.EditLayerStyleCommand.Execute(null);
            e.Handled = true;
        }

        private void ShowShortcuts() => MessageBox.Show(this,
            "TOOLS\n" +
            "V Move · M Marquee · L Lasso · W Magic Wand · C Crop · I Eyedropper\n" +
            "B Brush · N Pencil · E Eraser · G Gradient / Paint Bucket\n" +
            "S Clone Stamp (Alt+click = source) · J Spot Healing / Healing Brush / Patch / Content-Aware Move · O Dodge / Burn / Sponge · R Blur / Sharpen\n" +
            "P Pen · A Path Selection · T Text (click existing text to edit it) · U Shape · H Hand · Z Zoom (Alt+click = out)\n" +
            "Shift+letter cycles through tools that share a letter.\n\n" +
            "PAINTING\n" +
            "[ / ]  smaller / bigger brush · pen pressure: see brush options\n" +
            "X swap foreground / background · D default colors (black / white) · Q Quick Mask\n" +
            "Gradient: drag from start to end, Shift = 45° steps\n\n" +
            "VIEW\n" +
            "Space+drag or middle mouse  pan · wheel  zoom\n" +
            "Ctrl+0 fit · Ctrl+1 100% · Ctrl++ / Ctrl+- zoom · Ctrl+2 all channels · Ctrl+3/4/5 Red/Green/Blue only\n\n" +
            "SELECT\n" +
            "Ctrl+A all · Ctrl+D deselect · Ctrl+Shift+D reselect · Ctrl+Shift+I inverse · Shift+F6 feather\n" +
            "Hold Shift = add, Alt = subtract, Shift+Alt = intersect when starting a selection\n" +
            "Delete clear · Alt+Backspace fill foreground · Ctrl+Backspace fill background · Shift+F5 Content-Aware Fill\n" +
            "Ctrl+X cut · Shift+Ctrl+C copy merged · Shift+Ctrl+V paste in place · Shift+Ctrl+F fade · Ctrl+click thumbnail = select layer pixels\n\n" +
            "LAYERS\n" +
            "Ctrl+Shift+N new · Ctrl+J duplicate · Ctrl+E merge down · Ctrl+[ / Ctrl+] order · Shift+Ctrl+[ / ] to back / front · fx layer style\n" +
            "Ctrl+G group · Ctrl+Shift+G ungroup · Ctrl/Shift+click selects several · drag rows to reorder\n" +
            "Ctrl+Alt+G clipping mask (clip to the layer below)\n" +
            "HISTORY tab: click any step to go back to it · ACTIONS tab: + record, ■ stop, ▶ play on this document\n\n" +
            "IMAGE\n" +
            "Ctrl+L Levels · Ctrl+M Curves · Ctrl+U Hue/Saturation · Ctrl+I Invert · Ctrl+Shift+U Desaturate\n" +
            "Ctrl+Alt+Shift+L Auto Contrast · Ctrl+Shift+B Auto Color\n" +
            "Ctrl+T Free Transform (Enter apply, Esc cancel) · Ctrl+Alt+I Image Size · Ctrl+Alt+C Canvas Size\n\n" +
            "FILTER\n" +
            "Shift+Ctrl+X Liquify (W warp · R reconstruct · S pucker · B bloat · C twirl · [ ] brush) · Shift+Ctrl+A Camera Raw\n\n" +
            "FILE\n" +
            "Ctrl+N new · Ctrl+O open (.jolie, .psd, images) · Ctrl+S save · Ctrl+Shift+S save as · Ctrl+Shift+E export · Ctrl+W close tab\n\n" +
            "VIEW ROTATION\n" +
            "R Rotate View: drag to turn, Shift = 15° steps, Esc resets",
            "Keyboard Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => SystemCommands.MinimizeWindow(this);

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
            => ToggleMaximizeRestore();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => SystemCommands.CloseWindow(this);

        // ----- Maximizing a window with its own title bar -----

        private const int WmGetMinMaxInfo = 0x0024;
        private const int MonitorDefaultToNearest = 2;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr MonitorFromWindow(System.IntPtr hwnd, int flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(System.IntPtr monitor, ref MonitorInfo info);

        /// <summary>
        /// With our own title bar (WindowStyle None + WindowChrome), Windows maximizes the
        /// window to the whole monitor plus its invisible frame: the edges hang off the
        /// screen and the bottom goes under the taskbar. This tells Windows the maximized
        /// size is exactly the work area of the monitor the window is on.
        /// </summary>
        private System.IntPtr FitMaximizedToWorkArea(System.IntPtr hwnd, int msg, System.IntPtr wParam, System.IntPtr lParam, ref bool handled)
        {
            if (msg != WmGetMinMaxInfo) return System.IntPtr.Zero;
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
            if (monitor == System.IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return System.IntPtr.Zero;
            var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
            mmi.MaxPosition.X = info.Work.Left - info.Monitor.Left;
            mmi.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
            mmi.MaxSize.X = info.Work.Right - info.Work.Left;
            mmi.MaxSize.Y = info.Work.Bottom - info.Work.Top;
            // Keep the smallest size the window allows (MinWidth/MinHeight, in device pixels).
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            mmi.MinTrackSize.X = (int)(MinWidth * dpi.DpiScaleX);
            mmi.MinTrackSize.Y = (int)(MinHeight * dpi.DpiScaleY);
            System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
            return System.IntPtr.Zero;
        }

        private void ToggleMaximizeRestore()
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }

        /// <summary>Double-clicking a layer's name label swaps in its inline rename textbox.</summary>
        private void LayerName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is FrameworkElement { DataContext: LayerViewModel layer })
                layer.IsEditingName = true;
        }

        /// <summary>Focuses and selects the rename textbox's text as soon as it appears,
        /// so the user can start typing (or Ctrl+A/retype) immediately.</summary>
        private void LayerNameEditBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            textBox.Focus();
            textBox.SelectAll();
        }

        /// <summary>
        /// Enter commits the new name and exits edit mode; Escape exits without
        /// committing. Enter forces the textbox's Text binding (TextBox's default
        /// UpdateSourceTrigger is LostFocus, so it wouldn't otherwise fire here) to commit
        /// early, since we're not actually moving focus away. Escape instead resets the
        /// textbox's Text back to the layer's actual name before hiding it - collapsing a
        /// focused control's Visibility (which IsEditingName's own binding does next)
        /// makes WPF move focus away on its own, which would trigger that same
        /// LostFocus-triggered binding to commit whatever was typed anyway; feeding it
        /// back its own current value first makes that commit a no-op instead of a silent
        /// save of the discarded edit.
        /// </summary>
        private void LayerNameEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Escape) return;
            if (sender is not TextBox { DataContext: LayerViewModel layer } textBox) return;

            if (e.Key == Key.Enter)
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            else
                textBox.Text = layer.Name;

            layer.IsEditingName = false;
            e.Handled = true;
        }

        /// <summary>Clicking away from the rename textbox commits it (TextBox's own
        /// LostFocus-triggered binding already did that before this handler runs) and
        /// exits edit mode.</summary>
        private void LayerNameEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: LayerViewModel layer })
                layer.IsEditingName = false;
        }
    }
}
