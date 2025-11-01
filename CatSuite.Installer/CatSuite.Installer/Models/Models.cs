using System;
using System.Collections.Generic;

namespace CatSuite.Installer.Models
{
    // ========== МАНІФЕСТ ==========

    public class ManifestModel
    {
        public InstallerCoreInfo InstallerCore { get; set; }
        public List<PackageInfo> Packages { get; set; } = new();
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
        public List<string> DependsOn { get; set; } = new();
        public bool IsVisible { get; set; } = true;
        public string Category { get; set; }
    }

    // ========== СТАН ПАКЕТА ==========

    public class PackageState
    {
        public PackageInfo Info { get; set; }
        public string InstalledVersion { get; set; }
        public PackageStatus Status { get; set; }
        public string StatusText { get; set; }
        public bool IsSelected { get; set; }
        public PackageAction RecommendedAction { get; set; }
    }

    public enum PackageStatus
    {
        NotInstalled,
        Installed,
        UpdateAvailable,
        Installing,
        Updating,
        Removing,
        Error
    }

    public enum PackageAction
    {
        None,
        Install,
        Update,
        Remove,
        Repair
    }

    // ========== БД МОДЕЛІ ==========

    public class InstalledPackage
    {
        public int Id { get; set; }
        public string PackageId { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string InstallPath { get; set; }
        public DateTime InstalledAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string MsiProductCode { get; set; }
    }

    public class InstallationLog
    {
        public int Id { get; set; }
        public string PackageId { get; set; }
        public string Action { get; set; } // Install, Update, Remove
        public string Version { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
        public string LogFilePath { get; set; }
    }

    public class DependencyRelation
    {
        public int Id { get; set; }
        public string PackageId { get; set; }
        public string DependsOnPackageId { get; set; }
        public bool IsRequired { get; set; }
    }

    // ========== ОПЕРАЦІЇ ВСТАНОВЛЕННЯ ==========

    public class InstallationTask
    {
        public string PackageId { get; set; }
        public string PackageName { get; set; }
        public PackageAction Action { get; set; }
        public string Version { get; set; }
        public string MsiUrl { get; set; }
        public string Sha256 { get; set; }
        public List<string> Dependencies { get; set; } = new();
        public int Order { get; set; } // Для правильної послідовності встановлення
    }

    public class InstallationProgress
    {
        public string CurrentPackage { get; set; }
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; }
        public double OverallProgress { get; set; }
        public string StatusMessage { get; set; }
        public bool IsDownloading { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
    }

    public class InstallationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> InstalledPackages { get; set; } = new();
        public List<string> FailedPackages { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public DateTime CompletedAt { get; set; }
    }
}