using System.Collections.Immutable;
using SteamBacklogPicker.Integration.SteamHooks;
using Xunit;

namespace SteamHooks.Tests;

public sealed class SteamMemoryPollingHookClientTests
{
    [Fact]
    public void TryParseSnapshot_ShouldParseValidLinesAndIgnoreInvalidOnes()
    {
        var snapshot = "appid=570\tstatus=downloading\tprogress=12.5\tbytes=1024\tdepotid=111\nappid=invalid\tstatus=queued\n"u8.ToArray();

        var parsed = SteamSnapshotParser.TryParseSnapshot(snapshot, out var events);

        Assert.True(parsed);
        Assert.Single(events);
        Assert.Equal(570, events[0].AppId);
        Assert.Equal("downloading", events[0].Status);
        Assert.Equal(12.5d, events[0].Progress);
        Assert.Equal(1024L, events[0].BytesDownloaded);
        Assert.Equal(111, events[0].DepotId);
    }

    [Fact]
    public void TryParseSnapshot_ShouldReturnFalseForWhitespaceOnlyPayload()
    {
        var parsed = SteamSnapshotParser.TryParseSnapshot("\0\0\0"u8.ToArray(), out var events);

        Assert.False(parsed);
        Assert.Equal(ImmutableArray<SteamDownloadEvent>.Empty, events);
    }
}
