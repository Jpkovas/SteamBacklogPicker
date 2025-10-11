using System.Globalization;
using System.Reflection;
using SteamBacklogPicker.Integration.SteamHooks;
using Xunit;

namespace SteamBacklogPicker.Integration.SteamHooks.Tests;

public sealed class SteamNamedPipeHookClientTests
{
    [Fact]
    public void TryParseEvent_UsesInvariantCultureForNumericValues()
    {
        var client = new SteamNamedPipeHookClient(new SteamHookOptions());
        var method = typeof(SteamNamedPipeHookClient).GetMethod(
            "TryParseEvent",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var line = "appid=10\tstatus=progress\tprogress=0.5\tbytes=1234";
        var arguments = new object?[] { line, null };

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            var testCulture = new CultureInfo("pt-BR");
            CultureInfo.CurrentCulture = testCulture;
            CultureInfo.CurrentUICulture = testCulture;

            var result = (bool)method!.Invoke(client, arguments)!;
            Assert.True(result);

            var downloadEvent = Assert.IsType<SteamDownloadEvent>(arguments[1]);
            var progress = downloadEvent.Progress;
            Assert.True(progress.HasValue);
            Assert.Equal(0.5, progress.Value);

            var bytesTransferred = downloadEvent.BytesTransferred;
            Assert.True(bytesTransferred.HasValue);
            Assert.Equal(1234L, bytesTransferred.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }
}
