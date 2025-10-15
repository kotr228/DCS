using GrayCat.Shared.Models;
using GrayCat.UI.ViewModels;
using GrayCat.UI.Windows;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace GrayCat.UI
{
    public partial class MainWindow : System.Windows.Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        // --- Змінні для перетягування блоків ---
        private BlockViewModel? _draggedBlock;
        private System.Drawing.Point _mouseOffset; // Зміщення курсора відносно верхнього лівого кута блока

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        // --- Меню Файл ---

        private async void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewProjectWindow { Owner = this };
            if (dialog.ShowDialog() == true && dialog.GenerationRequest != null)
            {
                await ViewModel.CreateNewProjectAsync(dialog.GenerationRequest);
            }
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "GrayCat Project (*.json)|*.json",
                Title = "Open Project"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                ViewModel.LoadProject(openFileDialog.FileName);
            }
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveProject();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }


        // --- Логіка Drag & Drop (Створення нового блока) ---

        private void Block_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Починаємо перетягування для СТВОРЕННЯ нового блока
            if (e.LeftButton == MouseButtonState.Pressed && sender is System.Windows.Controls.Button button)
            {
                DragDrop.DoDragDrop(button, button.Tag.ToString(), System.Windows.DragDropEffects.Copy);
            }
        }

        private void Canvas_Drop(object sender, System.Windows.DragEventArgs e)
        {
            // Завершуємо перетягування, створюючи блок на Canvas
            if (e.Data.GetData(System.Windows.DataFormats.StringFormat) is string blockType)
            {
                System.Drawing.Point dropPosition = e.GetPosition(BlocksContainer);
                ViewModel.AddBlock(blockType, dropPosition.X, dropPosition.Y);
            }
        }

        // --- Нова, стабільна логіка для ПЕРЕМІЩЕННЯ існуючих блоків ---

        private void BlocksContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Знаходимо блок, на який натиснули
            var element = e.OriginalSource as UIElement;
            Controls.DraggableBlock? blockControl = null;

            while (element != null)
            {
                if (element is Controls.DraggableBlock)
                {
                    blockControl = (Controls.DraggableBlock)element;
                    break;
                }
                element = VisualTreeHelper.GetParent(element) as UIElement;
            }

            if (blockControl != null)
            {
                _draggedBlock = blockControl.DataContext as BlockViewModel;
                if (_draggedBlock != null)
                {
                    // Запам'ятовуємо, де всередині блока був курсор
                    _mouseOffset = e.GetPosition(blockControl);
                    // "Захоплюємо" мишу, щоб відстежувати її рух
                    BlocksContainer.CaptureMouse();
                    e.Handled = true;
                }
            }
        }

        private void BlocksContainer_MouseMove(object sender, MouseEventArgs e)
        {
            // Якщо ми перетягуємо блок і миша захоплена
            if (_draggedBlock != null && BlocksContainer.IsMouseCaptured)
            {
                Point currentPosition = e.GetPosition(BlocksContainer);

                // Розраховуємо нову позицію верхнього лівого кута блока
                double newX = currentPosition.X - _mouseOffset.X;
                double newY = currentPosition.Y - _mouseOffset.Y;

                // Прив'язка до сітки 10x10
                double snap = 10.0;
                newX = Math.Round(newX / snap) * snap;
                newY = Math.Round(newY / snap) * snap;

                // Оновлюємо позицію у ViewModel
                _draggedBlock.PositionX = newX;
                _draggedBlock.PositionY = newY;
            }
        }

        private void BlocksContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Якщо ми відпустили кнопку миші, завершуємо перетягування
            if (_draggedBlock != null)
            {
                BlocksContainer.ReleaseMouseCapture();
                _draggedBlock = null;
                e.Handled = true;
                // На цьому етапі ViewModel автоматично збереже зміни
            }
        }

        // --- Контекстне меню блока ---

        private void DeleteBlock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is BlockViewModel blockToDelete)
            {
                ViewModel.RemoveBlock(blockToDelete);
            }
        }
    }
}