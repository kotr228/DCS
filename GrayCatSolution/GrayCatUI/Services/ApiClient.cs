using GrayCat.Shared.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace GrayCat.UI.Services
{
    public class ApiClient
    {
        private readonly HttpClient _httpClient;
        private const string ServiceBaseUrl = "http://localhost:5123/";

        public ApiClient()
        {
            _httpClient = new HttpClient();
        }

        public async Task<GenerationResult> GenerateProjectAsync(ProjectGenerationRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(ServiceBaseUrl + "api/project/generate", request);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<GenerationResult>()
                           ?? new GenerationResult { Success = false, Message = "Failed to deserialize success response." };
                }
                else
                {
                    // Якщо сервер повернув помилку (400/500), спробуємо її десеріалізувати
                    return await response.Content.ReadFromJsonAsync<GenerationResult>()
                           ?? new GenerationResult { Success = false, Message = $"Server returned error code: {response.StatusCode}" };
                }
            }
            catch (HttpRequestException ex)
            {
                return new GenerationResult { Success = false, Message = $"Connection to the service failed: {ex.Message}" };
            }
        }
    }
}