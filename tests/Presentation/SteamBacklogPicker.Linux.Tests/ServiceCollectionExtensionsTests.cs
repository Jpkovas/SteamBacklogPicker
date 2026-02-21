using System.Runtime.InteropServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.Linux.Composition;
using SteamBacklogPicker.Linux.Services.Notifications;
using SteamBacklogPicker.Linux.Services.Updates;
using SteamBacklogPicker.UI.Services.Notifications;
using SteamBacklogPicker.UI.Services.Updates;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPlatformUserExperienceServices_ShouldBindLinuxImplementations_WhenPlatformIsLinux()
    {
        var services = new ServiceCollection();

        services.AddPlatformUserExperienceServices(OSPlatform.Linux);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToastNotificationService>().Should().BeOfType<LinuxToastNotificationService>();
        provider.GetRequiredService<IAppUpdateService>().Should().BeOfType<LinuxAppImageUpdateService>();
    }

    [Fact]
    public void AddPlatformUserExperienceServices_ShouldBindFallbackImplementations_WhenPlatformIsNotLinux()
    {
        var services = new ServiceCollection();

        services.AddPlatformUserExperienceServices(OSPlatform.Windows);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToastNotificationService>().Should().BeOfType<NullToastNotificationService>();
        provider.GetRequiredService<IAppUpdateService>().Should().BeOfType<NoOpAppUpdateService>();
    }
}
