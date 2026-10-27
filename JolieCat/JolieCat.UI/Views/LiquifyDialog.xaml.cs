using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Views
{
    /// <summary>The Liquify dialog (see <see cref="LiquifyViewModel"/>). Turns mouse
    /// input on the preview into brush strokes; a timer keeps the stationary tools
    /// working while the button is held.</summary>
    public partial class LiquifyDialog : Window
    {
        private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };

        public LiquifyDialog(LiquifyViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            ToolWarp.IsChecked = true;
            _holdTimer.Tick += (_, _) => viewModel.Tick();
            Closed += (_, _) => _holdTimer.Stop();
        }

        private LiquifyViewModel ViewModel => (LiquifyViewModel)DataContext;

        private void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is LiquifyViewModel vm && sender is RadioButton { Tag: LiquifyTool tool })
                vm.Tool = tool;
        }

        private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(Surface);
            Surface.CaptureMouse();
            ViewModel.BeginStroke(p.X, p.Y);
            _holdTimer.Start();
            e.Handled = true;
        }

        private void Surface_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(Surface);
            MoveBrushCircle(p);
            if (e.LeftButton == MouseButtonState.Pressed) ViewModel.ContinueStroke(p.X, p.Y);
        }

        private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _holdTimer.Stop();
            ViewModel.EndStroke();
            Surface.ReleaseMouseCapture();
        }

        private void Surface_MouseEnter(object sender, MouseEventArgs e) => BrushCircle.Visibility = Visibility.Visible;

        private void Surface_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!Surface.IsMouseCaptured) BrushCircle.Visibility = Visibility.Collapsed;
        }

        private void MoveBrushCircle(Point p)
        {
            var d = ViewModel.BrushPreviewDiameter;
            Canvas.SetLeft(BrushCircle, p.X - d / 2);
            Canvas.SetTop(BrushCircle, p.Y - d / 2);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Handled || Keyboard.FocusedElement is TextBox) return;

            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            switch (key)
            {
                case Key.Z when ctrl: ViewModel.UndoStrokeCommand.Execute(null); break;
                case Key.OemOpenBrackets: ViewModel.ResizeBrush(bigger: false); break;
                case Key.OemCloseBrackets: ViewModel.ResizeBrush(bigger: true); break;
                case Key.W when !ctrl: ToolWarp.IsChecked = true; break;
                case Key.R when !ctrl: ToolReconstruct.IsChecked = true; break;
                case Key.S when !ctrl: ToolPucker.IsChecked = true; break;
                case Key.B when !ctrl: ToolBloat.IsChecked = true; break;
                case Key.C when alt: ToolTwirlCcw.IsChecked = true; break;
                case Key.C when !ctrl: ToolTwirlCw.IsChecked = true; break;
                default: return;
            }

            if (Mouse.Captured is null && Surface.IsMouseOver) MoveBrushCircle(Mouse.GetPosition(Surface));
            e.Handled = true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
