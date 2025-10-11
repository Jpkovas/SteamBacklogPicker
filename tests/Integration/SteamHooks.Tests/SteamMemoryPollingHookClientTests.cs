using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using SteamBacklogPicker.Integration.SteamHooks;
using Xunit;

namespace SteamHooks.Tests;

public sealed class SteamMemoryPollingHookClientTests
{
    private delegate bool TryParseSnapshotDelegate(ReadOnlySpan<byte> data, out IEnumerable<SteamDownloadEvent> events);

    [Fact]
    public void TryParseSnapshot_ParsesProgressUsingInvariantCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = new CultureInfo("pt-BR");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var nestedType = typeof(SteamMemoryPollingHookClient)
                .GetNestedType("SteamProcessAccessor", BindingFlags.NonPublic) ?? throw new InvalidOperationException("SteamProcessAccessor type not found.");
            var method = nestedType.GetMethod("TryParseSnapshot", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException("TryParseSnapshot method not found.");
            var parser = (TryParseSnapshotDelegate)method.CreateDelegate(typeof(TryParseSnapshotDelegate));

            var snapshot = "appid=570\tstatus=progress\tprogress=0.5\tbytes=1024\n";
            var buffer = Encoding.UTF8.GetBytes(snapshot);

            Assert.True(parser(buffer, out var events));
            var downloadEvent = Assert.Single(events);
            Assert.Equal(0.5, downloadEvent.Progress);
            Assert.Equal(1024, downloadEvent.BytesTransferred);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
