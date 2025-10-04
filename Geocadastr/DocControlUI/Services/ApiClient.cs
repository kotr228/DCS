using DocControlUI.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DocControlUI.Services
{
    public class ApiClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl = "http://localhost:5000/api"; // бекенд URL

        public ApiClient()
        {
            _http = new HttpClient();
        }

        public async Task<List<FolderObject>> GetFolders()
        {
            var response = await _http.GetAsync($"{_baseUrl}/folders");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<FolderObject>>(json);
        }

        public async Task AddFolder(FolderObject folder)
        {
            var json = JsonSerializer.Serialize(folder);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync($"{_baseUrl}/folders", content);
            response.EnsureSuccessStatusCode();
        }
    }
}
