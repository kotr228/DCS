using DocControlService.Data;
using DocControlService.Models;
using DocControlService.Services;
using DocControlService.Shared;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DocControlService
{
    /// <summary>
    /// Головний Windows Service для керування мережевими шарами та версіонуванням директорій
    /// </summary>
    public partial class DocControlWindowsService : ServiceBase
    {
        private readonly DatabaseManager _dbManager;
        private readonly DirectoryRepository _dirRepo;
        private readonly DeviceRepository _deviceRepo;
        private readonly NetworkAccessRepository _accessRepo;
        private readonly NetworkShareService _shareService;
        private readonly DirectoryScanner _scanner;
        private readonly VersionControlFactory _versionFactory;
        private readonly AccessService _accessService;

        private CancellationTokenSource _cancellationTokenSource;
        private Task _pipeServerTask;
        private Task _monitoringTask;
        private Task _versionControlTask;
        private readonly bool _debugMode;
        private DateTime _startTime;
        private DateTime? _lastCommitTime;
        private int _commitIntervalMinutes = 720; // 12 годин за замовчуванням

        private CommitLogRepository _commitLogRepo;
        private ErrorLogRepository _errorLogRepo;
        private AppSettingsRepository _settingsRepo;

        private RoadmapRepository _roadmapRepo;
        private RoadmapService _roadmapService;
        private NetworkDiscoveryService _networkService;
        private ExternalServiceRepository _externalServiceRepo;

        private GeoRoadmapRepository _geoRoadmapRepo;
        private GeoMappingService _geoMappingService;
        private IpFilterService _ipFilterService;

        public DocControlWindowsService(bool debugMode = false)
        {
            _debugMode = debugMode;
            InitializeComponent();

            ServiceName = "DocControlService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;

            // Ініціалізація бази даних та репозиторіїв
            _dbManager = new DatabaseManager();
            _dirRepo = new DirectoryRepository(_dbManager);
            _deviceRepo = new DeviceRepository(_dbManager);
            _accessRepo = new NetworkAccessRepository(_dbManager);
            _shareService = new NetworkShareService();
            _scanner = new DirectoryScanner(_dbManager);
            _versionFactory = new VersionControlFactory(_dirRepo);

            // Ініціалізація геокарт у версії 0.3
            _geoRoadmapRepo = new GeoRoadmapRepository(_dbManager);
            _geoMappingService = new GeoMappingService();
            _ipFilterService = new IpFilterService(_dbManager);

            // Підписуємось на події Git комітів
            foreach (var vcs in _versionFactory.GetAllServices())
            {
                vcs.OnCommitStatusChanged += (path, status, message) =>
                {
                    // Знаходимо ID директорії
                    var dir = _dirRepo.GetAllDirectories().FirstOrDefault(d => d.Browse == path);
                    if (dir != null)
                    {
                        _commitLogRepo.LogCommit(dir.Id, path, status, message);
                    }
                };
            }

            _accessService = new AccessService(_dbManager);

            _commitLogRepo = new CommitLogRepository(_dbManager);
            _errorLogRepo = new ErrorLogRepository(_dbManager);
            _settingsRepo = new AppSettingsRepository(_dbManager);

            _roadmapRepo = new RoadmapRepository(_dbManager);
            _roadmapService = new RoadmapService();
            _networkService = new NetworkDiscoveryService();
            _externalServiceRepo = new ExternalServiceRepository(_dbManager);

            // Ініціалізуємо дефолтні налаштування
            _settingsRepo.InitializeDefaults();
        }

        protected override void OnStart(string[] args)
        {
            _startTime = DateTime.Now;
            _cancellationTokenSource = new CancellationTokenSource();

            Log("Service starting...");

            try
            {
                // Синхронізуємо таблицю доступу
                _accessService.SyncAccessTable();

                // Відновлюємо мережеві шари для активних директорій
                RestoreNetworkShares();

                // Запускаємо Named Pipe сервер для комунікації з UI
                _pipeServerTask = Task.Run(() => RunPipeServer(_cancellationTokenSource.Token));

                // Запускаємо моніторинг доступу
                _monitoringTask = Task.Run(() => MonitorAccessStatus(_cancellationTokenSource.Token));

                // Запускаємо автоматичне версіонування
                _versionControlTask = Task.Run(() => AutoCommitLoop(_cancellationTokenSource.Token));

                Log("Service started successfully");
            }
            catch (Exception ex)
            {
                Log($"Error during service start: {ex.Message}", EventLogEntryType.Error);
                throw;
            }
        }

        protected override void OnStop()
        {
            Log("Service stopping...");

            try
            {
                _cancellationTokenSource?.Cancel();

                // Чекаємо завершення задач
                Task.WaitAll(new[] { _pipeServerTask, _monitoringTask, _versionControlTask }
                    .Where(t => t != null).ToArray(),
                    TimeSpan.FromSeconds(10));

                // Закриваємо всі шари при зупинці сервісу (опціонально)
                // CloseAllNetworkShares();

                Log("Service stopped");
            }
            catch (Exception ex)
            {
                Log($"Error during service stop: {ex.Message}", EventLogEntryType.Warning);
            }
        }

        #region Network Share Management

        private void RestoreNetworkShares()
        {
            Log("Restoring network shares...");

            var directories = _dirRepo.GetAllDirectories();
            foreach (var dir in directories)
            {
                if (_accessRepo.IsDirectoryShared(dir.Id))
                {
                    string shareName = $"DocShare_{dir.Id}";
                    if (_shareService.OpenShare(shareName, dir.Browse))
                    {
                        Log($"Restored share: {shareName} -> {dir.Browse}");
                    }
                    else
                    {
                        Log($"Failed to restore share: {shareName}", EventLogEntryType.Warning);
                    }
                }
            }
        }

        private void CloseAllNetworkShares()
        {
            Log("Closing all network shares...");

            var directories = _dirRepo.GetAllDirectories();
            foreach (var dir in directories)
            {
                string shareName = $"DocShare_{dir.Id}";
                _shareService.CloseShare(shareName);
            }
        }

        #endregion

        #region Monitoring and Version Control

        private async Task MonitorAccessStatus(CancellationToken cancellationToken)
        {
            Log("Starting access monitoring...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);

                    // Перевіряємо статус доступу для кожної директорії
                    var directories = _dirRepo.GetAllDirectories();
                    foreach (var dir in directories)
                    {
                        bool shouldBeShared = _accessRepo.IsDirectoryShared(dir.Id);
                        string shareName = $"DocShare_{dir.Id}";
                        bool isShared = _shareService.ShareExists(shareName);

                        if (shouldBeShared && !isShared)
                        {
                            Log($"Share {shareName} should exist but doesn't. Restoring...");
                            _shareService.OpenShare(shareName, dir.Browse);
                        }
                        else if (!shouldBeShared && isShared)
                        {
                            Log($"Share {shareName} exists but shouldn't. Removing...");
                            _shareService.CloseShare(shareName);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Error in access monitoring: {ex.Message}", EventLogEntryType.Warning);
                }
            }
        }

        private async Task AutoCommitLoop(CancellationToken cancellationToken)
        {
            Log($"Starting auto-commit loop (interval: {_commitIntervalMinutes} minutes)...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_commitIntervalMinutes), cancellationToken);

                    Log("Performing scheduled commit...");
                    PerformCommitForAllDirectories();
                    _lastCommitTime = DateTime.Now;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Error in auto-commit: {ex.Message}", EventLogEntryType.Warning);
                }
            }
        }

        private void PerformCommitForAllDirectories()
        {
            var services = _versionFactory.GetAllServices();
            foreach (var vcs in services)
            {
                try
                {
                    vcs.CommitAll($"Auto-commit at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    Log($"Error committing: {ex.Message}", EventLogEntryType.Warning);
                }
            }
        }

        #endregion

        #region Named Pipe Communication

        private async Task RunPipeServer(CancellationToken cancellationToken)
        {
            Log("Starting Named Pipe server...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (var pipeServer = new NamedPipeServerStream(
                        "DocControlServicePipe",
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous))
                    {
                        await pipeServer.WaitForConnectionAsync(cancellationToken);
                        Log("Client connected to pipe");

                        using (var reader = new StreamReader(pipeServer))
                        using (var writer = new StreamWriter(pipeServer) { AutoFlush = true })
                        {
                            string request = await reader.ReadLineAsync();
                            if (string.IsNullOrEmpty(request))
                            {
                                continue;
                            }

                            Log($"Received command: {request.Substring(0, Math.Min(100, request.Length))}...");

                            var response = ProcessCommand(request);
                            var responseJson = JsonSerializer.Serialize(response);

                            await writer.WriteLineAsync(responseJson);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Pipe server error: {ex.Message}", EventLogEntryType.Warning);
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private ServiceResponse ProcessCommand(string requestJson)
        {
            try
            {
                var command = JsonSerializer.Deserialize<ServiceCommand>(requestJson);

                switch (command.Type)
                {
                    case CommandType.GetDirectories:
                        return HandleGetDirectories();

                    case CommandType.AddDirectory:
                        return HandleAddDirectory(command.Data);

                    case CommandType.RemoveDirectory:
                        return HandleRemoveDirectory(command.Data);

                    case CommandType.UpdateDirectoryName:
                        return HandleUpdateDirectoryName(command.Data);

                    case CommandType.ScanDirectory:
                        return HandleScanDirectory(command.Data);

                    case CommandType.GetDevices:
                        return HandleGetDevices();

                    case CommandType.AddDevice:
                        return HandleAddDevice(command.Data);

                    case CommandType.RemoveDevice:
                        return HandleRemoveDevice(command.Data);

                    case CommandType.GrantAccess:
                        return HandleGrantAccess(command.Data);

                    case CommandType.RevokeAccess:
                        return HandleRevokeAccess(command.Data);

                    case CommandType.OpenAllShares:
                        return HandleOpenAllShares();

                    case CommandType.CloseAllShares:
                        return HandleCloseAllShares();

                    case CommandType.GetStatus:
                        return HandleGetStatus();

                    case CommandType.ForceCommit:
                        return HandleForceCommit();

                    case CommandType.SetCommitInterval:
                        return HandleSetCommitInterval(command.Data);

                    case CommandType.GetCommitLog:
                        return HandleGetCommitLog(command.Data);

                    case CommandType.GetGitHistory:
                        return HandleGetGitHistory(command.Data);

                    case CommandType.RevertToCommit:
                        return HandleRevertToCommit(command.Data);

                    case CommandType.GetErrorLog:
                        return HandleGetErrorLog(command.Data);

                    case CommandType.MarkErrorResolved:
                        return HandleMarkErrorResolved(command.Data);

                    case CommandType.ClearResolvedErrors:
                        return HandleClearResolvedErrors();

                    case CommandType.GetUnresolvedErrorCount:
                        return HandleGetUnresolvedErrorCount();

                    case CommandType.GetSettings:
                        return HandleGetSettings();

                    case CommandType.SaveSettings:
                        return HandleSaveSettings(command.Data);

                    case CommandType.CreateRoadmap:
                        return HandleCreateRoadmap(command.Data);

                    case CommandType.GetRoadmaps:
                        return HandleGetRoadmaps();

                    case CommandType.GetRoadmapById:
                        return HandleGetRoadmapById(command.Data);

                    case CommandType.DeleteRoadmap:
                        return HandleDeleteRoadmap(command.Data);

                    case CommandType.AnalyzeDirectoryForRoadmap:
                        return HandleAnalyzeDirectoryForRoadmap(command.Data);

                    case CommandType.ExportRoadmapAsJson:
                        return HandleExportRoadmapAsJson(command.Data);

                    case CommandType.ScanNetwork:
                        return HandleScanNetwork();

                    case CommandType.GetNetworkInterfaces:
                        return HandleGetNetworkInterfaces();

                    case CommandType.GetExternalServices:
                        return HandleGetExternalServices();

                    case CommandType.AddExternalService:
                        return HandleAddExternalService(command.Data);

                    case CommandType.UpdateExternalService:
                        return HandleUpdateExternalService(command.Data);

                    case CommandType.DeleteExternalService:
                        return HandleDeleteExternalService(command.Data);

                    case CommandType.TestExternalService:
                        return HandleTestExternalService(command.Data);

                    case CommandType.CreateGeoRoadmap:
                        return HandleCreateGeoRoadmap(command.Data);

                    case CommandType.GetGeoRoadmaps:
                        return HandleGetGeoRoadmaps();

                    case CommandType.GetGeoRoadmapById:
                        return HandleGetGeoRoadmapById(command.Data);

                    case CommandType.UpdateGeoRoadmap:
                        return HandleUpdateGeoRoadmap(command.Data);

                    case CommandType.DeleteGeoRoadmap:
                        return HandleDeleteGeoRoadmap(command.Data);

                    case CommandType.AddGeoNode:
                        return HandleAddGeoNode(command.Data);

                    case CommandType.UpdateGeoNode:
                        return HandleUpdateGeoNode(command.Data);

                    case CommandType.DeleteGeoNode:
                        return HandleDeleteGeoNode(command.Data);

                    case CommandType.GetGeoNodesByRoadmap:
                        return HandleGetGeoNodesByRoadmap(command.Data);

                    case CommandType.AddGeoRoute:
                        return HandleAddGeoRoute(command.Data);

                    case CommandType.DeleteGeoRoute:
                        return HandleDeleteGeoRoute(command.Data);

                    case CommandType.AddGeoArea:
                        return HandleAddGeoArea(command.Data);

                    case CommandType.DeleteGeoArea:
                        return HandleDeleteGeoArea(command.Data);

                    case CommandType.GetGeoRoadmapTemplates:
                        return HandleGetGeoRoadmapTemplates();

                    case CommandType.CreateFromTemplate:
                        return HandleCreateFromTemplate(command.Data);

                    case CommandType.SaveAsTemplate:
                        return HandleSaveAsTemplate(command.Data);

                    case CommandType.GeocodeAddress:
                        return HandleGeocodeAddress(command.Data);

                    case CommandType.ReverseGeocode:
                        return HandleReverseGeocode(command.Data);

                    case CommandType.GetIpFilterRules:
                        return HandleGetIpFilterRules();

                    case CommandType.AddIpFilterRule:
                        return HandleAddIpFilterRule(command.Data);

                    case CommandType.UpdateIpFilterRule:
                        return HandleUpdateIpFilterRule(command.Data);

                    case CommandType.DeleteIpFilterRule:
                        return HandleDeleteIpFilterRule(command.Data);

                    case CommandType.TestIpAccess:
                        return HandleTestIpAccess(command.Data);

                    default:
                        return new ServiceResponse
                        {
                            Success = false,
                            Message = "Unknown command type"
                        };
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing command: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        #endregion

        #region Command Handlers

        private ServiceResponse HandleGetDirectories()
        {
            var directories = _dirRepo.GetAllDirectories();
            var result = directories.Select(d => new DirectoryWithAccessModel
            {
                Id = d.Id,
                Name = d.Name,
                Browse = d.Browse,
                IsShared = _accessRepo.IsDirectoryShared(d.Id),
                AllowedDevices = _accessRepo.GetAllowedDevicesForDirectory(d.Id)
            }).ToList();

            return new ServiceResponse
            {
                Success = true,
                Data = JsonSerializer.Serialize(result)
            };
        }

        private ServiceResponse HandleAddDirectory(string data)
        {
            var request = JsonSerializer.Deserialize<AddDirectoryRequest>(data);

            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Path))
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Name and Path are required"
                };
            }

            if (!Directory.Exists(request.Path))
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = $"Directory does not exist: {request.Path}"
                };
            }

            // Додаємо директорію
            int dirId = _dirRepo.AddDirectory(request.Name, request.Path);

            // Сканування директорії для заповнення залежних таблиць
            _scanner.ScanDirectoryById(dirId);

            // Ініціалізуємо Git репозиторій
            var vcs = _versionFactory.GetServiceFor(dirId);

            Log($"Added directory: {request.Name} (id={dirId})");

            return new ServiceResponse
            {
                Success = true,
                Message = "Directory added successfully",
                Data = dirId.ToString()
            };
        }

        private ServiceResponse HandleRemoveDirectory(string data)
        {
            int dirId = int.Parse(data);
            var dir = _dirRepo.GetById(dirId);

            if (dir == null)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Directory not found"
                };
            }

            // Закриваємо шар якщо він відкритий
            string shareName = $"DocShare_{dirId}";
            if (_shareService.ShareExists(shareName))
            {
                _shareService.CloseShare(shareName);
            }

            // Видаляємо всі записи доступу
            _accessRepo.SetDirectoryAccessStatus(dirId, false);

            // Видаляємо директорію
            bool deleted = _dirRepo.DeleteDirectory(dirId);

            if (deleted)
            {
                Log($"Removed directory: {dir.Name} (id={dirId})");
                return new ServiceResponse
                {
                    Success = true,
                    Message = "Directory removed successfully"
                };
            }

            return new ServiceResponse
            {
                Success = false,
                Message = "Failed to remove directory"
            };
        }

        private ServiceResponse HandleUpdateDirectoryName(string data)
        {
            var request = JsonSerializer.Deserialize<UpdateDirectoryNameRequest>(data);
            var dir = _dirRepo.GetById(request.DirectoryId);

            if (dir == null)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Directory not found"
                };
            }

            // Оновлюємо ім'я директорії (потрібно додати метод в DirectoryRepository)
            // Поки що створюємо простий UPDATE
            using (var conn = _dbManager.GetConnection())
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE directory SET Name = @name WHERE id = @id;";
                cmd.Parameters.AddWithValue("@name", request.NewName);
                cmd.Parameters.AddWithValue("@id", request.DirectoryId);
                cmd.ExecuteNonQuery();
            }

            Log($"Updated directory name: {dir.Name} -> {request.NewName}");

            return new ServiceResponse
            {
                Success = true,
                Message = "Directory name updated successfully"
            };
        }

        private ServiceResponse HandleScanDirectory(string data)
        {
            int dirId = int.Parse(data);
            var dir = _dirRepo.GetById(dirId);

            if (dir == null)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Directory not found"
                };
            }

            _scanner.ScanDirectoryById(dirId);

            Log($"Scanned directory: {dir.Name} (id={dirId})");

            return new ServiceResponse
            {
                Success = true,
                Message = "Directory scanned successfully"
            };
        }

        private ServiceResponse HandleGetDevices()
        {
            var devices = _deviceRepo.GetAllDevices();
            return new ServiceResponse
            {
                Success = true,
                Data = JsonSerializer.Serialize(devices)
            };
        }

        private ServiceResponse HandleAddDevice(string data)
        {
            var device = JsonSerializer.Deserialize<DeviceModel>(data);
            int deviceId = _deviceRepo.AddDevice(device.Name, device.Access);

            Log($"Added device: {device.Name} (id={deviceId})");

            return new ServiceResponse
            {
                Success = true,
                Message = "Device added successfully",
                Data = deviceId.ToString()
            };
        }

        private ServiceResponse HandleRemoveDevice(string data)
        {
            int deviceId = int.Parse(data);
            bool deleted = _deviceRepo.DeleteDevice(deviceId);

            if (deleted)
            {
                Log($"Removed device (id={deviceId})");
                return new ServiceResponse
                {
                    Success = true,
                    Message = "Device removed successfully"
                };
            }

            return new ServiceResponse
            {
                Success = false,
                Message = "Failed to remove device"
            };
        }

        private ServiceResponse HandleGrantAccess(string data)
        {
            var request = JsonSerializer.Deserialize<AccessRequest>(data);

            // Надаємо доступ в БД
            int accessId = _accessRepo.GrantAccess(request.DirectoryId, request.DeviceId);

            // Відкриваємо мережевий шар
            var dir = _dirRepo.GetById(request.DirectoryId);
            string shareName = $"DocShare_{request.DirectoryId}";
            _shareService.OpenShare(shareName, dir.Browse);

            Log($"Granted access: Directory {request.DirectoryId} -> Device {request.DeviceId}");

            return new ServiceResponse
            {
                Success = true,
                Message = "Access granted successfully"
            };
        }

        private ServiceResponse HandleRevokeAccess(string data)
        {
            var request = JsonSerializer.Deserialize<AccessRequest>(data);

            bool revoked = _accessRepo.RevokeAccess(request.DirectoryId, request.DeviceId);

            // Перевіряємо чи є ще активні доступи до цієї директорії
            if (!_accessRepo.IsDirectoryShared(request.DirectoryId))
            {
                string shareName = $"DocShare_{request.DirectoryId}";
                _shareService.CloseShare(shareName);
            }

            Log($"Revoked access: Directory {request.DirectoryId} -> Device {request.DeviceId}");

            return new ServiceResponse
            {
                Success = revoked,
                Message = revoked ? "Access revoked successfully" : "Failed to revoke access"
            };
        }

        private ServiceResponse HandleOpenAllShares()
        {
            _accessService.OpenAll();
            Log("Opened all network shares");

            return new ServiceResponse
            {
                Success = true,
                Message = "All shares opened successfully"
            };
        }

        private ServiceResponse HandleCloseAllShares()
        {
            _accessService.CloseAll();
            Log("Closed all network shares");

            return new ServiceResponse
            {
                Success = true,
                Message = "All shares closed successfully"
            };
        }

        private ServiceResponse HandleGetStatus()
        {
            var status = new ServiceStatus
            {
                IsRunning = true,
                TotalDirectories = _dirRepo.GetAllDirectories().Count,
                SharedDirectories = _dirRepo.GetAllDirectories().Count(d => _accessRepo.IsDirectoryShared(d.Id)),
                RegisteredDevices = _deviceRepo.GetAllDevices().Count,
                StartTime = _startTime,
                LastCommitTime = _lastCommitTime,
                CommitIntervalMinutes = _commitIntervalMinutes,
                UnresolvedErrors = _errorLogRepo.GetUnresolvedCount()
            };

            return new ServiceResponse
            {
                Success = true,
                Data = JsonSerializer.Serialize(status)
            };
        }

        private ServiceResponse HandleForceCommit()
        {
            PerformCommitForAllDirectories();
            _lastCommitTime = DateTime.Now;

            Log("Forced commit executed");

            return new ServiceResponse
            {
                Success = true,
                Message = "Commit performed successfully"
            };
        }

        private ServiceResponse HandleSetCommitInterval(string data)
        {
            int minutes = int.Parse(data);

            if (minutes < 1)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Interval must be at least 1 minute"
                };
            }

            _commitIntervalMinutes = minutes;

            Log($"Commit interval set to {minutes} minutes");

            return new ServiceResponse
            {
                Success = true,
                Message = $"Commit interval set to {minutes} minutes"
            };
        }

        #endregion

        #region Logging

        private void Log(string message, EventLogEntryType type = EventLogEntryType.Information)
        {
            var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            if (_debugMode)
            {
                Console.WriteLine(logMessage);
            }
            else
            {
                try
                {
                    EventLog.WriteEntry(ServiceName, logMessage, type);
                }
                catch
                {
                    // Ігноруємо помилки логування
                }
            }
        }

        #endregion

        #region Service Infrastructure

        private void InitializeComponent()
        {
            this.ServiceName = "DocControlService";
        }

        /// <summary>
        /// Метод для запуску в Debug режимі через консоль
        /// </summary>
        public void StartDebug(string[] args)
        {
            Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
            Console.WriteLine("║   DocControl Service - DEBUG MODE                     ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
            Console.WriteLine();

            OnStart(args);

            Console.WriteLine();
            Console.WriteLine("Service is running. Press 'Q' to stop, 'S' for status...");
            Console.WriteLine();

            while (true)
            {
                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Q)
                {
                    Console.WriteLine("Stopping service...");
                    break;
                }
                else if (key.Key == ConsoleKey.S)
                {
                    ShowStatus();
                }
                else if (key.Key == ConsoleKey.C)
                {
                    Console.WriteLine("Forcing commit...");
                    PerformCommitForAllDirectories();
                }
                else if (key.Key == ConsoleKey.H)
                {
                    ShowHelp();
                }
            }

            OnStop();
            Console.WriteLine("Service stopped. Press any key to exit...");
            Console.ReadKey();
        }

        private void ShowStatus()
        {
            Console.WriteLine();
            Console.WriteLine("═══ SERVICE STATUS ═══");
            Console.WriteLine($"Running since: {_startTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Uptime: {DateTime.Now - _startTime:hh\\:mm\\:ss}");
            Console.WriteLine($"Total directories: {_dirRepo.GetAllDirectories().Count}");
            Console.WriteLine($"Shared directories: {_dirRepo.GetAllDirectories().Count(d => _accessRepo.IsDirectoryShared(d.Id))}");
            Console.WriteLine($"Registered devices: {_deviceRepo.GetAllDevices().Count}");
            Console.WriteLine($"Last commit: {_lastCommitTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");
            Console.WriteLine($"Commit interval: {_commitIntervalMinutes} minutes");
            Console.WriteLine("══════════════════════");
            Console.WriteLine();
        }

        private void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("═══ AVAILABLE COMMANDS ═══");
            Console.WriteLine("Q - Quit (stop service)");
            Console.WriteLine("S - Show status");
            Console.WriteLine("C - Force commit now");
            Console.WriteLine("H - Show this help");
            Console.WriteLine("═══════════════════════════");
            Console.WriteLine();
        }

        #endregion

        #region New Command Handlers

        private ServiceResponse HandleGetCommitLog(string data)
        {
            try
            {
                List<CommitStatusLog> logs;
                if (data.Contains(","))
                {
                    var parts = data.Split(',');
                    int dirId = int.Parse(parts[0]);
                    int limit = int.Parse(parts[1]);
                    logs = _commitLogRepo.GetLogsByDirectory(dirId, limit);
                }
                else
                {
                    int limit = int.Parse(data);
                    logs = _commitLogRepo.GetRecentLogs(limit);
                }

                var models = logs.Select(l => new CommitLogModel
                {
                    Id = l.Id,
                    DirectoryId = l.DirectoryId,
                    DirectoryPath = l.DirectoryPath,
                    Status = l.Status,
                    Message = l.Message,
                    Timestamp = l.Timestamp
                }).ToList();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(models)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetCommitLog: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetGitHistory(string data)
        {
            try
            {
                int dirId = int.Parse(data);
                var vcs = _versionFactory.GetServiceFor(dirId);

                if (vcs == null)
                    return new ServiceResponse { Success = false, Message = "Git репозиторій не знайдено" };

                var history = vcs.GetCommitHistory(50);
                var models = history.Select(h => new GitCommitHistoryModel
                {
                    Hash = h.Hash,
                    Message = h.Message,
                    Author = h.Author,
                    Date = h.Date
                }).ToList();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(models)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGitHistory: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleRevertToCommit(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RevertRequest>(data);
                var vcs = _versionFactory.GetServiceFor(request.DirectoryId);

                if (vcs == null)
                    return new ServiceResponse { Success = false, Message = "Git репозиторій не знайдено" };

                bool success = vcs.RevertToCommit(request.CommitHash);
                return new ServiceResponse
                {
                    Success = success,
                    Message = success ? "Відкат виконано" : "Помилка відкату"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleRevertToCommit: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetErrorLog(string data)
        {
            try
            {
                bool onlyUnresolved = bool.Parse(data);
                var errors = _errorLogRepo.GetRecentErrors(100, onlyUnresolved);

                var models = errors.Select(e => new ErrorLogModel
                {
                    Id = e.Id,
                    ErrorType = e.ErrorType,
                    ErrorMessage = e.ErrorMessage,
                    UserFriendlyMessage = e.UserFriendlyMessage,
                    StackTrace = e.StackTrace,
                    Timestamp = e.Timestamp,
                    IsResolved = e.IsResolved
                }).ToList();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(models)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetErrorLog: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleMarkErrorResolved(string data)
        {
            try
            {
                int errorId = int.Parse(data);
                _errorLogRepo.MarkAsResolved(errorId);

                return new ServiceResponse { Success = true, Message = "Помилку позначено як вирішену" };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleMarkErrorResolved: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleClearResolvedErrors()
        {
            try
            {
                _errorLogRepo.ClearResolvedErrors();
                return new ServiceResponse { Success = true, Message = "Вирішені помилки очищено" };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleClearResolvedErrors: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetUnresolvedErrorCount()
        {
            try
            {
                int count = _errorLogRepo.GetUnresolvedCount();
                return new ServiceResponse { Success = true, Data = count.ToString() };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetUnresolvedErrorCount: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    AutoShareOnAdd = _settingsRepo.GetBoolSetting("AutoShareOnAdd", false),
                    EnableUpdateNotifications = _settingsRepo.GetBoolSetting("EnableUpdateNotifications", true),
                    CommitIntervalMinutes = _settingsRepo.GetIntSetting("CommitIntervalMinutes", 720)
                };

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(settings)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetSettings: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleSaveSettings(string data)
        {
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(data);

                _settingsRepo.SetBoolSetting("AutoShareOnAdd", settings.AutoShareOnAdd);
                _settingsRepo.SetBoolSetting("EnableUpdateNotifications", settings.EnableUpdateNotifications);
                _settingsRepo.SetIntSetting("CommitIntervalMinutes", settings.CommitIntervalMinutes);

                // Оновлюємо інтервал комітів
                _commitIntervalMinutes = settings.CommitIntervalMinutes;

                return new ServiceResponse { Success = true, Message = "Налаштування збережено" };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleSaveSettings: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Roadmap Handlers

        private ServiceResponse HandleCreateRoadmap(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
                int directoryId = Convert.ToInt32(request["DirectoryId"].ToString());
                string name = request["Name"].ToString();
                string description = request["Description"].ToString();

                var eventsJson = request["Events"].ToString();
                var events = JsonSerializer.Deserialize<List<RoadmapEvent>>(eventsJson);

                int roadmapId = _roadmapRepo.CreateRoadmap(directoryId, name, description, events);

                Log($"Створено дорожню карту: {name} (ID: {roadmapId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = roadmapId.ToString(),
                    Message = "Roadmap створено успішно"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleCreateRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetRoadmaps()
        {
            try
            {
                var roadmaps = _roadmapRepo.GetAllRoadmaps();
                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(roadmaps)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetRoadmaps: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetRoadmapById(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                var roadmap = _roadmapRepo.GetRoadmapById(roadmapId);

                if (roadmap == null)
                    return new ServiceResponse { Success = false, Message = "Roadmap не знайдено" };

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(roadmap)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetRoadmapById: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteRoadmap(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                bool deleted = _roadmapRepo.DeleteRoadmap(roadmapId);

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Roadmap видалено" : "Roadmap не знайдено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleAnalyzeDirectoryForRoadmap(string data)
        {
            try
            {
                int directoryId = int.Parse(data);
                var dir = _dirRepo.GetById(directoryId);

                if (dir == null)
                    return new ServiceResponse { Success = false, Message = "Директорію не знайдено" };

                Log($"Аналіз директорії для roadmap: {dir.Browse}");
                var events = _roadmapService.AnalyzeDirectory(dir.Browse);

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(events),
                    Message = $"Знайдено {events.Count} подій"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAnalyzeDirectoryForRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleExportRoadmapAsJson(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                var roadmap = _roadmapRepo.GetRoadmapById(roadmapId);

                if (roadmap == null)
                    return new ServiceResponse { Success = false, Message = "Roadmap не знайдено" };

                string json = _roadmapService.ExportToJson(roadmap);

                return new ServiceResponse
                {
                    Success = true,
                    Data = json,
                    Message = "Експорт успішний"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleExportRoadmapAsJson: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Network Discovery Handlers

        private ServiceResponse HandleScanNetwork()
        {
            try
            {
                Log("Запуск сканування мережі...");
                var devices = _networkService.ScanNetworkAsync().Result;

                Log($"Знайдено {devices.Count} пристроїв у мережі");

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(devices),
                    Message = $"Знайдено {devices.Count} пристроїв"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleScanNetwork: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetNetworkInterfaces()
        {
            try
            {
                var interfaces = _networkService.GetNetworkInterfaces();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(interfaces)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetNetworkInterfaces: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region External Services Handlers

        private ServiceResponse HandleGetExternalServices()
        {
            try
            {
                var services = _externalServiceRepo.GetAllServices();
                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(services)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetExternalServices: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleAddExternalService(string data)
        {
            try
            {
                var service = JsonSerializer.Deserialize<ExternalService>(data);

                int serviceId = _externalServiceRepo.AddService(
                    service.Name,
                    service.ServiceType,
                    service.Url,
                    service.ApiKey,
                    service.IsActive);

                Log($"Додано зовнішній сервіс: {service.Name}");

                return new ServiceResponse
                {
                    Success = true,
                    Data = serviceId.ToString(),
                    Message = "Сервіс додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddExternalService: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateExternalService(string data)
        {
            try
            {
                var service = JsonSerializer.Deserialize<ExternalService>(data);

                bool updated = _externalServiceRepo.UpdateService(
                    service.Id,
                    service.Name,
                    service.ServiceType,
                    service.Url,
                    service.ApiKey,
                    service.IsActive);

                return new ServiceResponse
                {
                    Success = updated,
                    Message = updated ? "Сервіс оновлено" : "Сервіс не знайдено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateExternalService: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteExternalService(string data)
        {
            try
            {
                int serviceId = int.Parse(data);
                bool deleted = _externalServiceRepo.DeleteService(serviceId);

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Сервіс видалено" : "Сервіс не знайдено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteExternalService: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleTestExternalService(string data)
        {
            try
            {
                int serviceId = int.Parse(data);
                var service = _externalServiceRepo.GetServiceById(serviceId);

                if (service == null)
                    return new ServiceResponse { Success = false, Message = "Сервіс не знайдено" };

                // Простий тест доступності URL
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var response = client.GetAsync(service.Url).Result;
                bool success = response.IsSuccessStatusCode;

                if (success)
                {
                    _externalServiceRepo.UpdateLastUsed(serviceId);
                }

                return new ServiceResponse
                {
                    Success = success,
                    Message = success ? $"Сервіс доступний (HTTP {(int)response.StatusCode})" : $"Помилка: HTTP {(int)response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleTestExternalService: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = $"Помилка з'єднання: {ex.Message}" };
            }
        }

        #endregion

        #region Geo Roadmap Handlers (v0.3)

        private ServiceResponse HandleCreateGeoRoadmap(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<CreateGeoRoadmapRequest>(data);
                int roadmapId = _geoRoadmapRepo.CreateGeoRoadmap(request, "System");

                Log($"Створено геокарту: {request.Name} (ID: {roadmapId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = roadmapId.ToString(),
                    Message = "Геокарту створено успішно"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleCreateGeoRoadmap: {ex.Message}", EventLogEntryType.Error);
                _errorLogRepo.LogError("CreateGeoRoadmap", ex.Message,
                    "Не вдалося створити геокарту", ex.StackTrace);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetGeoRoadmaps()
        {
            try
            {
                var roadmaps = _geoRoadmapRepo.GetAllGeoRoadmaps();
                Log($"Завантажено {roadmaps.Count} геокарт");

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(roadmaps)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGeoRoadmaps: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetGeoRoadmapById(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                var roadmap = _geoRoadmapRepo.GetGeoRoadmapById(roadmapId);

                if (roadmap == null)
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Геокарту не знайдено"
                    };
                }

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(roadmap)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGeoRoadmapById: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateGeoRoadmap(string data)
        {
            try
            {
                var roadmap = JsonSerializer.Deserialize<GeoRoadmap>(data);
                bool updated = _geoRoadmapRepo.UpdateGeoRoadmap(roadmap);

                if (updated)
                    Log($"Оновлено геокарту: {roadmap.Name} (ID: {roadmap.Id})");

                return new ServiceResponse
                {
                    Success = updated,
                    Message = updated ? "Геокарту оновлено" : "Не вдалося оновити геокарту"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateGeoRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteGeoRoadmap(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                bool deleted = _geoRoadmapRepo.DeleteGeoRoadmap(roadmapId);

                if (deleted)
                    Log($"Видалено геокарту ID: {roadmapId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Геокарту видалено" : "Не вдалося видалити геокарту"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteGeoRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Geo Node Handlers

        private ServiceResponse HandleAddGeoNode(string data)
        {
            try
            {
                var node = JsonSerializer.Deserialize<GeoRoadmapNode>(data);
                int nodeId = _geoRoadmapRepo.AddNode(node);

                Log($"Додано вузол: {node.Title} (ID: {nodeId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = nodeId.ToString(),
                    Message = "Вузол додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddGeoNode: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateGeoNode(string data)
        {
            try
            {
                var node = JsonSerializer.Deserialize<GeoRoadmapNode>(data);
                bool updated = _geoRoadmapRepo.UpdateNode(node);

                if (updated)
                    Log($"Оновлено вузол: {node.Title} (ID: {node.Id})");

                return new ServiceResponse
                {
                    Success = updated,
                    Message = updated ? "Вузол оновлено" : "Не вдалося оновити вузол"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateGeoNode: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteGeoNode(string data)
        {
            try
            {
                int nodeId = int.Parse(data);
                bool deleted = _geoRoadmapRepo.DeleteNode(nodeId);

                if (deleted)
                    Log($"Видалено вузол ID: {nodeId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Вузол видалено" : "Не вдалося видалити вузол"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteGeoNode: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetGeoNodesByRoadmap(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                var nodes = _geoRoadmapRepo.GetNodesByRoadmap(roadmapId);

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(nodes)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGeoNodesByRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Geo Route Handlers

        private ServiceResponse HandleAddGeoRoute(string data)
        {
            try
            {
                var route = JsonSerializer.Deserialize<GeoRoadmapRoute>(data);
                int routeId = _geoRoadmapRepo.AddRoute(route);

                Log($"Додано маршрут ID: {routeId} (від {route.FromNodeId} до {route.ToNodeId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = routeId.ToString(),
                    Message = "Маршрут додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddGeoRoute: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateGeoRoute(string data)
        {
            try
            {
                var route = JsonSerializer.Deserialize<GeoRoadmapRoute>(data);
                // Додати метод UpdateRoute в GeoRoadmapRepository якщо потрібно

                return new ServiceResponse
                {
                    Success = true,
                    Message = "Маршрут оновлено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateGeoRoute: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteGeoRoute(string data)
        {
            try
            {
                int routeId = int.Parse(data);
                bool deleted = _geoRoadmapRepo.DeleteRoute(routeId);

                if (deleted)
                    Log($"Видалено маршрут ID: {routeId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Маршрут видалено" : "Не вдалося видалити маршрут"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteGeoRoute: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Geo Area Handlers

        private ServiceResponse HandleAddGeoArea(string data)
        {
            try
            {
                var area = JsonSerializer.Deserialize<GeoRoadmapArea>(data);
                int areaId = _geoRoadmapRepo.AddArea(area);

                Log($"Додано область: {area.Name} (ID: {areaId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = areaId.ToString(),
                    Message = "Область додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddGeoArea: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateGeoArea(string data)
        {
            try
            {
                var area = JsonSerializer.Deserialize<GeoRoadmapArea>(data);
                // Додати метод UpdateArea в GeoRoadmapRepository якщо потрібно

                return new ServiceResponse
                {
                    Success = true,
                    Message = "Область оновлено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateGeoArea: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteGeoArea(string data)
        {
            try
            {
                int areaId = int.Parse(data);
                bool deleted = _geoRoadmapRepo.DeleteArea(areaId);

                if (deleted)
                    Log($"Видалено область ID: {areaId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Область видалено" : "Не вдалося видалити область"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteGeoArea: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Template Handlers

        private ServiceResponse HandleGetGeoRoadmapTemplates()
        {
            try
            {
                var templates = _geoRoadmapRepo.GetAllTemplates();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(templates)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGeoRoadmapTemplates: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleCreateFromTemplate(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
                int templateId = Convert.ToInt32(request["TemplateId"].ToString());
                int directoryId = Convert.ToInt32(request["DirectoryId"].ToString());
                string name = request["Name"].ToString();

                var template = _geoRoadmapRepo.GetAllTemplates()
                    .FirstOrDefault(t => t.Id == templateId);

                if (template == null)
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Шаблон не знайдено"
                    };
                }

                // Створюємо нову геокарту на основі шаблону
                var templateData = JsonSerializer.Deserialize<Dictionary<string, object>>(template.TemplateJson);

                var createRequest = new CreateGeoRoadmapRequest
                {
                    DirectoryId = directoryId,
                    Name = name,
                    Description = $"Створено з шаблону: {template.Name}",
                    MapProvider = MapProvider.OpenStreetMap,
                    CenterLatitude = 50.4501,
                    CenterLongitude = 30.5234,
                    ZoomLevel = 10
                };

                int roadmapId = _geoRoadmapRepo.CreateGeoRoadmap(createRequest, "System");

                Log($"Створено геокарту з шаблону {template.Name}: {name} (ID: {roadmapId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = roadmapId.ToString(),
                    Message = "Геокарту створено з шаблону"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleCreateFromTemplate: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleSaveAsTemplate(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
                int roadmapId = Convert.ToInt32(request["RoadmapId"].ToString());
                string name = request["Name"].ToString();
                string description = request["Description"].ToString();
                string category = request["Category"].ToString();

                var roadmap = _geoRoadmapRepo.GetGeoRoadmapById(roadmapId);

                if (roadmap == null)
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Геокарту не знайдено"
                    };
                }

                int templateId = _geoRoadmapRepo.SaveAsTemplate(name, description, category, roadmap);

                Log($"Збережено геокарту як шаблон: {name} (ID: {templateId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = templateId.ToString(),
                    Message = "Шаблон збережено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleSaveAsTemplate: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Geocoding Handlers

        private ServiceResponse HandleGeocodeAddress(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<GeocodeRequest>(data);

                Log($"Геокодування адреси: {request.Address}");

                var result = _geoMappingService.GeocodeAddressAsync(request.Address).Result;

                if (result.Success)
                    Log($"Знайдено координати: {result.Latitude}, {result.Longitude}");

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(result)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGeocodeAddress: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleReverseGeocode(string data)
        {
            try
            {
                var coords = JsonSerializer.Deserialize<Dictionary<string, double>>(data);
                double latitude = coords["Latitude"];
                double longitude = coords["Longitude"];

                Log($"Зворотне геокодування: {latitude}, {longitude}");

                var address = _geoMappingService.ReverseGeocodeAsync(latitude, longitude).Result;

                Log($"Знайдено адресу: {address}");

                return new ServiceResponse
                {
                    Success = true,
                    Data = address
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleReverseGeocode: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleCalculateRoute(string data)
        {
            try
            {
                var nodes = JsonSerializer.Deserialize<List<GeoRoadmapNode>>(data);

                var optimizedRoute = _geoMappingService.CalculateOptimalRoute(nodes);

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(optimizedRoute),
                    Message = "Маршрут оптимізовано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleCalculateRoute: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region IP Filter Handlers

        private ServiceResponse HandleGetIpFilterRules()
        {
            try
            {
                var rules = _ipFilterService.GetAllRules();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(rules)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetIpFilterRules: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleAddIpFilterRule(string data)
        {
            try
            {
                var rule = JsonSerializer.Deserialize<IpFilterRule>(data);
                int ruleId = _ipFilterService.AddRule(rule);

                Log($"Додано правило IP фільтрації: {rule.RuleName} (ID: {ruleId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = ruleId.ToString(),
                    Message = "Правило додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddIpFilterRule: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateIpFilterRule(string data)
        {
            try
            {
                var rule = JsonSerializer.Deserialize<IpFilterRule>(data);
                bool updated = _ipFilterService.UpdateRule(rule);

                if (updated)
                    Log($"Оновлено правило IP фільтрації: {rule.RuleName} (ID: {rule.Id})");

                return new ServiceResponse
                {
                    Success = updated,
                    Message = updated ? "Правило оновлено" : "Не вдалося оновити правило"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateIpFilterRule: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteIpFilterRule(string data)
        {
            try
            {
                int ruleId = int.Parse(data);
                bool deleted = _ipFilterService.DeleteRule(ruleId);

                if (deleted)
                    Log($"Видалено правило IP фільтрації ID: {ruleId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Правило видалено" : "Не вдалося видалити правило"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteIpFilterRule: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleTestIpAccess(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
                string ipAddress = request["IpAddress"].ToString();

                int? directoryId = request.ContainsKey("DirectoryId") && request["DirectoryId"] != null
                    ? int.Parse(request["DirectoryId"].ToString())
                    : null;

                int? geoRoadmapId = request.ContainsKey("GeoRoadmapId") && request["GeoRoadmapId"] != null
                    ? int.Parse(request["GeoRoadmapId"].ToString())
                    : null;

                bool allowed = _ipFilterService.CheckAccess(ipAddress, directoryId, geoRoadmapId);

                Log($"Перевірка доступу IP {ipAddress}: {(allowed ? "Дозволено" : "Заблоковано")}");

                return new ServiceResponse
                {
                    Success = true,
                    Data = allowed.ToString(),
                    Message = allowed ? "Доступ дозволено" : "Доступ заблоковано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleTestIpAccess: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion
    }
}