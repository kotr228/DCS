using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocControlAI.Core
{
    /// <summary>Клієнт для роботи з Ollama (Llama 3)</summary>
    public class OllamaClient
    {
        private readonly OllamaApiClient _client;
        private readonly string _modelName;
        private bool _isModelLoaded = false;

        public OllamaClient(string ollamaUrl = "http://localhost:11434", string modelName = "llama3")
        {
            _client = new OllamaApiClient(new Uri(ollamaUrl));
            _modelName = modelName;
        }

        /// <summary>Перевірка чи запущений Ollama</summary>
        public async Task<bool> IsOllamaRunningAsync()
        {
            try
            {
                var models = await _client.ListLocalModelsAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ollama не запущений: {ex.Message}");
                return false;
            }
        }

        /// <summary>Перевірка, чи модель доступна локально</summary>
        public async Task<bool> EnsureModelLoadedAsync()
        {
            try
            {
                var models = await _client.ListLocalModelsAsync();
                _isModelLoaded = models.Any(m => m.Name.Contains(_modelName, StringComparison.OrdinalIgnoreCase));

                if (!_isModelLoaded)
                    Console.WriteLine($"Модель '{_modelName}' не знайдена локально. Ollama завантажить її при першому запиті.");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка перевірки моделі: {ex.Message}");
                return false;
            }
        }

        /// <summary>Звичайний prompt (агрегація потоку у рядок)</summary>
        public async Task<string> SendPromptAsync(string prompt)
        {
            try
            {
                var request = new GenerateRequest
                {
                    Model = _modelName,
                    Prompt = prompt,
                    // Навіть якщо Stream=false, деякі версії OllamaSharp все одно повертають IAsyncEnumerable
                    Stream = false
                };

                var sb = new StringBuilder();
                await foreach (var chunk in _client.GenerateAsync(request))
                {
                    if (!string.IsNullOrEmpty(chunk?.Response))
                        sb.Append(chunk.Response);
                }
                return sb.Length > 0 ? sb.ToString() : "Помилка: порожня відповідь";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка запиту до Ollama: {ex.Message}");
                throw;
            }
        }

        /// <summary>Chat prompt (агрегація потоку у рядок)</summary>
        public async Task<string> SendChatPromptAsync(List<Message> messages)
        {
            try
            {
                var chatRequest = new ChatRequest
                {
                    Model = _modelName,
                    Messages = messages,
                    Stream = false
                };

                var sb = new StringBuilder();
                await foreach (var part in _client.ChatAsync(chatRequest))
                {
                    var piece = part?.Message?.Content;
                    if (!string.IsNullOrEmpty(piece))
                        sb.Append(piece);
                }
                return sb.Length > 0 ? sb.ToString() : "Помилка: порожня відповідь";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка chat-запиту: {ex.Message}");
                throw;
            }
        }

        /// <summary>Статус Ollama</summary>
        public async Task<(bool isRunning, string version, bool isModelLoaded)> GetStatusAsync()
        {
            try
            {
                bool isRunning = await IsOllamaRunningAsync();
                if (!isRunning)
                    return (false, null, false);

                var models = await _client.ListLocalModelsAsync();
                bool modelLoaded = models.Any(m => m.Name.Contains(_modelName, StringComparison.OrdinalIgnoreCase));
                return (true, "latest", modelLoaded);
            }
            catch
            {
                return (false, null, false);
            }
        }

        /// <summary>Генерація JSON (structured output)</summary>
        public async Task<string> GenerateJsonAsync(string prompt, string jsonSchema = null)
        {
            try
            {
                string enhancedPrompt = $@"{prompt}

IMPORTANT: Respond ONLY with valid JSON. Do not include any explanatory text before or after the JSON.
{(jsonSchema != null ? $"Use this JSON schema:\n{jsonSchema}" : "")}";

                var response = await SendPromptAsync(enhancedPrompt);

                int jsonStart = response.IndexOf('{');
                int jsonEnd = response.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                    return response.Substring(jsonStart, jsonEnd - jsonStart + 1);

                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка генерації JSON: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>Контекст розмови</summary>
    public class ConversationContext
    {
        public List<Message> Messages { get; set; } = new List<Message>();
        public string SystemPrompt { get; set; }

        public void AddMessage(string role, string content)
        {
            Messages.Add(new Message { Role = role, Content = content });
        }
    }
}
