using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.Linux.Composition;
using SteamBacklogPicker.Linux.Views;
using SteamBacklogPicker.UI.Services.Localization;
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

        if (_serviceProvider.GetService<ILocalizationService>() is { } localizationService)
        {
            localizationService.ResourcesChanged += OnLocalizationResourcesChanged;
            OnLocalizationResourcesChanged(this, localizationService.GetAllStrings());
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) =>
            {
                _updateCancellation?.Cancel();

                if (_serviceProvider?.GetService<ILocalizationService>() is { } localizationService)
                {
                    localizationService.ResourcesChanged -= OnLocalizationResourcesChanged;
                }

                _serviceProvider?.Dispose();
            };

            if (_serviceProvider.GetService<IAppUpdateService>() is { } updateService)
            {
                _ = Task.Run(() => updateService.CheckForUpdatesAsync(_updateCancellation.Token));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnLocalizationResourcesChanged(object? _, IReadOnlyDictionary<string, string> resources)
    {
        if (Current is not { } app)
        {
            return;
        }

        foreach (var (key, value) in resources)
        {
            app.Resources[key] = value;
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLinuxApplicationServices();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
