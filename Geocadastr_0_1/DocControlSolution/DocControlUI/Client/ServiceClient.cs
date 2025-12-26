using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ServiceCommandType = DocControlService.Shared.CommandType;

namespace DocControlService.Client
{
    /// <summary>
    /// Клієнт для комунікації з DocControl Service через Named Pipes
    /// </summary>
    public class DocControlServiceClient : IDisposable
    {
        private const string PipeName = "DocControlServicePipe";
        private const int TimeoutMs = 43200000;

        /// <summary>
        /// Відправка команди до сервісу
        /// </summary>
        private async Task<ServiceResponse> SendCommandAsync(ServiceCommand command)
        {
            NamedPipeClientStream pipeClient = null;
            StreamWriter writer = null;
            StreamReader reader = null;

            try
            {
                System.Diagnostics.Debug.WriteLine($"📨 Sending command: {command.Type}");

                pipeClient = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough);

                // Підключаємось
                var connectTask = pipeClient.ConnectAsync(TimeoutMs);
                if (await Task.WhenAny(connectTask, Task.Delay(TimeoutMs)) != connectTask)
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Тайм-аут підключення до сервісу"
                    };
                }

                System.Diagnostics.Debug.WriteLine("✅ Connected to pipe");

                // Пишемо запит
                writer = new StreamWriter(pipeClient, Encoding.UTF8, 1024, true) { AutoFlush = true };
                var requestJson = JsonSerializer.Serialize(command);

                await writer.WriteLineAsync(requestJson);
                await writer.FlushAsync();
                System.Diagnostics.Debug.WriteLine($"📤 Request sent: {requestJson.Length} chars");

                // КРИТИЧНО: закриваємо writer щоб сервер отримав повідомлення
                writer.Dispose();
                writer = null;

                // Даємо час серверу обробити
                await Task.Delay(100);

                // Читаємо відповідь
                reader = new StreamReader(pipeClient, Encoding.UTF8, false, 1024, true);

                System.Diagnostics.Debug.WriteLine("⏳ Waiting for response...");

