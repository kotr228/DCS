using BlackCat.Core.Data;

using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace BlackCat.UI;

public partial class StartupWindow : Window
{
    private bool _isAdmin = false;
    private bool _dbReady = false;

    public StartupWindow()
    {
        InitializeComponent();
        Loaded += StartupWindow_Loaded;
    }

    private async void StartupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckSystemStatus();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = System.Windows.WindowState.Minimized;

    private async Task CheckSystemStatus()
    {
        try
        {
            StatusMessageText.Text = "Перевірка прав адміністратора...";

            _isAdmin = CheckAdminPrivileges();

            if (_isAdmin)
            {
                AdminStatusText.Text = "Запущено з правами адміністратора";
                AdminStatusIcon.Text = "✅";
                AdminStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x75));
            }
            else
            {
                AdminStatusText.Text = "Немає прав адміністратора — деякі функції обмежені";
                AdminStatusIcon.Text = "⚠";
                AdminStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
            }

            await Task.Delay(400);

            StatusMessageText.Text = "Перевірка бази даних...";

            _dbReady = await CheckDatabaseAsync();

            if (_dbReady)
            {
                DbStatusText.Text = "База даних ініціалізована та доступна";
                DbStatusIcon.Text = "✅";
                DbStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x75));
            }
            else
            {
                DbStatusText.Text = "Помилка ініціалізації бази даних";
                DbStatusIcon.Text = "❌";
                DbStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
            }

            LoadingProgress.IsIndeterminate = false;
            LoadingProgress.Visibility = Visibility.Collapsed;

            if (_dbReady)
            {
                StatusMessageText.Text = _isAdmin
                    ? "✅ Система готова до роботи!"
                    : "⚠ Обмежений режим — деякі функції недоступні";
                StatusMessageText.Foreground = _isAdmin
                    ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x75))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));

                ContinueButton.Visibility = Visibility.Visible;

                if (!_isAdmin)
                    RestartAdminButton.Visibility = Visibility.Visible;
            }
            else
            {
                StatusMessageText.Text = "❌ Критична помилка — база даних недоступна";
                StatusMessageText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));

                ShowError("Не вдалося ініціалізувати базу даних.\n\n" +
                          "Переконайтеся що:\n" +
                          "• Програма має права на запис у теку встановлення\n" +
                          "• Диск не переповнений\n" +
                          "• Файл blackcat.db не заблоковано іншим процесом");
            }
        }
        catch (Exception ex)
        {
            LoadingProgress.IsIndeterminate = false;
            LoadingProgress.Visibility = Visibility.Collapsed;
            StatusMessageText.Text = "❌ Помилка перевірки системи";
            ShowError($"Помилка ініціалізації:\n{ex.Message}");
        }
    }

    private bool CheckAdminPrivileges()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckDatabaseAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                using var db = new BlackCatDatabase();
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    private async void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule!.FileName,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(processInfo);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ShowError($"Не вдалося перезапустити від імені адміністратора:\n{ex.Message}");
        }

        await Task.CompletedTask;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        OpenMainWindow();
    }

    private void UIOnly_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "В обмеженому режимі деякі функції файрволу можуть бути недоступні.\n\n" +
            "Обмеження без прав адміністратора:\n" +
            "• Модифікація правил Windows Firewall\n" +
            "• Блокування процесів\n" +
            "• Перехоплення мережевого трафіку\n\n" +
            "Мережеві тунелі та моніторинг доступні.\n\nПродовжити?",
            "Обмежений режим",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
            OpenMainWindow();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void OpenMainWindow()
    {
        var mainWindow = new MainWindow();
        mainWindow.Show();
        this.Close();
    }

    private void ShowError(string message)
    {
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorMessageText.Text = message;
    }
}
