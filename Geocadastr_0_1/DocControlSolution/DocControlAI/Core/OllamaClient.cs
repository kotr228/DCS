using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LLama;
using LLama.Common;

namespace DocControlAI.Core
{
    /// <summary>Клієнт для локальної Llama 3 (.gguf) без Ollama</summary>
    public class OllamaClient
    {
        private readonly string _modelPath;
        private readonly string _modelName;

        private LLamaModel _model;
        private LLamaContext _context;
        private InteractiveExecutor _executor;
        private bool _isModelLoaded = false;

        public OllamaClient(string ollamaUrl = "Models/meta-llama-3-8b-instruct.Q4_K_M.gguf", string modelName = "llama3")
        {
            _modelPath = ollamaUrl;
            _modelName = modelName;
        }

        public async Task<bool> IsOllamaRunningAsync()
        {
            return await Task.Run(() => File.Exists(_modelPath));
        }

        public async Task<bool> EnsureModelLoadedAsync()
        {
            if (_isModelLoaded)
                return true;

            if (!File.Exists(_modelPath))
            {
                Console.WriteLine($"❌ Файл моделі не знайдено: {_modelPath}");
                return false;
            }

            await Task.Run(() =>
            {
                Console.WriteLine($"[AI] Завантаження моделі {_modelPath}...");

                var modelParams = new ModelParams(_modelPath)
                {
                    ContextSize = 2048,
                    GpuLayerCount = 0
                };

                _model = new LLamaModel(modelParams);
                _context = _model.CreateContext();
                _executor = new InteractiveExecutor(_context);
                _isModelLoaded = true;

                Console.WriteLine($"✅ Модель {_modelName} готова до роботи.");
            });

            return true;
        }

        public async Task<string> SendPromptAsync(string prompt)
        {
            await EnsureModelLoadedAsync();
            if (!_isModelLoaded)
                throw new Exception("Модель не завантажена.");

            var sb = new StringBuilder();
            var inferParams = new InferenceParams
            {
                Temperature = 0.7f,
                MaxTokens = 512
            };

            await Task.Run(() =>
            {
                foreach (var token in _executor.Infer(prompt, inferParams))
                    sb.Append(token);
            });

            return sb.ToString();
        }

        public async Task<string> SendChatPromptAsync(List<Message> messages)
        {
            var combined = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
            return await SendPromptAsync(combined);
        }

        public async Task<string> GenerateJsonAsync(string prompt, string jsonSchema = null)
        {
            string fullPrompt = $@"{prompt}

IMPORTANT: Respond ONLY with valid JSON. Do not include any explanatory text before or after the JSON.
{(jsonSchema != null ? $"Use this JSON schema:\n{jsonSchema}" : "")}";

            string response = await SendPromptAsync(fullPrompt);

            int start = response.IndexOf('{');
            int end = response.LastIndexOf('}');
            if (start >= 0 && end > start)
                return response.Substring(start, end - start + 1);

            return response;
        }

        public async Task<(bool isRunning, string version, bool isModelLoaded)> GetStatusAsync()
        {
            bool exists = await IsOllamaRunningAsync();
            return (exists, "LLamaSharp 0.8.1 (local, .NET6)", _isModelLoaded);
        }
    }

    public class ConversationContext
    {
        public List<Message> Messages { get; set; } = new List<Message>();
        public string SystemPrompt { get; set; }

        public void AddMessage(string role, string content)
        {
            Messages.Add(new Message { Role = role, Content = content });
        }
    }

    public class Message
    {
        public string Role { get; set; }
        public string Content { get; set; }
    }
}
