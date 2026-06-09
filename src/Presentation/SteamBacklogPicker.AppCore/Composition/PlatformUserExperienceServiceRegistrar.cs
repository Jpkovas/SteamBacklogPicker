using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.UI.Services.Notifications;
using SteamBacklogPicker.UI.Services.Updates;

namespace SteamBacklogPicker.UI.Composition;

public static class PlatformUserExperienceServiceRegistrar
{
    public static IServiceCollection AddPlatformUserExperienceServices<TToastNotificationService, TAppUpdateService>(
        this IServiceCollection services,
        OSPlatform targetPlatform,
        OSPlatform? platformOverride = null)
        where TToastNotificationService : class, IToastNotificationService
        where TAppUpdateService : class, IAppUpdateService
    {
        var platform = platformOverride ?? GetCurrentPlatform();
        if (platform == targetPlatform)
        {
            services.AddSingleton<IToastNotificationService, TToastNotificationService>();
            services.AddSingleton<IAppUpdateService, TAppUpdateService>();
            return services;
        }

        services.AddSingleton<IToastNotificationService, NullToastNotificationService>();
        services.AddSingleton<IAppUpdateService, NoOpAppUpdateService>();
        return services;
    }

    private static OSPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OSPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return OSPlatform.Linux;
        }

        return OSPlatform.Create("UNKNOWN");
    }
}
