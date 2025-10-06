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

        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}