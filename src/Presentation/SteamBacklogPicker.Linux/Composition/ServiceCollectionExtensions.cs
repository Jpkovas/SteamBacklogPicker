using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.Linux.Services.Notifications;
using SteamBacklogPicker.Linux.Services.Updates;
using SteamBacklogPicker.UI.Services.Notifications;
using SteamBacklogPicker.UI.Services.Updates;

namespace SteamBacklogPicker.Linux.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformUserExperienceServices(this IServiceCollection services, OSPlatform? platformOverride = null)
    {
        var platform = platformOverride ?? GetCurrentPlatform();
        if (platform == OSPlatform.Linux)
        {
            services.AddSingleton<IToastNotificationService, LinuxToastNotificationService>();
            services.AddSingleton<IAppUpdateService, LinuxAppImageUpdateService>();
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
