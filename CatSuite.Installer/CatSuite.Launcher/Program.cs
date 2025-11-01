using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;

namespace CatSuite.Launcher
{
    public class Program
    {
        private const string MANIFEST_URL = "https://drive.google.com/uc?export=download&id=1TL-Oeyu7ntiiBPXy6lWpzWOb-TsXQ34D";
        private static readonly string AppDirectory = AppContext.BaseDirectory;
        private static readonly string InstallerDllPath = Path.Combine(AppDirectory, "CatSuite.Installer.dll");
        private static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "CatSuite_Update");

        [STAThread]
        public static int Main(string[] args)
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }

        public static async Task<int> MainAsync(string[] args)
        {
            try
            {
                Console.WriteLine("=== CatSuite Launcher v1.0 ===");
                Console.WriteLine($"📂 Робочий каталог: {AppDirectory}");

                // Завантажуємо маніфест
                var manifest = await LoadManifestAsync();
                if (manifest == null)
                {
                    ShowError("Не вдалося завантажити маніфест. Перевірте з'єднання з Інтернетом.");
                    return 1;
                }

                // Перевіряємо версію ядра
                var localVersion = GetLocalInstallerVersion();
                var remoteVersion = manifest.InstallerCore?.Version;

                Console.WriteLine($"🔍 Локальна версія ядра: {localVersion ?? "не знайдено"}");
                Console.WriteLine($"🌐 Версія ядра в хмарі: {remoteVersion ?? "не вказано"}");

                // Чи потрібне оновлення?
                if (NeedsUpdate(localVersion, remoteVersion))
                {
                    Console.WriteLine("⚡ Потрібне самооновлення інсталятора!");

                    bool success = await UpdateInstallerCoreAsync(manifest.InstallerCore);

                    if (!success)
                    {
                        ShowError("Не вдалося оновити ядро інсталятора.");
                        return 1;
                    }

                    // Перезапускаємо себе
                    Console.WriteLine("🔄 Перезапуск...");
                    RestartLauncher();
                    return 0;
                }

                // Версії збігаються - завантажуємо ядро
                Console.WriteLine("✅ Ядро актуальне. Запускаємо основний інсталятор...");
                LaunchInstallerCore(manifest);

                return 0;
            }
            catch (Exception ex)
            {
                ShowError($"Критична помилка:\n{ex.Message}");
                return 1;
            }
        }

        private static async Task<ManifestModel> LoadManifestAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var json = await client.GetStringAsync(MANIFEST_URL);
                return JsonSerializer.Deserialize<ManifestModel>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка завантаження маніфесту: {ex.Message}");
                return null;
            }
        }

        private static string GetLocalInstallerVersion()
        {
            if (!File.Exists(InstallerDllPath))
                return null;

            try
            {
                var assembly = Assembly.LoadFrom(InstallerDllPath);
                var version = assembly.GetName().Version;
                return version?.ToString();
            }
            catch
            {
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

        private static async Task<bool> UpdateInstallerCoreAsync(InstallerCoreInfo coreInfo)
        {
            if (coreInfo == null || string.IsNullOrEmpty(coreInfo.Url))
            {
                Console.WriteLine("❌ Невалідна інформація про оновлення.");
                return false;
            }

            try
            {
                // Створюємо тимчасову папку
                if (Directory.Exists(TempDirectory))
                    Directory.Delete(TempDirectory, true);
                Directory.CreateDirectory(TempDirectory);

                Console.WriteLine($"⬇️ Завантаження оновлення з {coreInfo.Url}...");

                // Завантажуємо ZIP
                string zipPath = Path.Combine(TempDirectory, "installer_core.zip");
                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(coreInfo.Url);
                    await File.WriteAllBytesAsync(zipPath, data);
                }

                Console.WriteLine($"📦 Розпакування...");
                ZipFile.ExtractToDirectory(zipPath, TempDirectory);

                // Створюємо BAT-скрипт для оновлення
                string batPath = Path.Combine(TempDirectory, "update.bat");
                string batContent = $@"@echo off
timeout /t 2 /nobreak > nul
echo Оновлення CatSuite Installer Core...
copy /Y ""{Path.Combine(TempDirectory, "CatSuite.Installer.dll")}"" ""{InstallerDllPath}""
if exist ""{Path.Combine(TempDirectory, "CatSuite.Installer.pdb")}"" (
    copy /Y ""{Path.Combine(TempDirectory, "CatSuite.Installer.pdb")}"" ""{Path.Combine(AppDirectory, "CatSuite.Installer.pdb")}""
)
start """" ""{Process.GetCurrentProcess().MainModule.FileName}""
rd /s /q ""{TempDirectory}""
exit
";
                await File.WriteAllTextAsync(batPath, batContent);

                // Запускаємо BAT і закриваємо себе
                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка оновлення: {ex.Message}");
                return false;
            }
        }

        private static void RestartLauncher()
        {
            var exePath = Process.GetCurrentProcess().MainModule.FileName;
            Process.Start(exePath);
        }

        private static void LaunchInstallerCore(ManifestModel manifest)
        {
            try
            {
                // Завантажуємо DLL у пам'ять
                var assembly = Assembly.LoadFrom(InstallerDllPath);
                var entryType = assembly.GetType("CatSuite.Installer.App");

                if (entryType == null)
                {
                    ShowError("Не знайдено точку входу в CatSuite.Installer.dll");
                    return;
                }

                // Передаємо маніфест як JSON
                string manifestJson = JsonSerializer.Serialize(manifest);

                // Створюємо новий потік з [STAThread]
                var thread = new Thread(() =>
                {
                    var runMethod = entryType.GetMethod("Run", BindingFlags.Static | BindingFlags.Public);
                    if (runMethod == null)
                    {
                        ShowError("Не знайдено метод Run в ядрі інсталятора.");
                        return;
                    }

                    runMethod.Invoke(null, new object[] { manifestJson });
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
    }

    #endregion
}