using System;
using System.Collections.Generic;

namespace DocControlService.Shared
{
    /// <summary>
    /// Модель для пристрою з таблиці Devises
    /// </summary>
    [Serializable]
    public class DeviceModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Access { get; set; }
    }

    /// <summary>
    /// Модель для мережевого доступу до директорії
    /// </summary>
    [Serializable]
    public class NetworkAccessModel
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public bool Status { get; set; }
        public int DeviceId { get; set; }
    }

    /// <summary>
    /// Розширена модель директорії з інформацією про доступ
    /// </summary>
    [Serializable]
    public class DirectoryWithAccessModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Browse { get; set; }
        public bool IsShared { get; set; }
        public string GitStatus { get; set; }
        public List<DeviceModel> AllowedDevices { get; set; } = new List<DeviceModel>();
    }

    /// <summary>
    /// Модель логу Git комітів
    /// </summary>
    [Serializable]
    public class CommitLogModel
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string DirectoryPath { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Модель логу помилок
    /// </summary>
    [Serializable]
    public class ErrorLogModel
    {
        public int Id { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string UserFriendlyMessage { get; set; }
        public string StackTrace { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsResolved { get; set; }
    }

    /// <summary>
    /// Модель історії комітів Git
    /// </summary>
    [Serializable]
    public class GitCommitHistoryModel
    {
        public string Hash { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public DateTime Date { get; set; }
    }

    /// <summary>
    /// Команди для взаємодії між UI та Service
    /// </summary>
    [Serializable]
    public class ServiceCommand
    {
        public CommandType Type { get; set; }
        public string Data { get; set; }
    }

    public enum CommandType
    {
        // Directory operations
        GetDirectories,
        AddDirectory,
        RemoveDirectory,
        UpdateDirectoryName,
        ScanDirectory,

        // Device operations
        GetDevices,
        AddDevice,
        RemoveDevice,
        UpdateDevice,

        // Access control
        GrantAccess,
        RevokeAccess,
        OpenAllShares,
        CloseAllShares,
        GetNetworkAccess,

        // Service status
        GetStatus,
        GetServiceLogs,

        // Version control
        ForceCommit,
        GetCommitLog,
        SetCommitInterval,
        GetGitHistory,
        RevertToCommit,
        GetDirectoryGitStatus,

        // Error logging
        GetErrorLog,
        MarkErrorResolved,
        ClearResolvedErrors,
        GetUnresolvedErrorCount,

        // Settings
        GetSettings,
        SaveSettings,
        GetSetting,
        SetSetting,

        // Roadmap
        CreateRoadmap,
        GetRoadmaps,
        GetRoadmapById,
        DeleteRoadmap,
        AnalyzeDirectoryForRoadmap,
        ExportRoadmapAsJson,
        ExportRoadmapAsImage,

        // Network Discovery
        ScanNetwork,
        GetNetworkInterfaces,
        GetNetworkDevices,

        // External Services
        GetExternalServices,
        AddExternalService,
        UpdateExternalService,
        DeleteExternalService,
        TestExternalService
    }

    /// <summary>
    /// Відповідь від сервісу
    /// </summary>
    [Serializable]
    public class ServiceResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Data { get; set; }
    }

    /// <summary>
    /// Статус сервісу
    /// </summary>
    [Serializable]
    public class ServiceStatus
    {
        public bool IsRunning { get; set; }
        public int TotalDirectories { get; set; }
        public int SharedDirectories { get; set; }
        public int RegisteredDevices { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? LastCommitTime { get; set; }
        public int CommitIntervalMinutes { get; set; }
        public int UnresolvedErrors { get; set; }
    }

    /// <summary>
    /// Запит на додавання директорії
    /// </summary>
    [Serializable]
    public class AddDirectoryRequest
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    /// <summary>
    /// Запит на оновлення імені директорії
    /// </summary>
    [Serializable]
    public class UpdateDirectoryNameRequest
    {
        public int DirectoryId { get; set; }
        public string NewName { get; set; }
    }

    /// <summary>
    /// Запит на надання/відкликання доступу
    /// </summary>
    [Serializable]
    public class AccessRequest
    {
        public int DirectoryId { get; set; }
        public int DeviceId { get; set; }
    }

    /// <summary>
    /// Запит на відкат версії Git
    /// </summary>
    [Serializable]
    public class RevertRequest
    {
        public int DirectoryId { get; set; }
        public string CommitHash { get; set; }
    }

    /// <summary>
    /// Налаштування додатку
    /// </summary>
    [Serializable]
    public class AppSettings
    {
        public bool AutoShareOnAdd { get; set; }
        public bool EnableUpdateNotifications { get; set; }
        public int CommitIntervalMinutes { get; set; }
    }

    /// <summary>
    /// Елемент дорожньої карти (timeline event)
    /// </summary>
    [Serializable]
    public class RoadmapEvent
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime EventDate { get; set; }
        public string EventType { get; set; } // file_created, file_modified, manual, milestone
        public string FilePath { get; set; }
        public string Category { get; set; }
    }

    /// <summary>
    /// Дорожня карта (набір подій)
    /// </summary>
    [Serializable]
    public class Roadmap
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<RoadmapEvent> Events { get; set; } = new List<RoadmapEvent>();
    }

    /// <summary>
    /// Мережевий пристрій
    /// </summary>
    [Serializable]
    public class NetworkDevice
    {
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string HostName { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
    }

    /// <summary>
    /// Мережевий інтерфейс ПК
    /// </summary>
    [Serializable]
    public class NetworkInterfaceInfo
    {
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string NetworkType { get; set; } // Ethernet, WiFi
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Налаштування зовнішнього сервісу
    /// </summary>
    [Serializable]
    public class ExternalService
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ServiceType { get; set; } // api, ftp, webhook
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastUsed { get; set; }
    }
}