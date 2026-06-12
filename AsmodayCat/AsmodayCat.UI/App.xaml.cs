using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AsmodayCat.UI.Services;
using AsmodayCat.UI.ViewModels;
using AsmodayCat.UI.Views;

namespace AsmodayCat.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IpcClient>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<HardwareViewModel>();
        services.AddTransient<AgentRulesViewModel>();
        services.AddTransient<TerminalViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<ModelsManagerViewModel>();

        services.AddTransient<DashboardView>();
        services.AddTransient<HardwareView>();
        services.AddTransient<AgentRulesView>();
        services.AddTransient<TerminalView>();
        services.AddTransient<ChatView>();
        services.AddTransient<ModelsManagerView>();
        services.AddTransient<MainWindow>();
        services.AddTransient<OverlayWindow>();

        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
