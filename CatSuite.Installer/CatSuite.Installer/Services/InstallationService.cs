using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CatSuite.Installer.Models;

namespace CatSuite.Installer.Services
{
    /// <summary>
    /// Сервіс для виконання операцій встановлення/оновлення/видалення MSI пакетів
    /// </summary>
    public class InstallationService
    {
        private readonly DatabaseManager _dbManager;
        private readonly string _cacheDirectory;
        private readonly string _logsDirectory;

        public event EventHandler<InstallationProgress> ProgressChanged;

        public InstallationService(DatabaseManager dbManager)
        {
            _dbManager = dbManager;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string catSuiteFolder = Path.Combine(appData, "CatSuite");

            _cacheDirectory = Path.Combine(catSuiteFolder, "Cache");
            _logsDirectory = Path.Combine(catSuiteFolder, "Logs");

            Directory.CreateDirectory(_cacheDirectory);
            Directory.CreateDirectory(_logsDirectory);
        }

        /// <summary>
        /// Виконати план встановлення
        /// </summary>
        public async Task<InstallationResult> ExecuteInstallationPlanAsync(
            List<InstallationTask> tasks,
            CancellationToken cancellationToken = default)
        {
            var result = new InstallationResult
            {
                CompletedAt = DateTime.Now
            };

            int currentStep = 0;
            int totalSteps = tasks.Count * 2; // Завантаження + встановлення

            try
            {
                foreach (var task in tasks.OrderBy(t => t.Order))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Console.WriteLine($"\n=== {task.Action} {task.PackageName} v{task.Version} ===");

                    // Крок 1: Завантаження MSI
                    ReportProgress(new InstallationProgress
                    {
                        CurrentPackage = task.PackageName,
                        CurrentStep = ++currentStep,
                        TotalSteps = totalSteps,
                        OverallProgress = (currentStep * 100.0 / totalSteps),
                        StatusMessage = $"Завантаження {task.PackageName}...",
                        IsDownloading = true
                    });

                    string msiPath = await DownloadMsiAsync(task, cancellationToken);
                    if (string.IsNullOrEmpty(msiPath))
                    {
                        result.FailedPackages.Add(task.PackageName);
                        result.Errors.Add($"Не вдалося завантажити {task.PackageName}");
                        continue;
                    }

                    // Крок 2: Встановлення
                    ReportProgress(new InstallationProgress
                    {
                        CurrentPackage = task.PackageName,
                        CurrentStep = ++currentStep,
                        TotalSteps = totalSteps,
                        OverallProgress = (currentStep * 100.0 / totalSteps),
                        StatusMessage = $"Встановлення {task.PackageName}...",
                        IsDownloading = false
                    });

                    bool success = await InstallMsiAsync(task, msiPath, cancellationToken);

                    if (success)
                    {
                        result.InstalledPackages.Add(task.PackageName);
                        Console.WriteLine($"✅ {task.PackageName} встановлено успішно.");
                    }
                    else
                    {
                        result.FailedPackages.Add(task.PackageName);
                        result.Errors.Add($"Помилка встановлення {task.PackageName}");
                        Console.WriteLine($"❌ Помилка встановлення {task.PackageName}");
                    }
                }

                result.Success = result.FailedPackages.Count == 0;
                result.Message = result.Success
                    ? "Встановлення завершено успішно."
                    : $"Встановлення завершено з помилками. Невдалих: {result.FailedPackages.Count}";
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "Встановлення скасовано користувачем.";
                Console.WriteLine("⚠️ Операція скасована.");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Критична помилка: {ex.Message}";
                result.Errors.Add(ex.ToString());
                Console.WriteLine($"❌ Критична помилка: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Завантажити MSI файл
        /// </summary>
        private async Task<string> DownloadMsiAsync(InstallationTask task, CancellationToken cancellationToken)
        {
            try
            {
                string fileName = $"{task.PackageId}_v{task.Version}.msi";
                string targetPath = Path.Combine(_cacheDirectory, fileName);

                // Перевіряємо кеш
                if (File.Exists(targetPath) && ValidateSha256(targetPath, task.Sha256))
                {
                    Console.WriteLine($"✅ Використовується кешований файл: {fileName}");
                    return targetPath;
                }

                Console.WriteLine($"⬇️ Завантаження з {task.MsiUrl}...");

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

                using var response = await client.GetAsync(task.MsiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        ReportProgress(new InstallationProgress
                        {
                            CurrentPackage = task.PackageName,
                            IsDownloading = true,
                            DownloadedBytes = totalRead,
                            TotalBytes = totalBytes.Value,
                            StatusMessage = $"Завантаження {task.PackageName}: {totalRead / 1024 / 1024} MB / {totalBytes.Value / 1024 / 1024} MB"
                        });
                    }
                }

                Console.WriteLine($"✅ Завантажено: {totalRead / 1024 / 1024} MB");

                // Перевірка SHA256
                if (!string.IsNullOrEmpty(task.Sha256))
                {
                    if (!ValidateSha256(targetPath, task.Sha256))
                    {
                        Console.WriteLine("❌ Контрольна сума не збігається!");
                        File.Delete(targetPath);
                        return null;
                    }
                    Console.WriteLine("✅ Контрольна сума підтверджена.");
                }

                return targetPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка завантаження: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Встановити MSI пакет
        /// </summary>
        private async Task<bool> InstallMsiAsync(InstallationTask task, string msiPath, CancellationToken cancellationToken)
        {
            string logFileName = $"{task.PackageId}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            string logPath = Path.Combine(_logsDirectory, logFileName);

            try
            {
                string arguments = task.Action switch
                {
                    PackageAction.Install => $"/i \"{msiPath}\" /qn /norestart /l*v \"{logPath}\"",
                    PackageAction.Update => $"/i \"{msiPath}\" /qn /norestart /l*v \"{logPath}\" REINSTALL=ALL REINSTALLMODE=vomus",
                    PackageAction.Remove => $"/x \"{msiPath}\" /qn /norestart /l*v \"{logPath}\"",
                    _ => $"/i \"{msiPath}\" /qn /norestart /l*v \"{logPath}\""
                };

                Console.WriteLine($"🔧 msiexec {arguments}");

                var processInfo = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(processInfo);

                await process.WaitForExitAsync(cancellationToken);

                bool success = process.ExitCode == 0 || process.ExitCode == 3010; // 3010 = потрібен перезапуск

                // Логуємо в БД
                var log = new InstallationLog
                {
                    PackageId = task.PackageId,
                    Action = task.Action.ToString(),
                    Version = task.Version,
                    Success = success,
                    ErrorMessage = success ? null : $"Exit code: {process.ExitCode}",
                    Timestamp = DateTime.Now,
                    LogFilePath = logPath
                };

                _dbManager.AddInstallationLog(log);

                if (success && task.Action != PackageAction.Remove)
                {
                    // Оновлюємо БД встановлених пакетів
                    var installedPackage = new InstalledPackage
                    {
                        PackageId = task.PackageId,
                        Name = task.PackageName,
                        Version = task.Version,
                        InstallPath = GetInstallPath(task.PackageId),
                        InstalledAt = task.Action == PackageAction.Install ? DateTime.Now : _dbManager.GetInstalledPackage(task.PackageId)?.InstalledAt ?? DateTime.Now,
                        UpdatedAt = task.Action == PackageAction.Update ? DateTime.Now : null,
                        MsiProductCode = ExtractProductCode(logPath)
                    };

                    _dbManager.SaveInstalledPackage(installedPackage);
                }
                else if (success && task.Action == PackageAction.Remove)
                {
                    _dbManager.RemoveInstalledPackage(task.PackageId);
                }

                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка виконання msiexec: {ex.Message}");

                _dbManager.AddInstallationLog(new InstallationLog
                {
                    PackageId = task.PackageId,
                    Action = task.Action.ToString(),
                    Version = task.Version,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.Now,
                    LogFilePath = logPath
                });

                return false;
            }
        }

        /// <summary>
        /// Перевірка SHA256 хешу
        /// </summary>
        private bool ValidateSha256(string filePath, string expectedHash)
        {
            if (string.IsNullOrEmpty(expectedHash))
                return true; // Якщо хеш не вказаний, вважаємо валідним

            try
            {
                using var sha256 = SHA256.Create();
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

        /// <summary>
        /// Отримати шлях встановлення з реєстру
        /// </summary>
        private string GetInstallPath(string packageId)
        {
            // Тут можна додати логіку читання з реєстру
            // Для простоти повертаємо стандартний шлях
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), packageId);
        }

        /// <summary>
        /// Витягнути Product Code з MSI логу
        /// </summary>
        private string ExtractProductCode(string logPath)
        {
            try
            {
                if (!File.Exists(logPath))
                    return null;

                var lines = File.ReadAllLines(logPath);
                var productCodeLine = lines.FirstOrDefault(l => l.Contains("Product Code"));

                if (productCodeLine != null)
                {
                    // Парсинг Product Code з логу
                    var parts = productCodeLine.Split(':');
                    if (parts.Length > 1)
                        return parts[1].Trim();
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Повідомити про прогрес
        /// </summary>
        private void ReportProgress(InstallationProgress progress)
        {
            ProgressChanged?.Invoke(this, progress);
        }
    }
}