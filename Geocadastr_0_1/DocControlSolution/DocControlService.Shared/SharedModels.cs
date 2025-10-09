using System;
using System.Collections.Generic;

namespace DocControlService.Shared
{
    // =============== ІСНУЮЧІ МОДЕЛІ (v0.1-0.2) ===============

    [Serializable]
    public class DeviceModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Access { get; set; }
    }

    [Serializable]
    public class NetworkAccessModel
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public bool Status { get; set; }
        public int DeviceId { get; set; }
    }

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

    [Serializable]
    public class RoadmapEvent
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime EventDate { get; set; }
        public string EventType { get; set; }
        public string FilePath { get; set; }
        public string Category { get; set; }
    }

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

    // =============== НОВІ МОДЕЛІ v0.3 - ГЕОДОРОЖНІ КАРТИ ===============

    /// <summary>
    /// Географічна точка на карті
    /// </summary>
    [Serializable]
    public class GeoPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Address { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Геодорожня карта проекту
    /// </summary>
    [Serializable]
    public class GeoRoadmap
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string CreatedBy { get; set; }

        // Налаштування карти
        public MapProvider MapProvider { get; set; } = MapProvider.OpenStreetMap;
        public double CenterLatitude { get; set; }
        public double CenterLongitude { get; set; }
        public int ZoomLevel { get; set; } = 10;

        // Елементи карти
        public List<GeoRoadmapNode> Nodes { get; set; } = new List<GeoRoadmapNode>();
        public List<GeoRoadmapRoute> Routes { get; set; } = new List<GeoRoadmapRoute>();
        public List<GeoRoadmapArea> Areas { get; set; } = new List<GeoRoadmapArea>();
    }

    /// <summary>
    /// Вузол на геокарті (точка)
    /// </summary>
    [Serializable]
    public class GeoRoadmapNode
    {
        public int Id { get; set; }
        public int GeoRoadmapId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Address { get; set; }
        public NodeType Type { get; set; }
        public string IconName { get; set; }
        public string Color { get; set; }
        public DateTime? EventDate { get; set; }
        public string RelatedFiles { get; set; } // JSON масив шляхів до файлів
        public int OrderIndex { get; set; }
    }

    /// <summary>
    /// Маршрут між точками
    /// </summary>
    [Serializable]
    public class GeoRoadmapRoute
    {
        public int Id { get; set; }
        public int GeoRoadmapId { get; set; }
        public int FromNodeId { get; set; }
        public int ToNodeId { get; set; }
        public string Label { get; set; }
        public string Color { get; set; }
        public RouteStyle Style { get; set; }
        public int StrokeWidth { get; set; } = 2;
    }

    /// <summary>
    /// Область на карті (полігон)
    /// </summary>
    [Serializable]
    public class GeoRoadmapArea
    {
        public int Id { get; set; }
        public int GeoRoadmapId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string PolygonCoordinates { get; set; } // JSON масив координат
        public string FillColor { get; set; }
        public string StrokeColor { get; set; }
        public double Opacity { get; set; } = 0.3;
    }

    /// <summary>
    /// Шаблон геокарти
    /// </summary>
    [Serializable]
    public class GeoRoadmapTemplate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string TemplateJson { get; set; } // JSON структура шаблону
        public bool IsBuiltIn { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Правило IP фільтрації
    /// </summary>
    [Serializable]
    public class IpFilterRule
    {
        public int Id { get; set; }
        public string RuleName { get; set; }
        public string IpAddress { get; set; } // Може бути IP або CIDR (192.168.1.0/24)
        public IpFilterAction Action { get; set; }
        public bool IsEnabled { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? DirectoryId { get; set; } // null = глобальне правило
        public int? GeoRoadmapId { get; set; } // null = для всіх карт
    }

    // =============== ЕНУМИ v0.3 ===============

    public enum MapProvider
    {
        OpenStreetMap,
        GoogleMaps,
        BingMaps
    }

    public enum NodeType
    {
        Milestone,      // Віха проекту
        Location,       // Локація
        Office,         // Офіс
        Site,           // Об'єкт
        Meeting,        // Зустріч
        Checkpoint,     // Контрольна точка
        Custom          // Користувацька
    }

    public enum RouteStyle
    {
        Solid,
        Dashed,
        Dotted,
        Arrow
    }

    public enum IpFilterAction
    {
        Allow,
        Deny
    }

    // =============== ОНОВЛЕНІ КОМАНДИ v0.3 ===============

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

        // Roadmap (v0.2)
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
        TestExternalService,

        // ===== НОВІ КОМАНДИ v0.3 - GEO ROADMAPS =====

        // Geo Roadmap CRUD
        CreateGeoRoadmap,
        GetGeoRoadmaps,
        GetGeoRoadmapById,
        UpdateGeoRoadmap,
        DeleteGeoRoadmap,

        // Geo Nodes
        AddGeoNode,
        UpdateGeoNode,
        DeleteGeoNode,
        GetGeoNodesByRoadmap,

        // Geo Routes
        AddGeoRoute,
        UpdateGeoRoute,
        DeleteGeoRoute,

        // Geo Areas
        AddGeoArea,
        UpdateGeoArea,
        DeleteGeoArea,

        // Templates
        GetGeoRoadmapTemplates,
        CreateFromTemplate,
        SaveAsTemplate,

        // Map operations
        GeocodeAddress,        // Адреса -> координати
        ReverseGeocode,        // Координати -> адреса
        CalculateRoute,        // Розрахунок маршруту

        // IP Filtering
        GetIpFilterRules,
        AddIpFilterRule,
        UpdateIpFilterRule,
        DeleteIpFilterRule,
        TestIpAccess,

        // ===== AI КОМАНДИ v0.4.1 =====

        // AI Analysis
        StartAIAnalysis,
        GetAIAnalysisResults,
        GetAIAnalysisById,
        ApplyAIRecommendations,
        GetAIServiceStatus,

        // Chronological Roadmaps
        GenerateAIChronologicalRoadmap,
        GetAIChronologicalRoadmaps,
        GetAIChronologicalRoadmapById,
        DeleteAIChronologicalRoadmap,
        ExportAIChronologicalRoadmap,

        // Geo Roadmaps AI
        GenerateGeoRoadmapFromAI,
        ExtractLocationsFromFiles,

        // File Reorganization
        ValidateDirectoryStructure,
        GetStructureViolations,
        ApplyReorganization,
        PreviewReorganization,

        // AI Statistics
        GetAIStatistics

    }

    [Serializable]
    public class ServiceCommand
    {
        public CommandType Type { get; set; }
        public string Data { get; set; }
    }

    [Serializable]
    public class ServiceResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Data { get; set; }
    }

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
        public int TotalGeoRoadmaps { get; set; } // НОВЕ v0.3
        public bool WebApiEnabled { get; set; } // НОВЕ v0.3
    }

    // =============== ЗАПИТИ v0.3 ===============

    [Serializable]
    public class CreateGeoRoadmapRequest
    {
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public MapProvider MapProvider { get; set; }
        public double CenterLatitude { get; set; }
        public double CenterLongitude { get; set; }
        public int ZoomLevel { get; set; }
    }

    [Serializable]
    public class GeocodeRequest
    {
        public string Address { get; set; }
    }

    [Serializable]
    public class GeocodeResponse
    {
        public bool Success { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string FormattedAddress { get; set; }
    }

    [Serializable]
    public class AddDirectoryRequest
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    [Serializable]
    public class UpdateDirectoryNameRequest
    {
        public int DirectoryId { get; set; }
        public string NewName { get; set; }
    }

    [Serializable]
    public class AccessRequest
    {
        public int DirectoryId { get; set; }
        public int DeviceId { get; set; }
    }

    [Serializable]
    public class RevertRequest
    {
        public int DirectoryId { get; set; }
        public string CommitHash { get; set; }
    }

    [Serializable]
    public class AppSettings
    {
        public bool AutoShareOnAdd { get; set; }
        public bool EnableUpdateNotifications { get; set; }
        public int CommitIntervalMinutes { get; set; }
        public bool EnableWebApi { get; set; } // НОВЕ v0.3
        public int WebApiPort { get; set; } = 5000; // НОВЕ v0.3
        public string DefaultMapProvider { get; set; } = "OpenStreetMap"; // НОВЕ v0.3
    }

    // Існуючі моделі (CommitLogModel, ErrorLogModel, ExternalService тощо)
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

    [Serializable]
    public class GitCommitHistoryModel
    {
        public string Hash { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public DateTime Date { get; set; }
    }

    [Serializable]
    public class NetworkDevice
    {
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string HostName { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
    }

    [Serializable]
    public class NetworkInterfaceInfo
    {
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string NetworkType { get; set; }
        public bool IsActive { get; set; }
    }

    [Serializable]
    public class ExternalService
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ServiceType { get; set; }
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastUsed { get; set; }
    }

    /// <summary>
    /// Результат аналізу AI
    /// </summary>
    [Serializable]
    public class AIAnalysisResult
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string DirectoryPath { get; set; }
        public DateTime AnalysisDate { get; set; }
        public AIAnalysisType Type { get; set; }
        public string Summary { get; set; }
        public List<AIRecommendation> Recommendations { get; set; } = new List<AIRecommendation>();
        public List<StructureViolation> Violations { get; set; } = new List<StructureViolation>();
        public string RawAIResponse { get; set; }
        public bool IsProcessed { get; set; }
    }

    /// <summary>
    /// Рекомендація від AI
    /// </summary>
    [Serializable]
    public class AIRecommendation
    {
        public int Id { get; set; }
        public int AnalysisResultId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public RecommendationType Type { get; set; }
        public string ActionJson { get; set; } // JSON структура дії
        public RecommendationPriority Priority { get; set; }
        public bool IsApplied { get; set; }
        public DateTime? AppliedAt { get; set; }
    }

    /// <summary>
    /// Порушення структури директорій
    /// </summary>
    [Serializable]
    public class StructureViolation
    {
        public int Id { get; set; }
        public int AnalysisResultId { get; set; }
        public string FilePath { get; set; }
        public ViolationType Type { get; set; }
        public string Description { get; set; }
        public string SuggestedPath { get; set; }
        public bool IsResolved { get; set; }
    }

    /// <summary>
    /// AI-згенерована хронологічна дорожня карта
    /// </summary>
    [Serializable]
    public class AIChronologicalRoadmap
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime GeneratedAt { get; set; }
        public List<ChronologicalEvent> Events { get; set; } = new List<ChronologicalEvent>();
        public string AIInsights { get; set; }
    }

    /// <summary>
    /// Хронологічна подія (AI-generated)
    /// </summary>
    [Serializable]
    public class ChronologicalEvent
    {
        public int Id { get; set; }
        public int RoadmapId { get; set; }
        public DateTime EventDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public List<string> RelatedFiles { get; set; } = new List<string>();
        public string AIGeneratedContext { get; set; }
    }

    /// <summary>
    /// AI-згенерована геокарта
    /// </summary>
    [Serializable]
    public class AIGeoRoadmap
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime GeneratedAt { get; set; }
        public List<AIGeoPoint> Points { get; set; } = new List<AIGeoPoint>();
        public string AIAnalysis { get; set; }
    }

    /// <summary>
    /// AI-згенерована геоточка
    /// </summary>
    [Serializable]
    public class AIGeoPoint
    {
        public int Id { get; set; }
        public int RoadmapId { get; set; }
        public string LocationName { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime? EventDate { get; set; }
        public string Description { get; set; }
        public List<string> RelatedFiles { get; set; } = new List<string>();
        public string ExtractedFrom { get; set; } // "filename.pdf", "metadata", тощо
    }

    /// <summary>
    /// Дія для реорганізації файлів
    /// </summary>
    [Serializable]
    public class FileReorganizationAction
    {
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public ReorganizationActionType ActionType { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Запит на AI аналіз
    /// </summary>
    [Serializable]
    public class AIAnalysisRequest
    {
        public int DirectoryId { get; set; }
        public AIAnalysisType AnalysisType { get; set; }
        public bool DeepScan { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Статус AI сервісу
    /// </summary>
    [Serializable]
    public class AIServiceStatus
    {
        public bool IsOllamaRunning { get; set; }
        public string OllamaVersion { get; set; }
        public string ModelName { get; set; }
        public bool IsModelLoaded { get; set; }
        public int TotalAnalyses { get; set; }
        public DateTime? LastAnalysisTime { get; set; }
        public string Status { get; set; }
    }

    // =============== ЕНУМИ v0.4 ===============

    public enum AIAnalysisType
    {
        StructureValidation,    // Перевірка структури директорій
        ChronologicalRoadmap,   // Створення хронологічної карти
        GeoRoadmapGeneration,   // Генерація геокарти
        FileClassification,     // Класифікація файлів
        ProjectInsights,        // Загальний аналіз проекту
        AutoOrganization        // Автоматична організація
    }

    public enum RecommendationType
    {
        CreateFolder,
        MoveFile,
        RenameFile,
        DeleteDuplicate,
        StructureOptimization,
        MetadataEnrichment
    }

    public enum RecommendationPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum ViolationType
    {
        FileInRootDirectory,    // Файл в корені замість в папці
        MissingObjectFolder,    // Відсутня папка об'єкта
        WrongNestingLevel,      // Неправильний рівень вкладеності
        InvalidNaming,          // Неправильне іменування
        Duplicate,              // Дублікат файлу
        OrphanFile              // Файл без батьківської структури
    }

    public enum ReorganizationActionType
    {
        Move,
        CreateFolderAndMove,
        Rename,
        Delete,
        Archive
    }

    [Serializable]
    public class GenerateChronoRoadmapRequest
    {
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    [Serializable]
    public class ApplyReorganizationRequest
    {
        public int AnalysisResultId { get; set; }
        public bool CreateBackup { get; set; }
        public List<int> ViolationIds { get; set; }
    }

    // =============== ОНОВЛЕНІ КОМАНДИ v0.4 ===============

    // Додати до існуючого enum CommandType:

    // AI Analysis
    // StartAIAnalysis,
    // GetAIAnalysisResults,
    // GetAIAnalysisById,
    // ApplyAIRecommendations,
    // GetAIServiceStatus,

    // Chronological Roadmaps
    // GenerateChronologicalRoadmap,
    // GetChronologicalRoadmaps,
    // ExportChronologicalRoadmap,

    // Geo Roadmaps AI
    // GenerateGeoRoadmapFromAI,
    // ExtractLocationsFromFiles,

    // File Reorganization
    // ValidateDirectoryStructure,
    // GetStructureViolations,
    // ApplyReorganization,
    // PreviewReorganization

}