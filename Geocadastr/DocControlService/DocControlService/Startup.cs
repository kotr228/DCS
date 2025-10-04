using DocControlService;
using DocControlService.Data;
using DocControlService.Models;
using DocControlService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();

        // 🔹 База даних і репозиторії
        services.AddSingleton<DatabaseManager>();
        services.AddSingleton<DirectoryRepository>();

        // 🔹 Сервіси для доступів і версій
        services.AddSingleton<AccessService>();
        services.AddSingleton<NetworkShareService>();

        // ⚡ Версійний контроль інтегруємо через фабрику
        services.AddSingleton<VersionControlFactory>();

        // 🔹 Фоновий Worker
        services.AddHostedService<Worker>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}
