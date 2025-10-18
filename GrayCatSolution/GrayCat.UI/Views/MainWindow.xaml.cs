namespace GrayCat.UI.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GrayCat.UI.ViewModels;
using Microsoft.Win32;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private BlockViewModel? _draggedBlock;
    private Point _dragStartPoint;
    private bool _isDragging;

    public MainWindow()
    {
        InitializeComponent();
    }

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
        Application.Current.Shutdown();
    }

    private void GenerateCode_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Coming soon!");
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Coming soon!");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Coming soon!");
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("GrayCat Studio v0.3.2");
    }

    private void Block_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.Tag != null)
        {
            DragDrop.DoDragDrop(button, button.Tag.ToString(), DragDropEffects.Copy);
            e.Handled = true;
        }
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.StringFormat) is string blockType)
        {
            var canvas = sender as Canvas;
            var position = e.GetPosition(canvas);
            ViewModel.AddBlock(blockType, position.X, position.Y);
        }
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;

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

            const double gridSize = 10.0;
            newX = Math.Round(newX / gridSize) * gridSize;
            newY = Math.Round(newY / gridSize) * gridSize;

            newX = Math.Max(0, newX);
            newY = Math.Max(0, newY);

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

    private void EditBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is BlockViewModel block)
        {
            ViewModel.SelectedBlock = block;
        }
    }

    private void DuplicateBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is BlockViewModel block)
        {
            ViewModel.DuplicateBlock(block);
        }
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is BlockViewModel block)
        {
            var result = MessageBox.Show($"Delete {block.Type}?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                ViewModel.RemoveBlock(block);
            }
        }
    }
}