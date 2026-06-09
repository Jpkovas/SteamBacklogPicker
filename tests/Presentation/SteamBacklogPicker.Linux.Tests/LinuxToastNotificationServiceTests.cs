using Domain;
using FluentAssertions;
using SteamBacklogPicker.Linux.Services.Notifications;
using Xunit;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class LinuxToastNotificationServiceTests
{
    [Fact]
    public void ShowGameSelected_ShouldSendFreedesktopNotificationPayload()
    {
        var client = new CapturingNotificationClient();
        var service = new LinuxToastNotificationService(client);
        var game = new GameEntry
        {
            Id = GameIdentifier.ForSteam(42),
            Title = "Half-Life"
        };

        service.ShowGameSelected(game, "/tmp/cover.jpg");

        client.AppName.Should().Be("Steam Backlog Picker");
        client.Summary.Should().Be("Half-Life");
        client.Body.Should().BeEmpty();
        client.ImagePath.Should().Be("/tmp/cover.jpg");
        client.Timeout.Should().Be(TimeSpan.FromMilliseconds(1500));
    }

    [Fact]
    public void ShowGameSelected_ShouldIgnoreNotificationBackendFailure()
    {
        var service = new LinuxToastNotificationService(new ThrowingNotificationClient());
        var game = new GameEntry
        {
            Id = GameIdentifier.ForSteam(42),
            Title = "Half-Life"
        };

        var act = () => service.ShowGameSelected(game, null);

        act.Should().NotThrow();
    }

    private sealed class CapturingNotificationClient : IFreedesktopNotificationClient
    {
        public string? AppName { get; private set; }

        public string? Summary { get; private set; }

        public string? Body { get; private set; }

        public string? ImagePath { get; private set; }

        public TimeSpan Timeout { get; private set; }

        public void Show(string appName, string summary, string body, string? imagePath, TimeSpan timeout)
        {
            AppName = appName;
            Summary = summary;
            Body = body;
            ImagePath = imagePath;
            Timeout = timeout;
        }
    }

    private sealed class ThrowingNotificationClient : IFreedesktopNotificationClient
    {
        public void Show(string appName, string summary, string body, string? imagePath, TimeSpan timeout)
            => throw new InvalidOperationException("notification backend unavailable");
    }
}
