using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Text;

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
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            return MainAsync(args).GetAwaiter().GetResult();
        }

        public static async Task<int> MainAsync(string[] args)
        {
            try
            {
                Console.WriteLine("=== CatSuite Launcher v1.2.0 ===");
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

                    // НЕ робити RestartLauncher тут.
                    // UpdateInstallerCoreAsync уже запустив bat і завершив процес.
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
                // Цей метод читає метадані версії з файлу, 
                // НЕ завантажуючи і НЕ блокуючи саму DLL.
                var versionInfo = FileVersionInfo.GetVersionInfo(InstallerDllPath);

                // Використовуйте FileVersion або ProductVersion. 
                // FileVersion зазвичай є правильним вибором.
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

        private static async Task<bool> UpdateInstallerCoreAsync(InstallerCoreInfo coreInfo)
        {
            if (coreInfo == null || string.IsNullOrWhiteSpace(coreInfo.Url))
            {
                Console.WriteLine("❌ Невалідна інформація про оновлення.");
                return false;
            }

            try
            {
                // ⬅ очистити/підготувати temp
                if (Directory.Exists(TempDirectory))
                    Directory.Delete(TempDirectory, true);
                Directory.CreateDirectory(TempDirectory);

                Console.WriteLine($"⬇️ Завантаження оновлення з {coreInfo.Url}...");

                var zipPath = Path.Combine(TempDirectory, "installer_core.zip");

                // ===== Локальні хелпери без перейменування зовнішніх методів =====
                static bool LooksLikeHtml(byte[] head)
                {
                    if (head.Length == 0) return true;
                    var s = System.Text.Encoding.UTF8.GetString(head);
                    return s.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                static bool LooksLikeZip(byte[] head)
                    => head.Length >= 4 && head[0] == (byte)'P' && head[1] == (byte)'K';

                static string Sha256HexOf(string file)
                {
                    using var fs = File.OpenRead(file);
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    return Convert.ToHexString(sha.ComputeHash(fs));
                }

                async Task DownloadToFileAsync(HttpClient http, string url, string path, CancellationToken ct)
                {
                    using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();

                    await using var src = await resp.Content.ReadAsStreamAsync(ct);
                    await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
                    await src.CopyToAsync(dst, 8192, ct);
                    await dst.FlushAsync(ct);
                }

                async Task<bool> TryDownloadDriveAsync(string url, string path, CancellationToken ct)
                {
                    using var handler = new HttpClientHandler
                    {
                        AllowAutoRedirect = true,
                        AutomaticDecompression = System.Net.DecompressionMethods.All,
                        UseCookies = true,
                        CookieContainer = new System.Net.CookieContainer()
                    };
                    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("CatSuiteLauncher/1.0");

                    // 1) Перша спроба
                    await DownloadToFileAsync(http, url, path, ct);

                    // Перевіряємо, що не HTML і схоже на ZIP
                    var head = await File.ReadAllBytesAsync(path, ct);
                    var headSlice = head.AsSpan(0, Math.Min(1024, head.Length)).ToArray();

                    if (!LooksLikeHtml(headSlice) && LooksLikeZip(headSlice))
                        return true;

                    // 2) Якщо це Google Drive HTML — дістати confirm і перезакачати
                    var html = System.Text.Encoding.UTF8.GetString(head);
                    var confirm = System.Text.RegularExpressions.Regex
                        .Match(html, @"name=""confirm""\s+value=""(?<v>[^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        .Groups["v"].Value;

                    if (string.IsNullOrEmpty(confirm))
                    {
                        // іноді confirm у URL
                        var m2 = System.Text.RegularExpressions.Regex.Match(html, @"confirm=([0-9A-Za-z_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m2.Success) confirm = m2.Groups[1].Value;
                    }

                    var idMatch = System.Text.RegularExpressions.Regex.Match(url, @"[\?&]id=([^&]+)");
                    var id = idMatch.Success ? idMatch.Groups[1].Value : null;
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(confirm))
                        return false;

                    var confirmedUrl = $"https://drive.google.com/uc?export=download&confirm={confirm}&id={id}";

                    await DownloadToFileAsync(http, confirmedUrl, path, ct);

                    var head2 = await File.ReadAllBytesAsync(path, ct);
                    var head2Slice = head2.AsSpan(0, Math.Min(1024, head2.Length)).ToArray();
                    return !LooksLikeHtml(head2Slice) && LooksLikeZip(head2Slice);
                }
                // ===== кінець локальних хелперів =====

                // 3 ретраї на всяк випадок
                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                var ok = false;
                for (int attempt = 1; attempt <= 3 && !ok; attempt++)
                {
                    try
                    {
                        if (File.Exists(zipPath)) File.Delete(zipPath);
                        ok = await TryDownloadDriveAsync(coreInfo.Url, zipPath, cts.Token);
                        if (!ok) Console.WriteLine($"⚠️ Спроба {attempt}: отримано не ZIP (ймовірно HTML від Drive).");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Спроба {attempt} не вдалася: {ex.Message}");
                        await Task.Delay(1000);
                    }
                }

                if (!ok)
                {
                    Console.WriteLine("❌ Не вдалося отримати валідний ZIP з Google Drive.");
                    return false;
                }

                // Перевірка SHA-256 перед розпакуванням (якщо в маніфесті задано)
                if (!string.IsNullOrWhiteSpace(coreInfo.Sha256))
                {
                    var got = Sha256HexOf(zipPath);
                    if (!got.Equals(coreInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"❌ Хеш не співпав. Очікував: {coreInfo.Sha256}, отримав: {got}");
                        return false;
                    }
                    Console.WriteLine("✅ Контрольна сума підтверджена.");
                }

                Console.WriteLine("📦 Розпакування...");
                // Розпаковуємо у TempDirectory (staging)
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, TempDirectory, overwriteFiles: true);

                // Підготуємо шляхи для батника (як у тебе було)
                string tempDllPath = Path.Combine(TempDirectory, "CatSuite.Installer.dll");
                string targetDllPath = InstallerDllPath;
                string tempPdbPath = Path.Combine(TempDirectory, "CatSuite.Installer.pdb");
                string targetPdbPath = Path.Combine(AppDirectory, "CatSuite.Installer.pdb");
                string launcherExePath = Process.GetCurrentProcess().MainModule!.FileName;
                string batPath = Path.Combine(TempDirectory, "update.bat");

                string batContent = string.Format(
        @"@echo off
echo.
echo === CatSuite Updater ===
echo Будь ласка, зачекайте, лаунчер оновлюється...
echo.

timeout /t 3 /nobreak > nul

setlocal
set RETRY=5
:copy_loop
copy /Y ""{0}"" ""{1}""
if errorlevel 1 (
    echo Помилка копіювання! Файл може бути заблокований.
    set /a RETRY-=1
    if %RETRY% equ 0 goto copy_fail
    timeout /t 1 /nobreak > nul
    goto copy_loop
)
echo Копіювання DLL успішне.

if exist ""{2}"" copy /Y ""{2}"" ""{3}""

goto copy_success

:copy_fail
echo НЕ ВДАЛОСЯ оновити файл.
goto start_launcher

:copy_success
echo Оновлення успішне.
start """" cmd /c ""rd /s /q ""{4}"""" 

:start_launcher
start """" ""{5}""
exit
",
                    tempDllPath,
                    targetDllPath,
                    tempPdbPath,
                    targetPdbPath,
                    TempDirectory,
                    launcherExePath
                );

                // Запис батника OEM-866, як у тебе
                var oem866 = Encoding.GetEncoding(866);
                await File.WriteAllTextAsync(batPath, batContent, oem866);

                // Запускаємо оновлення і виходимо
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


        private static void RestartLauncher()
        {
            var exePath = Process.GetCurrentProcess().MainModule.FileName;
            Process.Start(exePath);
        }

        private static void LaunchInstallerCore(ManifestModel manifest)
        {
            try
            {
                // ✅ ВИРІШЕННЯ: Читаємо файл в пам'ять і завантажуємо звідти
                byte[] assemblyBytes = File.ReadAllBytes(InstallerDllPath);
                var assembly = Assembly.Load(assemblyBytes); // НЕ .LoadFrom()

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
    }

    #endregion
}