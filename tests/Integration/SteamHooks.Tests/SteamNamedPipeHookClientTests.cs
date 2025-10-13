using System.Globalization;
using System.Reflection;
using FluentAssertions;
using SteamBacklogPicker.Integration.SteamHooks;
using Xunit;

namespace SteamHooks.Tests;

public sealed class SteamNamedPipeHookClientTests
{
    [Fact]
    public void TryParseEvent_ShouldParseProgressUsingInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var client = new SteamNamedPipeHookClient(new SteamHookOptions());
            var method = typeof(SteamNamedPipeHookClient).GetMethod(
                "TryParseEvent",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            var parameters = new object?[]
            {
                "appid=123\tprogress=0.75\tbytes=2048\tstatus=downloading",
                null,
            };

            var parsed = (bool)method.Invoke(client, parameters)!;
            parsed.Should().BeTrue();

            var downloadEvent = (SteamDownloadEvent)parameters[1]!;
            downloadEvent.AppId.Should().Be(123);
            downloadEvent.Progress.Should().Be(0.75d);
            downloadEvent.BytesTransferred.Should().Be(2048);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