                var readTask = reader.ReadLineAsync();
                var timeoutTask = Task.Delay(TimeoutMs);
                var completedTask = await Task.WhenAny(readTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Response timeout");
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Тайм-аут очікування відповіді від сервісу. Перевірте логи сервісу."
                    };
                }

                var responseJson = await readTask;

                if (string.IsNullOrEmpty(responseJson))
                {
                    System.Diagnostics.Debug.WriteLine("❌ Empty response");
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Отримано порожню відповідь від сервісу"
                    };
                }

                System.Diagnostics.Debug.WriteLine($"✅ Response received: {responseJson.Substring(0, Math.Min(100, responseJson.Length))}");

                var response = JsonSerializer.Deserialize<ServiceResponse>(responseJson);
                return response;
            }
            catch (TimeoutException)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Тайм-аут підключення до сервісу"
                };
            }
            catch (IOException ex)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = $"Помилка читання/запису: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = $"Помилка: {ex.Message}"
                };
            }
            finally
            {
                try
                {
                    reader?.Dispose();
                    writer?.Dispose();

                    if (pipeClient != null)
                    {
                        await Task.Delay(50); // Даємо час на flush
                        pipeClient.Dispose();
                    }
                }
                catch { }
            }
        }

        #region Directory Operations

        public async Task<List<DirectoryWithAccessModel>> GetDirectoriesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetDirectories
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<DirectoryWithAccessModel>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<int> AddDirectoryAsync(string name, string path)
        {
            var request = new AddDirectoryRequest
            {
                Name = name,
                Path = path
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.AddDirectory,
                Data = JsonSerializer.Serialize(request)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> RemoveDirectoryAsync(int directoryId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.RemoveDirectory,
                Data = directoryId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> UpdateDirectoryNameAsync(int directoryId, string newName)
        {
            var request = new UpdateDirectoryNameRequest
            {
                DirectoryId = directoryId,
                NewName = newName
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.UpdateDirectoryName,
                Data = JsonSerializer.Serialize(request)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> ScanDirectoryAsync(int directoryId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.ScanDirectory,
                Data = directoryId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> UpdateDirectoryAsync(int directoryId, string newName, string newPath)
        {
            var request = new UpdateDirectoryRequest
            {
                DirectoryId = directoryId,
                NewName = newName,
                NewPath = newPath
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.UpdateDirectory,
                Data = JsonSerializer.Serialize(request)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<List<DirectoryModel>> SearchDirectoriesAsync(string searchQuery)
        {
            var request = new SearchDirectoriesRequest
            {
                SearchQuery = searchQuery
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.SearchDirectories,
                Data = JsonSerializer.Serialize(request)
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<DirectoryModel>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<DirectoryStatisticsModel> GetDirectoryStatisticsAsync(int directoryId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetDirectoryStatistics,
                Data = directoryId.ToString()
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<DirectoryStatisticsModel>(response.Data);
            }

            throw new Exception(response.Message);
        }

        #endregion

        #region Device Operations

        public async Task<List<DeviceModel>> GetDevicesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetDevices
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<DeviceModel>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<List<DirectoryWithAccessModel>> GetRemoteDirectoriesAsync(string deviceName)
        {
            var request = new RemoteDirectoriesRequest
            {
                DeviceName = deviceName
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetRemoteDirectories,
                Data = JsonSerializer.Serialize(request)
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<DirectoryWithAccessModel>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<int> AddDeviceAsync(string name, bool access = false)
        {
            var device = new DeviceModel
            {
                Name = name,
                Access = access
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.AddDevice,
                Data = JsonSerializer.Serialize(device)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> RemoveDeviceAsync(int deviceId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.RemoveDevice,
                Data = deviceId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> UpdateDeviceAccessAsync(int deviceId, bool hasAccess)
        {
            var request = new { DeviceId = deviceId, HasAccess = hasAccess };
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.UpdateDevice,
                Data = JsonSerializer.Serialize(request)
            });

            return response.Success;
        }

        public async Task<List<NetworkAccessModel>> GetDirectoryAccessListAsync(int directoryId)
        {
            var request = new { DirectoryId = directoryId };
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetNetworkAccess,
                Data = directoryId.ToString()
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<NetworkAccessModel>>(response.Data);
            }

            return new List<NetworkAccessModel>();
        }

        #endregion

        #region Access Control

        public async Task<bool> GrantAccessAsync(int directoryId, int deviceId)
        {
            var request = new AccessRequest
            {
                DirectoryId = directoryId,
                DeviceId = deviceId
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GrantAccess,
                Data = JsonSerializer.Serialize(request)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> RevokeAccessAsync(int directoryId, int deviceId)
        {
            var request = new AccessRequest
            {
                DirectoryId = directoryId,
                DeviceId = deviceId
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.RevokeAccess,
                Data = JsonSerializer.Serialize(request)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> OpenAllSharesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.OpenAllShares
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> CloseAllSharesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.CloseAllShares
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        #endregion

        #region Service Status and Control

        public async Task<ServiceStatus> GetStatusAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetStatus
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<ServiceStatus>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> ForceCommitAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.ForceCommit
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> CommitDirectoryAsync(int directoryId, string message)
        {
            var request = new CommitRequest
            {
                DirectoryId = directoryId,
                Message = message
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.CommitDirectory,
                Data = JsonSerializer.Serialize(request)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> SetCommitIntervalAsync(int minutes)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.SetCommitInterval,
                Data = minutes.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        #endregion

        #region Version Control

        public async Task<List<CommitLogModel>> GetCommitLogAsync(int? directoryId = null, int limit = 100)
        {
            var data = directoryId.HasValue ? $"{directoryId.Value},{limit}" : limit.ToString();

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetCommitLog,
                Data = data
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<CommitLogModel>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<List<GitCommitHistoryModel>> GetGitHistoryAsync(int directoryId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetGitHistory,
                Data = directoryId.ToString()
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<GitCommitHistoryModel>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> RevertToCommitAsync(int directoryId, string commitHash)
        {
            var request = new RevertRequest
            {
                DirectoryId = directoryId,
                CommitHash = commitHash
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.RevertToCommit,
                Data = JsonSerializer.Serialize(request)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        #endregion

        #region Error Logging

        public async Task<List<ErrorLogModel>> GetErrorLogAsync(bool onlyUnresolved = false)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetErrorLog,
                Data = onlyUnresolved.ToString()
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<ErrorLogModel>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> MarkErrorResolvedAsync(int errorId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.MarkErrorResolved,
                Data = errorId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> ClearResolvedErrorsAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.ClearResolvedErrors
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<int> GetUnresolvedErrorCountAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetUnresolvedErrorCount
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        #endregion

        #region Settings

        public async Task<AppSettings> GetSettingsAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetSettings
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<AppSettings>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> SaveSettingsAsync(AppSettings settings)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.SaveSettings,
                Data = JsonSerializer.Serialize(settings)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        #endregion

        #region Service Health Check

        /// <summary>
        /// Перевірка чи доступний сервіс
        /// </summary>
        public async Task<bool> IsServiceAvailableAsync()
        {
            try
            {
                var status = await GetStatusAsync();
                return status.IsRunning;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Roadmap Operations

        public async Task<int> CreateRoadmapAsync(int directoryId, string name, string description, List<RoadmapEvent> events)
        {
            var data = new
            {
                DirectoryId = directoryId,
                Name = name,
                Description = description,
                Events = events
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.CreateRoadmap,
                Data = JsonSerializer.Serialize(data)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<List<Roadmap>> GetRoadmapsAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetRoadmaps
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<Roadmap>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<List<RoadmapEvent>> AnalyzeDirectoryForRoadmapAsync(int directoryId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.AnalyzeDirectoryForRoadmap,
                Data = directoryId.ToString()
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<RoadmapEvent>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<string> ExportRoadmapAsJsonAsync(int roadmapId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.ExportRoadmapAsJson,
                Data = roadmapId.ToString()
            });

            if (response.Success)
            {
                return response.Data;
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> DeleteRoadmapAsync(int roadmapId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.DeleteRoadmap,
                Data = roadmapId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        #endregion

        #region Network Discovery

        public async Task<List<NetworkDevice>> ScanNetworkAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.ScanNetwork
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<NetworkDevice>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<List<NetworkInterfaceInfo>> GetNetworkInterfacesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetNetworkInterfaces
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<NetworkInterfaceInfo>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        #endregion

        #region External Services

        public async Task<List<ExternalService>> GetExternalServicesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetExternalServices
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<ExternalService>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<int> AddExternalServiceAsync(string name, string serviceType, string url, string apiKey)
        {
            var service = new ExternalService
            {
                Name = name,
                ServiceType = serviceType,
                Url = url,
                ApiKey = apiKey,
                IsActive = true
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.AddExternalService,
                Data = JsonSerializer.Serialize(service)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> UpdateExternalServiceAsync(ExternalService service)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.UpdateExternalService,
                Data = JsonSerializer.Serialize(service)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> DeleteExternalServiceAsync(int serviceId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.DeleteExternalService,
                Data = serviceId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> TestExternalServiceAsync(int serviceId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.TestExternalService,
                Data = serviceId.ToString()
            });

            return response.Success;
        }

        #endregion

        #region Geo Roadmap Operations (v0.3)

        public async Task<int> CreateGeoRoadmapAsync(CreateGeoRoadmapRequest request)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.CreateGeoRoadmap,
                Data = JsonSerializer.Serialize(request)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<List<GeoRoadmap>> GetGeoRoadmapsAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetGeoRoadmaps
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<GeoRoadmap>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<GeoRoadmap> GetGeoRoadmapByIdAsync(int roadmapId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetGeoRoadmapById,
                Data = roadmapId.ToString()
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<GeoRoadmap>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> UpdateGeoRoadmapAsync(GeoRoadmap roadmap)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.UpdateGeoRoadmap,
                Data = JsonSerializer.Serialize(roadmap)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> DeleteGeoRoadmapAsync(int roadmapId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.DeleteGeoRoadmap,
                Data = roadmapId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        #endregion

        #region Geo Nodes Operations

        public async Task<int> AddGeoNodeAsync(GeoRoadmapNode node)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.AddGeoNode,
                Data = JsonSerializer.Serialize(node)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> UpdateGeoNodeAsync(GeoRoadmapNode node)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.UpdateGeoNode,
                Data = JsonSerializer.Serialize(node)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> DeleteGeoNodeAsync(int nodeId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.DeleteGeoNode,
                Data = nodeId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<List<GeoRoadmapNode>> GetGeoNodesByRoadmapAsync(int roadmapId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetGeoNodesByRoadmap,
                Data = roadmapId.ToString()
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<GeoRoadmapNode>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        #endregion

        #region Geo Routes Operations

        public async Task<int> AddGeoRouteAsync(GeoRoadmapRoute route)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.AddGeoRoute,
                Data = JsonSerializer.Serialize(route)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> DeleteGeoRouteAsync(int routeId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.DeleteGeoRoute,
                Data = routeId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        #endregion

        #region Geo Areas Operations

        public async Task<int> AddGeoAreaAsync(GeoRoadmapArea area)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.AddGeoArea,
                Data = JsonSerializer.Serialize(area)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> DeleteGeoAreaAsync(int areaId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.DeleteGeoArea,
                Data = areaId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        #endregion

        #region Templates Operations

        public async Task<List<GeoRoadmapTemplate>> GetGeoRoadmapTemplatesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetGeoRoadmapTemplates
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<GeoRoadmapTemplate>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<int> CreateFromTemplateAsync(int templateId, int directoryId, string name)
        {
            var data = new { TemplateId = templateId, DirectoryId = directoryId, Name = name };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.CreateFromTemplate,
                Data = JsonSerializer.Serialize(data)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<int> SaveAsTemplateAsync(int roadmapId, string name, string description, string category)
        {
            var data = new { RoadmapId = roadmapId, Name = name, Description = description, Category = category };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.SaveAsTemplate,
                Data = JsonSerializer.Serialize(data)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        #endregion

        #region Geocoding Operations

        public async Task<GeocodeResponse> GeocodeAddressAsync(string address)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GeocodeAddress,
                Data = JsonSerializer.Serialize(new GeocodeRequest { Address = address })
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<GeocodeResponse>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<string> ReverseGeocodeAsync(double latitude, double longitude)
        {
            var data = new { Latitude = latitude, Longitude = longitude };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.ReverseGeocode,
                Data = JsonSerializer.Serialize(data)
            });

            if (response.Success)
            {
                return response.Data;
            }

            throw new Exception(response.Message);
        }

        #endregion

        #region IP Filter Operations

        public async Task<List<IpFilterRule>> GetIpFilterRulesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetIpFilterRules
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<IpFilterRule>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<int> AddIpFilterRuleAsync(IpFilterRule rule)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.AddIpFilterRule,
                Data = JsonSerializer.Serialize(rule)
            });

            if (response.Success)
            {
                return int.Parse(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> UpdateIpFilterRuleAsync(IpFilterRule rule)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.UpdateIpFilterRule,
                Data = JsonSerializer.Serialize(rule)
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> DeleteIpFilterRuleAsync(int ruleId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.DeleteIpFilterRule,
                Data = ruleId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        public async Task<bool> TestIpAccessAsync(string ipAddress, int? directoryId = null, int? geoRoadmapId = null)
        {
            var data = new { IpAddress = ipAddress, DirectoryId = directoryId, GeoRoadmapId = geoRoadmapId };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.TestIpAccess,
                Data = JsonSerializer.Serialize(data)
            });

            return response.Success && bool.Parse(response.Data);
        }

        #endregion

        #region AI Analysis Operations

        public async Task<AIAnalysisResult> StartAIAnalysisAsync(int directoryId, AIAnalysisType analysisType, bool deepScan = false)
        {
            var request = new AIAnalysisRequest
            {
                DirectoryId = directoryId,
                AnalysisType = analysisType,
                DeepScan = deepScan
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.StartAIAnalysis,
                Data = JsonSerializer.Serialize(request)
            });

            if (response.Success)
                return JsonSerializer.Deserialize<AIAnalysisResult>(response.Data);

            throw new Exception(response.Message);
        }

        public async Task<List<AIAnalysisResult>> GetAIAnalysisResultsAsync(int directoryId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetAIAnalysisResults,
                Data = directoryId.ToString()
            });

            if (response.Success)
                return JsonSerializer.Deserialize<List<AIAnalysisResult>>(response.Data);

            throw new Exception(response.Message);
        }

        public async Task<bool> ApplyAIRecommendationsAsync(int analysisResultId, bool createBackup, List<int> violationIds = null)
        {
            var request = new ApplyReorganizationRequest
            {
                AnalysisResultId = analysisResultId,
                CreateBackup = createBackup,
                ViolationIds = violationIds ?? new List<int>()
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.ApplyAIRecommendations,
                Data = JsonSerializer.Serialize(request)
            });

            if (!response.Success)
                throw new Exception(response.Message);

            return true;
        }

        public async Task<AIServiceStatus> GetAIServiceStatusAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetAIServiceStatus
            });

            if (response.Success)
                return JsonSerializer.Deserialize<AIServiceStatus>(response.Data);

            throw new Exception(response.Message);
        }

        #endregion

        #region AI Chronological Roadmaps

        public async Task<AIChronologicalRoadmap> GenerateAIChronologicalRoadmapAsync(int directoryId, string name, string description)
        {
            var request = new GenerateChronoRoadmapRequest
            {
                DirectoryId = directoryId,
                Name = name,
                Description = description
            };

            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GenerateAIChronologicalRoadmap,
                Data = JsonSerializer.Serialize(request)
            });

            if (response.Success)
                return JsonSerializer.Deserialize<AIChronologicalRoadmap>(response.Data);

            throw new Exception(response.Message);
        }

        public async Task<List<AIChronologicalRoadmap>> GetAIChronologicalRoadmapsAsync(int directoryId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetAIChronologicalRoadmaps,
                Data = directoryId.ToString()
            });

            if (response.Success)
                return JsonSerializer.Deserialize<List<AIChronologicalRoadmap>>(response.Data);

            throw new Exception(response.Message);
        }

        public async Task<AIChronologicalRoadmap> GetAIChronologicalRoadmapByIdAsync(int roadmapId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetAIChronologicalRoadmapById,
                Data = roadmapId.ToString()
            });

            if (response.Success)
                return JsonSerializer.Deserialize<AIChronologicalRoadmap>(response.Data);

            throw new Exception(response.Message);
        }

        public async Task<bool> DeleteAIChronologicalRoadmapAsync(int roadmapId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.DeleteAIChronologicalRoadmap,
                Data = roadmapId.ToString()
            });

            if (!response.Success)
                throw new Exception(response.Message);

            return true;
        }

        public async Task<string> ExportAIChronologicalRoadmapAsync(int roadmapId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.ExportAIChronologicalRoadmap,
                Data = roadmapId.ToString()
            });

            if (response.Success)
                return response.Data;

            throw new Exception(response.Message);
        }

        #endregion

        #region AI Statistics

        public async Task<Dictionary<string, int>> GetAIStatisticsAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetAIStatistics
            });

            if (response.Success)
                return JsonSerializer.Deserialize<Dictionary<string, int>>(response.Data);

            throw new Exception(response.Message);
        }

        #endregion

        #region Network Core Operations

        public async Task<(bool IsRunning, PeerIdentity LocalIdentity)> GetNetworkCoreStatusAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetNetworkCoreStatus
            });

            if (response.Success)
            {
                var statusObj = JsonSerializer.Deserialize<JsonElement>(response.Data);
                var isRunning = statusObj.GetProperty("IsRunning").GetBoolean();
                var localIdentity = statusObj.GetProperty("LocalIdentity").Deserialize<PeerIdentity>();

                return (isRunning, localIdentity);
            }

            throw new Exception(response.Message);
        }

        public async Task<List<RemoteNode>> GetRemoteNodesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetRemoteNodes
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<RemoteNode>>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<FileSystemItemList> GetRemoteFileListAsync(RemoteFileListRequest request)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetRemoteFileList,
                Data = JsonSerializer.Serialize(request)
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<FileSystemItemList>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<FileSystemItem> GetRemoteFileMetadataAsync(RemoteFileRequest request)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.GetRemoteFileMetadata,
                Data = JsonSerializer.Serialize(request)
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<FileSystemItem>(response.Data);
            }

            throw new Exception(response.Message);
        }

        public async Task<bool> DownloadRemoteFileAsync(RemoteDownloadRequest request)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.DownloadRemoteFile,
                Data = JsonSerializer.Serialize(request)
            });

            return response.Success;
        }

        public async Task<bool> PingRemoteNodeAsync(Guid nodeId)
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = ServiceCommandType.PingRemoteNode,
                Data = nodeId.ToString()
            });

            return response.Success && bool.Parse(response.Data);
        }

        #endregion


        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}