using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.UI.Services.Notifications;
using SteamBacklogPicker.UI.Services.Updates;

namespace SteamBacklogPicker.UI.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformUserExperienceServices(this IServiceCollection services, OSPlatform? platformOverride = null)
        => services.AddPlatformUserExperienceServices<ToastNotificationService, SquirrelUpdateService>(
            OSPlatform.Windows,
            platformOverride);
}
