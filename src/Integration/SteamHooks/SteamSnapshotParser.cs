using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace SteamBacklogPicker.Integration.SteamHooks;

public static class SteamSnapshotParser
{
    public static bool TryParseSnapshot(ReadOnlySpan<byte> data, out ImmutableArray<SteamDownloadEvent> events)
    {
        var text = Encoding.UTF8.GetString(data).TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(text))
        {
            events = ImmutableArray<SteamDownloadEvent>.Empty;
            return false;
        }

        var results = ImmutableArray.CreateBuilder<SteamDownloadEvent>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                var kvp = field.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kvp.Length == 2)
                {
                    fieldMap[kvp[0]] = kvp[1];
                }
            }

            if (!fieldMap.TryGetValue("appid", out var appIdRaw) || !int.TryParse(appIdRaw, out var appId))
            {
                continue;
            }

            fieldMap.TryGetValue("status", out var statusRaw);
            fieldMap.TryGetValue("progress", out var progressRaw);
            fieldMap.TryGetValue("bytes", out var bytesRaw);
            fieldMap.TryGetValue("depotid", out var depotRaw);

            var downloadEvent = new SteamDownloadEvent(
                DateTimeOffset.UtcNow,
                appId,
                int.TryParse(depotRaw, out var depotId) ? depotId : null,
                string.IsNullOrWhiteSpace(statusRaw) ? "unknown" : statusRaw!,
                double.TryParse(progressRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var progress) ? progress : null,
                long.TryParse(bytesRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) ? bytes : null);

            results.Add(downloadEvent);
        }

        events = results.MoveToImmutable();
        return events.Length > 0;
    }
}
