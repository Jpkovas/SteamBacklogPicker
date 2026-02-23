using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.Linux.Composition;
using SteamBacklogPicker.Linux.Views;
using SteamBacklogPicker.UI.Services.Updates;

namespace SteamBacklogPicker.Linux;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private CancellationTokenSource? _updateCancellation;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _serviceProvider = BuildServices();
        _updateCancellation = new CancellationTokenSource();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) =>
            {
                _updateCancellation?.Cancel();
                _serviceProvider?.Dispose();
            };
            if (_serviceProvider.GetService<IAppUpdateService>() is { } updateService)
            {
                _ = Task.Run(() => updateService.CheckForUpdatesAsync(_updateCancellation.Token));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }


    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLinuxApplicationServices();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
