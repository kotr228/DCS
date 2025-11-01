using System;
using System.Text.Json;
using System.Windows;
using CatSuite.Installer.Models;
using CatSuite.Installer.Services;
using CatSuite.Installer.Views;

namespace CatSuite.Installer
{
    public class App
    {
        [STAThread]
        public static void Run(string manifestJson)
        {
            try
            {
                Console.WriteLine("=== CatSuite Installer Core v1.0 ===");

                // Парсимо маніфест
                var manifest = JsonSerializer.Deserialize<ManifestModel>(manifestJson);
                if (manifest == null)
                {
                    MessageBox.Show("Помилка парсингу маніфесту.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Ініціалізуємо БД
                var dbManager = new DatabaseManager();
                dbManager.InitializeDatabase();

                // Створюємо сервіси
                var packageService = new PackageService(dbManager, manifest);
                var installationService = new InstallationService(dbManager);

                // Запускаємо WPF
                var app = new Application();
                var mainWindow = new MainWindow(packageService, installationService);

                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Критична помилка: {ex.Message}");
                MessageBox.Show($"Критична помилка:\n{ex.Message}\n\n{ex.StackTrace}",
                    "CatSuite Installer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}