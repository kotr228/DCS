using System;
using System.Collections.Generic;
using System.Linq;
using CatSuite.Installer.Models;

namespace CatSuite.Installer.Services
{
    /// <summary>
    /// Сервіс для аналізу та управління пакетами
    /// </summary>
    public class PackageService
    {
        private readonly DatabaseManager _dbManager;
        private readonly ManifestModel _manifest;

        public PackageService(DatabaseManager dbManager, ManifestModel manifest)
        {
            _dbManager = dbManager;
            _manifest = manifest;
        }

        /// <summary>
        /// Отримати DatabaseManager для доступу до БД
        /// </summary>
        public DatabaseManager GetDatabaseManager()
        {
            return _dbManager;
        }

        /// <summary>
        /// Отримати стан всіх пакетів (встановлені + доступні)
        /// </summary>
        public List<PackageState> GetAllPackageStates()
        {
            var states = new List<PackageState>();
            var installedPackages = _dbManager.GetAllInstalledPackages();

            foreach (var packageInfo in _manifest.Packages.Where(p => p.IsVisible))
            {
                var installed = installedPackages.FirstOrDefault(ip => ip.PackageId == packageInfo.Id);

                var state = new PackageState
                {
                    Info = packageInfo,
                    InstalledVersion = installed?.Version,
                    Status = DetermineStatus(packageInfo, installed),
                    IsSelected = false
                };

                state.RecommendedAction = DetermineRecommendedAction(state);
                state.StatusText = GetStatusText(state);

                states.Add(state);
            }

            return states;
        }

        /// <summary>
        /// Визначити статус пакета
        /// </summary>
        private PackageStatus DetermineStatus(PackageInfo packageInfo, InstalledPackage installed)
        {
            if (installed == null)
                return PackageStatus.NotInstalled;

            try
            {
                var installedVersion = new Version(installed.Version);
                var availableVersion = new Version(packageInfo.Version);

                if (availableVersion > installedVersion)
                    return PackageStatus.UpdateAvailable;

                return PackageStatus.Installed;
            }
            catch
            {
                // Якщо не можемо розпарсити версії, вважаємо встановленим
                return PackageStatus.Installed;
            }
        }

        /// <summary>
        /// Визначити рекомендовану дію
        /// </summary>
        private PackageAction DetermineRecommendedAction(PackageState state)
        {
            return state.Status switch
            {
                PackageStatus.NotInstalled => PackageAction.Install,
                PackageStatus.UpdateAvailable => PackageAction.Update,
                PackageStatus.Installed => PackageAction.None,
                _ => PackageAction.None
            };
        }

        /// <summary>
        /// Отримати текст статусу
        /// </summary>
        private string GetStatusText(PackageState state)
        {
            return state.Status switch
            {
                PackageStatus.NotInstalled => "Не встановлено",
                PackageStatus.Installed => $"Встановлено v{state.InstalledVersion}",
                PackageStatus.UpdateAvailable => $"Оновлення доступне: v{state.InstalledVersion} → v{state.Info.Version}",
                PackageStatus.Installing => "Встановлення...",
                PackageStatus.Updating => "Оновлення...",
                PackageStatus.Removing => "Видалення...",
                PackageStatus.Error => "Помилка",
                _ => "Невідомо"
            };
        }

        /// <summary>
        /// Побудувати план встановлення з урахуванням залежностей
        /// </summary>
        public List<InstallationTask> BuildInstallationPlan(List<PackageState> selectedPackages)
        {
            var tasks = new List<InstallationTask>();
            var processed = new HashSet<string>();

            foreach (var package in selectedPackages.Where(p => p.IsSelected))
            {
                AddPackageWithDependencies(package.Info, package.RecommendedAction, tasks, processed);
            }

            // Сортуємо за залежностями (залежності першими)
            return OrderByDependencies(tasks);
        }

        /// <summary>
        /// Рекурсивно додати пакет та його залежності
        /// </summary>
        private void AddPackageWithDependencies(
            PackageInfo package,
            PackageAction action,
            List<InstallationTask> tasks,
            HashSet<string> processed)
        {
            if (processed.Contains(package.Id))
                return;

            // Спочатку додаємо залежності
            if (package.DependsOn != null && package.DependsOn.Count > 0)
            {
                foreach (var depId in package.DependsOn)
                {
                    var dependency = _manifest.Packages.FirstOrDefault(p => p.Id == depId);
                    if (dependency != null)
                    {
                        var installed = _dbManager.GetInstalledPackage(depId);
                        var depAction = installed == null ? PackageAction.Install : PackageAction.None;

                        if (depAction != PackageAction.None)
                        {
                            AddPackageWithDependencies(dependency, depAction, tasks, processed);
                        }
                    }
                }
            }

            // Потім додаємо сам пакет
            tasks.Add(new InstallationTask
            {
                PackageId = package.Id,
                PackageName = package.Name,
                Action = action,
                Version = package.Version,
                MsiUrl = package.Url,
                Sha256 = package.Sha256,
                Dependencies = package.DependsOn ?? new List<string>()
            });

            processed.Add(package.Id);
        }

        /// <summary>
        /// Сортувати завдання за залежностями
        /// </summary>
        private List<InstallationTask> OrderByDependencies(List<InstallationTask> tasks)
        {
            var ordered = new List<InstallationTask>();
            var remaining = new List<InstallationTask>(tasks);

            int order = 0;
            while (remaining.Count > 0)
            {
                var canInstall = remaining.Where(t =>
                    t.Dependencies.All(depId =>
                        ordered.Any(o => o.PackageId == depId) ||
                        !tasks.Any(x => x.PackageId == depId)
                    )
                ).ToList();

                if (canInstall.Count == 0)
                {
                    // Циклічна залежність або помилка - додаємо все що залишилось
                    ordered.AddRange(remaining);
                    break;
                }

                foreach (var task in canInstall)
                {
                    task.Order = order++;
                    ordered.Add(task);
                    remaining.Remove(task);
                }
            }

            return ordered;
        }

        /// <summary>
        /// Перевірити, чи можна видалити пакет (чи не залежить від нього щось встановлене)
        /// </summary>
        public bool CanRemovePackage(string packageId, out List<string> dependentPackages)
        {
            dependentPackages = new List<string>();
            var installedPackages = _dbManager.GetAllInstalledPackages();

            foreach (var installed in installedPackages)
            {
                var packageInfo = _manifest.Packages.FirstOrDefault(p => p.Id == installed.PackageId);
                if (packageInfo?.DependsOn != null && packageInfo.DependsOn.Contains(packageId))
                {
                    dependentPackages.Add(packageInfo.Name);
                }
            }

            return dependentPackages.Count == 0;
        }

        /// <summary>
        /// Отримати інформацію про пакет за ID
        /// </summary>
        public PackageInfo GetPackageInfo(string packageId)
        {
            return _manifest.Packages.FirstOrDefault(p => p.Id == packageId);
        }

        /// <summary>
        /// Перевірити доступність оновлень
        /// </summary>
        public int GetAvailableUpdatesCount()
        {
            var states = GetAllPackageStates();
            return states.Count(s => s.Status == PackageStatus.UpdateAvailable);
        }
    }
}