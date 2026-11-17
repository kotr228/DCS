namespace GrayCat.UI.Services;

using GrayCat.Core.Constants;
using GrayCat.Core.Generation;
using GrayCat.Core.Models.Generation;
using GrayCat.Core.Serialization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

/// <summary>
/// Runs project generation through the GrayCat background service when it is running,
/// and directly in the app otherwise, so the editor works with or without the service.
/// </summary>
public class ServiceCommunicator : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = GrayCatJson.Compact;

    public ServiceCommunicator()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(AppConstants.ServiceEndpoints.BaseUrl),
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    /// <summary>"service" or "built-in": where the last generation ran.</summary>
    public string LastEngine { get; private set; } = "built-in";

    public async Task<GenerationResult> GenerateProjectAsync(GenerationRequest request)
    {
        if (await IsServiceRunningAsync())
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(AppConstants.ServiceEndpoints.GenerateProject, request, _jsonOptions);
                var result = await response.Content.ReadFromJsonAsync<GenerationResult>(_jsonOptions);
                // The service account may not be allowed into the user's folders; then generate here instead.
                if (result != null && !result.AccessDenied)
                {
                    LastEngine = "service";
                    return result;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
            {
                // Fall through to in-process generation.
            }
        }

        LastEngine = "built-in";
        return await ProjectGenerator.GenerateAsync(request);
    }

    public async Task<bool> IsServiceRunningAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
            var response = await _httpClient.GetAsync(AppConstants.ServiceEndpoints.Health, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
