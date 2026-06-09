using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.Linux.Services.Notifications;
using SteamBacklogPicker.Linux.Services.Updates;
using SteamBacklogPicker.UI.Composition;

namespace SteamBacklogPicker.Linux.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformUserExperienceServices(this IServiceCollection services, OSPlatform? platformOverride = null)
    {
        services.AddSingleton<IFreedesktopNotificationClient, FreedesktopNotificationClient>();

        return services.AddPlatformUserExperienceServices<LinuxToastNotificationService, LinuxAppImageUpdateService>(
            OSPlatform.Linux,
            platformOverride);
    }
}
