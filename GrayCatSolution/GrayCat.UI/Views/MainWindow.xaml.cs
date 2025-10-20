namespace GrayCat.UI.Views;

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GrayCat.Core.Validation;
using GrayCat.UI.ViewModels;
using Microsoft.Win32;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private BlockViewModel? _draggedBlock;
    private Point _dragStartPoint;
    private bool _isDragging;
    private bool _showGrid = false;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Show startup message
        var result = MessageBox.Show(
            "Welcome to GrayCat Studio v0.3.4!\n\n" +
            "✨ NEW in this version:\n" +
            "• 12+ new block types (Button, Hero, Card, etc.)\n" +
            "• Dynamic properties panel\n" +
            "• Real-time validation\n" +
            "• Enhanced UI/UX\n\n" +
            "Would you like to create a new project?",
            "GrayCat Studio v0.3.4",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
        {
            NewProject_Click(sender, e);
        }
    }

    // ========== FILE MENU ==========

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewProjectWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var request = dialog.ViewModel.CreateRequest();
            await ViewModel.CreateNewProjectAsync(request);
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "GrayCat Project (*.json)|*.json",
            Title = "Open Project"
        };
        if (dialog.ShowDialog() == true)
        {
            await ViewModel.LoadProjectAsync(dialog.FileName);
        }
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveProjectAsync();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to exit? Unsaved changes will be lost.",
            "Confirm Exit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }

    // ========== PROJECT MENU ==========

    private void GenerateCode_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsProjectOpen)
        {
            MessageBox.Show("Please open or create a project first.", "No Project",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBox.Show(
            "Code generation will be available in the next update.\n\n" +
            "This feature will generate production-ready React code from your blocks.",
            "Coming Soon",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ValidateAll_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsProjectOpen || ViewModel.Blocks.Count == 0)
        {
            MessageBox.Show("No blocks to validate.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var allErrors = new List<string>();
        var validCount = 0;
        var invalidCount = 0;

        foreach (var block in ViewModel.Blocks)
        {
            var (isValid, errors) = BlockValidator.ValidateBlock(block.Model);

            if (isValid)
            {
                validCount++;
            }
            else
            {
                invalidCount++;
                allErrors.Add($"\n{block.Type} Block (ID: {block.Id.Substring(0, 8)}...):");
                allErrors.AddRange(errors.Select(e => $"  • {e}"));
            }
        }

        var message = $"Validation Results:\n\n" +
                     $"✓ Valid blocks: {validCount}\n" +
                     $"✗ Invalid blocks: {invalidCount}\n";

        if (allErrors.Any())
        {
            message += $"\nErrors found:\n{string.Join("\n", allErrors)}";
            MessageBox.Show(message, "Validation Results",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            message += "\n🎉 All blocks are valid!";
            MessageBox.Show(message, "Validation Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Live preview will be available in the next update.\n\n" +
            "This feature will show your design in a browser window.",
            "Coming Soon",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Export functionality will be available in the next update.\n\n" +
            "You'll be able to export your project as:\n" +
            "• Static HTML/CSS/JS\n" +
            "• React components\n" +
            "• Next.js project\n" +
            "• React Native",
            "Coming Soon",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ========== HELP MENU ==========

    private void Documentation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/graycat-studio/docs",
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show("Documentation: https://github.com/graycat-studio/docs",
                "Documentation", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "GrayCat Studio v0.3.4\n\n" +
            "Visual website builder with block-based design.\n\n" +
            "✨ New in v0.3.4:\n" +
            "• 12+ new block types\n" +
            "• Dynamic properties panel\n" +
            "• Real-time validation\n" +
            "• Enhanced UI/UX\n\n" +
            "© 2025 GrayCat Studio\n" +
            "Built with WPF & .NET 8",
            "About GrayCat Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ========== CANVAS OPERATIONS ==========

    private void Block_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.Tag != null)
        {
            var blockType = button.Tag.ToString();
            DragDrop.DoDragDrop(button, blockType, DragDropEffects.Copy);
            e.Handled = true;
        }
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.StringFormat) is string blockType)
        {
            var canvas = sender as Canvas;
            var position = e.GetPosition(canvas);

            // Snap to grid if enabled
            if (_showGrid)
            {
                const double gridSize = 20.0;
                position.X = Math.Round(position.X / gridSize) * gridSize;
                position.Y = Math.Round(position.Y / gridSize) * gridSize;
            }

            ViewModel.AddBlock(blockType, position.X, position.Y);

            // Auto-select the new block
            if (ViewModel.Blocks.Any())
            {
                ViewModel.SelectedBlock = ViewModel.Blocks.Last();
            }
        }
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;

        // Find DraggableBlock in visual tree
        while (element != null && element is not Controls.DraggableBlock)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        if (element is Controls.DraggableBlock block)
        {
            _draggedBlock = block.DataContext as BlockViewModel;
            if (_draggedBlock != null)
            {
                _dragStartPoint = e.GetPosition(block);
                _isDragging = true;
                (sender as Canvas)?.CaptureMouse();
                e.Handled = true;
                ViewModel.SelectedBlock = _draggedBlock;
            }
        }
        else
        {
            // Clicked on empty canvas - deselect
            ViewModel.SelectedBlock = null;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && _draggedBlock != null && sender is Canvas canvas && canvas.IsMouseCaptured)
        {
            Point currentPosition = e.GetPosition(canvas);

            double newX = currentPosition.X - _dragStartPoint.X;
            double newY = currentPosition.Y - _dragStartPoint.Y;

            // Snap to grid
            double gridSize = _showGrid ? 20.0 : 10.0;
            newX = Math.Round(newX / gridSize) * gridSize;
            newY = Math.Round(newY / gridSize) * gridSize;

            // Keep within bounds
            newX = Math.Max(0, Math.Min(newX, canvas.ActualWidth - _draggedBlock.Width));
            newY = Math.Max(0, Math.Min(newY, canvas.ActualHeight - _draggedBlock.Height));

            _draggedBlock.PositionX = newX;
            _draggedBlock.PositionY = newY;
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            (sender as Canvas)?.ReleaseMouseCapture();
            _draggedBlock = null;
            e.Handled = true;
        }
    }

    // ========== CONTEXT MENU OPERATIONS ==========

    private void EditBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is BlockViewModel block)
        {
            ViewModel.SelectedBlock = block;
        }
    }

    private void ValidateBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is BlockViewModel block)
        {
            var (isValid, errors) = BlockValidator.ValidateBlock(block.Model);

            if (isValid)
            {
                MessageBox.Show(
                    $"✓ {block.Type} block is valid!",
                    "Validation Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                var errorMessage = string.Join("\n• ", errors);
                MessageBox.Show(
                    $"Validation errors in {block.Type} block:\n\n• {errorMessage}",
                    "Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void DuplicateBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is BlockViewModel block)
        {
            ViewModel.DuplicateBlock(block);

            // Select the duplicated block
            if (ViewModel.Blocks.Any())
            {
                ViewModel.SelectedBlock = ViewModel.Blocks.Last();
            }
        }
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is BlockViewModel block)
        {
            var result = MessageBox.Show(
                $"Delete {block.Type} block?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                ViewModel.RemoveBlock(block);
            }
        }
    }

    // ========== UI HELPERS ==========

    private void GridOverlay_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            _showGrid = checkBox.IsChecked == true;

            // TODO: Add visual grid overlay to canvas
            if (_showGrid)
            {
                MessageBox.Show(
                    "Grid snapping is now enabled.\n\n" +
                    "Blocks will snap to a 20px grid when moved.",
                    "Grid Enabled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }

    // ========== KEYBOARD SHORTCUTS ==========

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Ctrl+N - New Project
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            NewProject_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Ctrl+O - Open Project
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenProject_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Ctrl+S - Save Project
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveProject_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Delete - Delete selected block
        else if (e.Key == Key.Delete && ViewModel.SelectedBlock != null)
        {
            var result = MessageBox.Show(
                $"Delete {ViewModel.SelectedBlock.Type} block?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ViewModel.RemoveBlock(ViewModel.SelectedBlock);
            }
            e.Handled = true;
        }
        // Ctrl+D - Duplicate selected block
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control &&
                 ViewModel.SelectedBlock != null)
        {
            ViewModel.DuplicateBlock(ViewModel.SelectedBlock);
            e.Handled = true;
        }
        // Escape - Deselect
        else if (e.Key == Key.Escape)
        {
            ViewModel.SelectedBlock = null;
            e.Handled = true;
        }
        // Arrow keys - Move selected block
        else if (ViewModel.SelectedBlock != null &&
                 (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right))
        {
            var step = Keyboard.Modifiers == ModifierKeys.Shift ? 10.0 : 1.0;

            switch (e.Key)
            {
                case Key.Up:
                    ViewModel.SelectedBlock.PositionY = Math.Max(0, ViewModel.SelectedBlock.PositionY - step);
                    break;
                case Key.Down:
                    ViewModel.SelectedBlock.PositionY += step;
                    break;
                case Key.Left:
                    ViewModel.SelectedBlock.PositionX = Math.Max(0, ViewModel.SelectedBlock.PositionX - step);
                    break;
                case Key.Right:
                    ViewModel.SelectedBlock.PositionX += step;
                    break;
            }
            e.Handled = true;
        }
    }
}