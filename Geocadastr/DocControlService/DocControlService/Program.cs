using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;

namespace DocControlService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 🔹 Перевірка прав
            if (!IsAdministrator())
            {
                var exeName = Process.GetCurrentProcess().MainModule.FileName;
                var startInfo = new ProcessStartInfo(exeName)
                {
                    Verb = "runas", // UAC підняття
                    UseShellExecute = true
                };

                try
                {
                    Process.Start(startInfo);
                }
                catch
                {
                    Console.WriteLine("❌ Користувач відмовив у підвищенні прав.");
                }

                return;
            }

            Console.WriteLine("🚀 Запуск DocControlService...");

            if (Environment.UserInteractive)
            {
                Console.WriteLine("🖥 Режим: Консольний (debug).");
                CreateHostBuilder(args, useWindowsService: false).Build().Run();
            }
            else
            {
                Console.WriteLine("⚙ Режим: Windows Service.");

                if (!ServiceController.GetServices().Any(s => s.ServiceName == "DocControlService"))
                {
                    try
                    {
                        var exePath = Process.GetCurrentProcess().MainModule.FileName;
                        Console.WriteLine($"📂 Виконуваний файл: {exePath}");

                        Console.WriteLine("📌 Реєстрація сервісу...");
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "sc",
                            Arguments = $"create DocControlService binPath= \"{exePath}\" start= auto",
                            Verb = "runas",
                            UseShellExecute = true
                        })?.WaitForExit();

                        Console.WriteLine("✅ Сервіс зареєстровано.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Помилка при реєстрації: {ex.Message}");
                    }
                }

                CreateHostBuilder(args, useWindowsService: true).Build().Run();
            }
        }

        // 🔹 Хост для API і сервісу
        public static IHostBuilder CreateHostBuilder(string[] args, bool useWindowsService) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    Console.WriteLine("🔧 Реєстрація HostedService (Worker).");
                    services.AddHostedService<Worker>();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    Console.WriteLine("🌐 WebHost на http://localhost:5000");
                    webBuilder.UseUrls("http://localhost:5000");
                    webBuilder.UseStartup<Startup>();
                })
                .ApplyIf(useWindowsService, builder => builder.UseWindowsService());

        // 🔹 Перевірка прав
        private static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    // ✅ Винесене розширення у статичний клас
    public static class HostBuilderExtensions
    {
        public static IHostBuilder ApplyIf(this IHostBuilder builder, bool condition, Func<IHostBuilder, IHostBuilder> action)
            => condition ? action(builder) : builder;
    }
}
