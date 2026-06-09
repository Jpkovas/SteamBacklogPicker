using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.AppCore.Composition;

namespace SteamBacklogPicker.Linux.Composition;

public static class LinuxServiceCollectionExtensions
{
    public static IServiceCollection AddLinuxApplicationServices(this IServiceCollection services)
    {
        services.AddPlatformUserExperienceServices();
        services.AddSteamBacklogPickerApplicationCore();

        return services;
    }
}
