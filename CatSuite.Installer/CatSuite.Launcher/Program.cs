using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CatSuite.Launcher
{
    public class Program
    {
        // ========== КОНФІГУРАЦІЯ ==========

        // Пріоритет джерел (використовується перше доступне):
        // 1. Локальний файл (для тестування без інтернету)
        // 2. HTTP сервер (для локального тестування)
        // 3. Production сервер (для release)

        private const string LOCAL_MANIFEST_PATH = "manifest.json"; // Поруч з exe
        private const string HTTP_SERVER_URL = "http://localhost:8080/"; // Локальний сервер
        private const string PRODUCTION_SERVER_URL = "https://your-server.com/catsuite/manifest.json"; // TODO: Замінити на реальний

        // Google Drive (fallback для backward compatibility - буде видалено пізніше)
        private const string GOOGLE_DRIVE_FALLBACK = "https://drive.google.com/uc?export=download&id=1TL-Oeyu7ntiiBPXy6lWpzWOb-TsXQ34D";

        private static readonly string AppDirectory = AppContext.BaseDirectory;
        private static readonly string InstallerDllPath = Path.Combine(AppDirectory, "CatSuite.Installer.dll");
        private static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "CatSuite_Update");

        [STAThread]
        public static int Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return MainAsync(args).GetAwaiter().GetResult();
        }

        public static async Task<int> MainAsync(string[] args)
        {
            try
            {
                Console.WriteLine("=== CatSuite Launcher v2.0.0 ===");
                Console.WriteLine($"📂 Робочий каталог: {AppDirectory}");
                Console.WriteLine();

                // Завантажуємо маніфест (спробуємо всі джерела)
                var manifest = await LoadManifestFromBestSourceAsync();
                if (manifest == null)
                {
                    ShowError("Не вдалося завантажити маніфест з жодного джерела.\n\n" +
                             "Перевірено:\n" +
                             $"1. Локальний файл: {LOCAL_MANIFEST_PATH}\n" +
                             $"2. HTTP сервер: {HTTP_SERVER_URL}\n" +
                             $"3. Production: {PRODUCTION_SERVER_URL}");
                    return 1;
                }

                // Перевіряємо версію ядра
                var localVersion = GetLocalInstallerVersion();
                var remoteVersion = manifest.InstallerCore?.Version;

                Console.WriteLine($"🔍 Локальна версія ядра: {localVersion ?? "не знайдено"}");
                Console.WriteLine($"🌐 Версія ядра в маніфесті: {remoteVersion ?? "не вказано"}");

                // Чи потрібне оновлення?
                if (NeedsUpdate(localVersion, remoteVersion))
                {
                    Console.WriteLine("⚡ Потрібне самооновлення інсталятора!");
                    Console.WriteLine();

                    bool success = await UpdateInstallerCoreAsync(manifest.InstallerCore);

                    if (!success)
                    {
                        ShowError("Не вдалося оновити ядро інсталятора.");
                        return 1;
                    }

                    // UpdateInstallerCoreAsync запускає батник і завершує процес
                    return 0;
                }

                // Версії збігаються - запускаємо ядро
                Console.WriteLine("✅ Ядро актуальне. Запускаємо основний інсталятор...");
                Console.WriteLine();

                LaunchInstallerCore(manifest);

                return 0;
            }
            catch (Exception ex)
            {
                ShowError($"Критична помилка:\n{ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Завантажити маніфест з найкращого доступного джерела
        /// </summary>
        private static async Task<ManifestModel> LoadManifestFromBestSourceAsync()
        {
            // 1. Спробувати локальний файл (найшвидший, для тестування)
            Console.WriteLine("🔍 Перевірка локального файлу...");
            var manifest = await TryLoadManifestFromFileAsync(LOCAL_MANIFEST_PATH);
            if (manifest != null)
            {
                Console.WriteLine("✅ Маніфест завантажено з локального файлу");
                Console.WriteLine();
                return manifest;
            }

            // 2. Спробувати локальний HTTP сервер (для розробки/тестування)
            Console.WriteLine("🔍 Перевірка локального HTTP сервера...");
            manifest = await TryLoadManifestFromUrlAsync(HTTP_SERVER_URL);
            if (manifest != null)
            {
                Console.WriteLine("✅ Маніфест завантажено з локального сервера");
                Console.WriteLine();
                return manifest;
            }

            // 3. Спробувати production сервер
            Console.WriteLine("🔍 Підключення до production сервера...");
            manifest = await TryLoadManifestFromUrlAsync(PRODUCTION_SERVER_URL);
            if (manifest != null)
            {
                Console.WriteLine("✅ Маніфест завантажено з production сервера");
                Console.WriteLine();
                return manifest;
            }

            // 4. Fallback - Google Drive (для сумісності)
            Console.WriteLine("🔍 Спроба завантаження з Google Drive (fallback)...");
            manifest = await TryLoadManifestFromUrlAsync(GOOGLE_DRIVE_FALLBACK);
            if (manifest != null)
            {
                Console.WriteLine("⚠️ Маніфест завантажено з Google Drive (застарілий метод)");
                Console.WriteLine();
                return manifest;
            }

            return null;
        }

        /// <summary>
        /// Спробувати завантажити маніфест з локального файлу
        /// </summary>
        private static async Task<ManifestModel> TryLoadManifestFromFileAsync(string filePath)
        {
            try
            {
                string fullPath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(AppDirectory, filePath);

                if (!File.Exists(fullPath))
                    return null;

                var json = await File.ReadAllTextAsync(fullPath);
                return JsonSerializer.Deserialize<ManifestModel>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Помилка читання файлу: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Спробувати завантажити маніфест з URL
        /// </summary>
        private static async Task<ManifestModel> TryLoadManifestFromUrlAsync(string url)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var json = await client.GetStringAsync(url);
                return JsonSerializer.Deserialize<ManifestModel>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Помилка завантаження з {url}: {ex.Message}");
                return null;
            }
        }

        private static string GetLocalInstallerVersion()
        {
            if (!File.Exists(InstallerDllPath))
                return null;

            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(InstallerDllPath);
                return versionInfo.FileVersion;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка читання версії файлу: {ex.Message}");
                return null;
            }
        }

        private static bool NeedsUpdate(string localVersion, string remoteVersion)
        {
            if (string.IsNullOrEmpty(remoteVersion))
                return false;

            if (string.IsNullOrEmpty(localVersion))
                return true;

            try
            {
                var local = new Version(localVersion);
                var remote = new Version(remoteVersion);
                return remote > local;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Оновити ядро інсталятора (НОВИЙ СПРОЩЕНИЙ АЛГОРИТМ)
        /// Замість ZIP - прямі файли
        /// </summary>
        private static async Task<bool> UpdateInstallerCoreAsync(InstallerCoreInfo coreInfo)
        {
            if (coreInfo == null || string.IsNullOrWhiteSpace(coreInfo.Url))
            {
                Console.WriteLine("❌ Невалідна інформація про оновлення.");
                return false;
            }

            try
            {
                // Очистити temp директорію
                if (Directory.Exists(TempDirectory))
                    Directory.Delete(TempDirectory, true);
                Directory.CreateDirectory(TempDirectory);

                Console.WriteLine($"⬇️ Завантаження оновлення...");
                Console.WriteLine($"   URL: {coreInfo.Url}");
                Console.WriteLine();

                // НОВИЙ ПІДХІД: Завантажуємо DLL напряму (без ZIP)
                string tempDllPath = Path.Combine(TempDirectory, "CatSuite.Installer.dll");

                bool success = await DownloadFileWithRetryAsync(coreInfo.Url, tempDllPath);
                if (!success)
                {
                    Console.WriteLine("❌ Не вдалося завантажити файл оновлення.");
                    return false;
                }

                // Перевірка SHA256
                if (!string.IsNullOrWhiteSpace(coreInfo.Sha256))
                {
                    Console.WriteLine("🔐 Перевірка контрольної суми...");
                    if (!ValidateSha256(tempDllPath, coreInfo.Sha256))
                    {
                        Console.WriteLine("❌ Контрольна сума не співпадає!");
                        return false;
                    }
                    Console.WriteLine("✅ Контрольна сума підтверджена.");
                }

                // Створюємо батник для заміни файлу
                string targetDllPath = InstallerDllPath;
                string launcherExePath = Process.GetCurrentProcess().MainModule!.FileName;
                string batPath = Path.Combine(TempDirectory, "update.bat");

                string batContent = $@"@echo off
echo.
echo === CatSuite Updater v2.0 ===
echo Оновлення інсталятора...
echo.

timeout /t 2 /nobreak > nul

setlocal
set RETRY=10
:copy_loop
copy /Y ""{tempDllPath}"" ""{targetDllPath}""
if errorlevel 1 (
    set /a RETRY-=1
    if %RETRY% equ 0 goto copy_fail
    timeout /t 1 /nobreak > nul
    goto copy_loop
)
echo Оновлення успішне!

rem Очищаємо temp (асинхронно)
start """" cmd /c ""timeout /t 2 /nobreak > nul && rd /s /q ""{TempDirectory}""""

:copy_success
timeout /t 1 /nobreak > nul
start """" ""{launcherExePath}""
exit

:copy_fail
echo Не вдалося оновити файл. Файл може бути заблокований.
pause
start """" ""{launcherExePath}""
exit
";

                // Запис батника в OEM-866
                var oem866 = Encoding.GetEncoding(866);
                await File.WriteAllTextAsync(batPath, batContent, oem866);

                Console.WriteLine("🚀 Запуск оновлення...");
                Console.WriteLine("   (Лаунчер перезапуститься автоматично)");

                // Запускаємо батник і виходимо
                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                });

                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка оновлення: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Завантажити файл з ретраями
        /// </summary>
        private static async Task<bool> DownloadFileWithRetryAsync(string url, string targetPath, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

                    Console.WriteLine($"   Спроба {attempt}/{maxRetries}...");

                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;

                    await using var contentStream = await response.Content.ReadAsStreamAsync();
                    await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            var progress = (int)((totalRead * 100) / totalBytes.Value);
                            Console.Write($"\r   Прогрес: {progress}% ({totalRead / 1024} KB / {totalBytes / 1024} KB)");
                        }
                    }

                    Console.WriteLine();
                    Console.WriteLine($"   ✅ Завантажено: {totalRead / 1024 / 1024:F2} MB");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\r   ❌ Спроба {attempt} не вдалася: {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        Console.WriteLine($"   ⏳ Повтор через 2 секунди...");
                        await Task.Delay(2000);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Перевірка SHA256 хешу
        /// </summary>
        private static bool ValidateSha256(string filePath, string expectedHash)
        {
            if (string.IsNullOrEmpty(expectedHash))
                return true;

            try
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(filePath);

                byte[] hash = sha256.ComputeHash(stream);
                string actualHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                return actualHash.Equals(expectedHash.ToLowerInvariant());
            }
            catch
            {
                return false;
            }
        }

        private static void LaunchInstallerCore(ManifestModel manifest)
        {
            try
            {
                // Читаємо DLL в пам'ять і завантажуємо
                byte[] assemblyBytes = File.ReadAllBytes(InstallerDllPath);
                var assembly = Assembly.Load(assemblyBytes);

                var entryType = assembly.GetType("CatSuite.Installer.App");

                if (entryType == null)
                {
                    ShowError("Не знайдено точку входу в CatSuite.Installer.dll");
                    return;
                }

                // Передаємо маніфест як JSON
                string manifestJson = JsonSerializer.Serialize(manifest);

                // Створюємо STA потік
                var thread = new Thread(() =>
                {
                    try
                    {
                        var runMethod = entryType.GetMethod("Run", BindingFlags.Static | BindingFlags.Public);
                        if (runMethod == null)
                        {
                            ShowError("Не знайдено метод Run в ядрі інсталятора.");
                            return;
                        }
                        runMethod.Invoke(null, new object[] { manifestJson });
                    }
                    catch (Exception ex)
                    {
                        var innerEx = ex.InnerException ?? ex;
                        ShowError($"Помилка запуску ядра інсталятора:\n{innerEx.Message}\n\n{innerEx.StackTrace}");
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }
            catch (Exception ex)
            {
                ShowError($"Помилка запуску ядра інсталятора:\n{ex.Message}");
            }
        }

        private static void ShowError(string message)
        {
            Console.WriteLine($"❌ {message}");
            MessageBox.Show(message, "CatSuite Launcher - Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region Models

    public class ManifestModel
    {
        public InstallerCoreInfo InstallerCore { get; set; }
        public PackageInfo[] Packages { get; set; }
    }

    public class InstallerCoreInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
    }

    public class PackageInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
        public string[] DependsOn { get; set; }
        public bool IsVisible { get; set; } = true;
        public string Category { get; set; }
    }

    #endregion
}
