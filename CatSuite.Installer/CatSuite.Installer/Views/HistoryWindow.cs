using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CatSuite.Installer.Models;
using CatSuite.Installer.Services;

namespace CatSuite.Installer.Views
{
    public partial class HistoryWindow : Window
    {
        private readonly DatabaseManager _dbManager;

        public HistoryWindow(DatabaseManager dbManager)
        {
            InitializeComponent();
            _dbManager = dbManager;
            LoadHistory();
        }

        private void InitializeComponent()
        {
            this.Title = "Історія встановлень";
            this.Width = 800;
            this.Height = 600;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Створюємо простий Grid з TextBlock
            var grid = new Grid();
            var textBlock = new TextBlock
            {
                Text = "Історія встановлень буде тут.\n\nФункція буде додана в наступній версії.",
                FontSize = 16,
                TextAlignment = System.Windows.TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            grid.Children.Add(textBlock);
            this.Content = grid;
        }

        private void LoadHistory()
        {
            try
            {
                var logs = _dbManager.GetInstallationLogs(null, 50);
                // TODO: Відобразити логи в UI
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка завантаження історії:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}