namespace GrayCat.UI.Services;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Core.Constants;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

public class ServiceCommunicator
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ServiceCommunicator()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(AppConstants.ServiceEndpoints.BaseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }

    public async Task<GenerationResult> GenerateProjectAsync(GenerationRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                AppConstants.ServiceEndpoints.GenerateProject,
                request,
                _jsonOptions);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<GenerationResult>(_jsonOptions)
                    ?? new GenerationResult
                    {
                        Success = false,
                        Message = "Failed to deserialize response"
                    };
            }

            var errorResult = await response.Content.ReadFromJsonAsync<GenerationResult>(_jsonOptions);
            return errorResult ?? new GenerationResult
            {
                Success = false,
                Message = $"Server returned error: {response.StatusCode}"
            };
        }
        catch (HttpRequestException ex)
        {
            return new GenerationResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}\n\nMake sure GrayCat Service is running.",
                Errors = new List<string> { ex.Message }
            };
        }
        catch (TaskCanceledException)
        {
            return new GenerationResult
            {
                Success = false,
                Message = "Request timeout. The generation process took too long.",
                Errors = new List<string> { "Timeout" }
            };
        }
    }

    public async Task<GenerationResult> ValidateProjectAsync(GenerationRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                AppConstants.ServiceEndpoints.ValidateProject,
                request,
                _jsonOptions);

            return await response.Content.ReadFromJsonAsync<GenerationResult>(_jsonOptions)
                ?? new GenerationResult { Success = false, Message = "Validation failed" };
        }
        catch (Exception ex)
        {
            return new GenerationResult
            {
                Success = false,
                Message = $"Validation error: {ex.Message}",
                Errors = new List<string> { ex.Message }
            };
        }
    }

    public async Task<bool> IsServiceRunningAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(AppConstants.ServiceEndpoints.GetStatus + "/ping");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}