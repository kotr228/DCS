using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;

namespace DocControlService.Client
{
    /// <summary>
    /// Клієнт для комунікації з DocControl Service через Named Pipes
    /// </summary>
    public class DocControlServiceClient : IDisposable
    {
        private const string PipeName = "DocControlServicePipe";
        private const int TimeoutMs = 5000;

        /// <summary>
        /// Відправка команди до сервісу
        /// </summary>
        private async Task<ServiceResponse> SendCommandAsync(ServiceCommand command)
        {
            NamedPipeClientStream pipeClient = null;
            try
            {
                pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                await pipeClient.ConnectAsync(TimeoutMs);

                using (var writer = new StreamWriter(pipeClient, leaveOpen: true) { AutoFlush = true })
                using (var reader = new StreamReader(pipeClient, leaveOpen: true))
                {
                    var requestJson = JsonSerializer.Serialize(command);
                    await writer.WriteLineAsync(requestJson);
                    await writer.FlushAsync();

                    var responseJson = await reader.ReadLineAsync();

                    if (string.IsNullOrEmpty(responseJson))
                    {
                        return new ServiceResponse
                        {
                            Success = false,
                            Message = "Отримано порожню відповідь від сервісу"
                        };
                    }

                    return JsonSerializer.Deserialize<ServiceResponse>(responseJson);
                }
            }
            catch (TimeoutException)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Тайм-аут підключення до сервісу. Перевірте чи запущений сервіс."
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
                return new ServiceResponse
                {
                    Success = false,
                    Message = $"Помилка комунікації з сервісом: {ex.Message}"
                };
            }
            finally
            {
                if (pipeClient != null)
                {
                    try
                    {
                        if (pipeClient.IsConnected)
                        {
                            pipeClient.Close();
                        }
                        pipeClient.Dispose();
                    }
                    catch { }
                }
            }
        }

        #region Directory Operations

        public async Task<List<DirectoryWithAccessModel>> GetDirectoriesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = CommandType.GetDirectories
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
                Type = CommandType.AddDirectory,
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
                Type = CommandType.RemoveDirectory,
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
                Type = CommandType.UpdateDirectoryName,
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
                Type = CommandType.ScanDirectory,
                Data = directoryId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
        }

        #endregion

        #region Device Operations

        public async Task<List<DeviceModel>> GetDevicesAsync()
        {
            var response = await SendCommandAsync(new ServiceCommand
            {
                Type = CommandType.GetDevices
            });

            if (response.Success)
            {
                return JsonSerializer.Deserialize<List<DeviceModel>>(response.Data);
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
                Type = CommandType.AddDevice,
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
                Type = CommandType.RemoveDevice,
                Data = deviceId.ToString()
            });

            if (!response.Success)
            {
                throw new Exception(response.Message);
            }

            return true;
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
                Type = CommandType.GrantAccess,
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
                Type = CommandType.RevokeAccess,
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
                Type = CommandType.OpenAllShares
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
                Type = CommandType.CloseAllShares
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
                Type = CommandType.GetStatus
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
                Type = CommandType.ForceCommit
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
                Type = CommandType.SetCommitInterval,
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
                Type = CommandType.GetCommitLog,
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
                Type = CommandType.GetGitHistory,
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
                Type = CommandType.RevertToCommit,
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
                Type = CommandType.GetErrorLog,
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
                Type = CommandType.MarkErrorResolved,
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
                Type = CommandType.ClearResolvedErrors
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
                Type = CommandType.GetUnresolvedErrorCount
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
                Type = CommandType.GetSettings
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
                Type = CommandType.SaveSettings,
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
                Type = CommandType.CreateRoadmap,
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
                Type = CommandType.GetRoadmaps
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
                Type = CommandType.AnalyzeDirectoryForRoadmap,
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
                Type = CommandType.ExportRoadmapAsJson,
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
                Type = CommandType.DeleteRoadmap,
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
                Type = CommandType.ScanNetwork
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
                Type = CommandType.GetNetworkInterfaces
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
                Type = CommandType.GetExternalServices
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
                Type = CommandType.AddExternalService,
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
                Type = CommandType.UpdateExternalService,
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
                Type = CommandType.DeleteExternalService,
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
                Type = CommandType.TestExternalService,
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
                Type = CommandType.CreateGeoRoadmap,
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
                Type = CommandType.GetGeoRoadmaps
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
                Type = CommandType.GetGeoRoadmapById,
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
                Type = CommandType.UpdateGeoRoadmap,
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
                Type = CommandType.DeleteGeoRoadmap,
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
                Type = CommandType.AddGeoNode,
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
                Type = CommandType.UpdateGeoNode,
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
                Type = CommandType.DeleteGeoNode,
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
                Type = CommandType.GetGeoNodesByRoadmap,
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
                Type = CommandType.AddGeoRoute,
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
                Type = CommandType.DeleteGeoRoute,
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
                Type = CommandType.AddGeoArea,
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
                Type = CommandType.DeleteGeoArea,
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
                Type = CommandType.GetGeoRoadmapTemplates
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
                Type = CommandType.CreateFromTemplate,
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
                Type = CommandType.SaveAsTemplate,
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
                Type = CommandType.GeocodeAddress,
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
                Type = CommandType.ReverseGeocode,
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
                Type = CommandType.GetIpFilterRules
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
                Type = CommandType.AddIpFilterRule,
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
                Type = CommandType.UpdateIpFilterRule,
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
                Type = CommandType.DeleteIpFilterRule,
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
                Type = CommandType.TestIpAccess,
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
                Type = CommandType.StartAIAnalysis,
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
                Type = CommandType.GetAIAnalysisResults,
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
                Type = CommandType.ApplyAIRecommendations,
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
                Type = CommandType.GetAIServiceStatus
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
                Type = CommandType.GenerateAIChronologicalRoadmap,
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
                Type = CommandType.GetAIChronologicalRoadmaps,
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
                Type = CommandType.GetAIChronologicalRoadmapById,
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
                Type = CommandType.DeleteAIChronologicalRoadmap,
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
                Type = CommandType.ExportAIChronologicalRoadmap,
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
                Type = CommandType.GetAIStatistics
            });

            if (response.Success)
                return JsonSerializer.Deserialize<Dictionary<string, int>>(response.Data);

            throw new Exception(response.Message);
        }

        #endregion


        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}