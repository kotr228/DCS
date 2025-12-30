using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CatSuite.TestServer
{
    /// <summary>
    /// Простий HTTP сервер для тестування інсталера локально
    /// </summary>
    public class SimpleHttpServer
    {
        private readonly HttpListener _listener;
        private readonly string _webRootPath;
        private readonly int _port;
        private bool _running;

        public SimpleHttpServer(int port = 8080, string webRootPath = null)
        {
            _port = port;
            _webRootPath = webRootPath ?? Path.Combine(AppContext.BaseDirectory, "www");

            if (!Directory.Exists(_webRootPath))
            {
                Directory.CreateDirectory(_webRootPath);
            }

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        }

        public void Start()
        {
            Console.WriteLine("═══════════════════════════════════════════════");
            Console.WriteLine("  CatSuite Test HTTP Server");
            Console.WriteLine("═══════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine($"📂 Web Root: {_webRootPath}");
            Console.WriteLine($"🌐 Listening on:");
            Console.WriteLine($"   http://localhost:{_port}/");
            Console.WriteLine($"   http://127.0.0.1:{_port}/");
            Console.WriteLine();
            Console.WriteLine("Натисніть Ctrl+C для зупинки...");
            Console.WriteLine("═══════════════════════════════════════════════");
            Console.WriteLine();

            _listener.Start();
            _running = true;

            Task.Run(async () => await HandleRequestsAsync());
        }

        public void Stop()
        {
            _running = false;
            _listener.Stop();
            _listener.Close();
        }

        private async Task HandleRequestsAsync()
        {
            while (_running)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequestAsync(context));
                }
                catch (Exception ex) when (_running)
                {
                    Console.WriteLine($"❌ Помилка: {ex.Message}");
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string requestedPath = request.Url.LocalPath.TrimStart('/');

                // Якщо запит до кореня - шукаємо index.html
                if (string.IsNullOrEmpty(requestedPath))
                    requestedPath = "index.html";

                string filePath = Path.Combine(_webRootPath, requestedPath);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {request.HttpMethod} {request.Url.LocalPath}");

                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"   ❌ 404 Not Found: {requestedPath}");
                    response.StatusCode = 404;
                    byte[] notFoundData = Encoding.UTF8.GetBytes($"404 - File not found: {requestedPath}");
                    await response.OutputStream.WriteAsync(notFoundData, 0, notFoundData.Length);
                }
                else
                {
                    // Визначаємо Content-Type
                    string contentType = GetContentType(filePath);
                    response.ContentType = contentType;

                    // Встановлюємо CORS headers для локальної розробки
                    response.Headers.Add("Access-Control-Allow-Origin", "*");

                    // Читаємо та відправляємо файл
                    byte[] fileData = await File.ReadAllBytesAsync(filePath);
                    response.ContentLength64 = fileData.Length;
                    response.StatusCode = 200;

                    await response.OutputStream.WriteAsync(fileData, 0, fileData.Length);

                    Console.WriteLine($"   ✅ 200 OK ({fileData.Length} bytes, {contentType})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error: {ex.Message}");
                response.StatusCode = 500;
                byte[] errorData = Encoding.UTF8.GetBytes($"500 - Internal error: {ex.Message}");
                await response.OutputStream.WriteAsync(errorData, 0, errorData.Length);
            }
            finally
            {
                response.Close();
            }
        }

        private string GetContentType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            return ext switch
            {
                ".json" => "application/json",
                ".dll" => "application/octet-stream",
                ".msi" => "application/octet-stream",
                ".exe" => "application/octet-stream",
                ".zip" => "application/zip",
                ".html" => "text/html",
                ".htm" => "text/html",
                ".txt" => "text/plain",
                ".xml" => "application/xml",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            int port = 8080;
            string webRoot = null;

            // Параметри командного рядка
            if (args.Length > 0 && int.TryParse(args[0], out int parsedPort))
                port = parsedPort;

            if (args.Length > 1)
                webRoot = args[1];

            var server = new SimpleHttpServer(port, webRoot);

            // Обробка Ctrl+C
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("🛑 Зупинка сервера...");
                server.Stop();
                e.Cancel = true;
            };

            try
            {
                server.Start();

                // Чекаємо нескінченно
                while (true)
                {
                    System.Threading.Thread.Sleep(1000);
                }
            }
            catch (HttpListenerException ex) when (ex.Message.Contains("denied"))
            {
                Console.WriteLine("❌ ПОМИЛКА: Недостатньо прав для запуску HTTP сервера.");
                Console.WriteLine();
                Console.WriteLine("Рішення:");
                Console.WriteLine("1. Запустіть від адміністратора");
                Console.WriteLine("2. Або використайте NGINX/IIS");
                Console.WriteLine("3. Або змініть порт у Program.cs (наприклад, на 8888)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Критична помилка: {ex.Message}");
            }
        }
    }
}
