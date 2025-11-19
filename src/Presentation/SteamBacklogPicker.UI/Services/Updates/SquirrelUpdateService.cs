using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Infrastructure.Telemetry;
using Squirrel;
using Squirrel.Sources;

namespace SteamBacklogPicker.UI.Services.Updates;

public sealed class SquirrelUpdateService : IAppUpdateService
{
    private const string RepositoryUrl = "https://github.com/Jpkovas/SteamBacklogPicker";
    private readonly ITelemetryClient? _telemetryClient;

    public SquirrelUpdateService(ITelemetryClient? telemetryClient)
    {
        _telemetryClient = telemetryClient;
    }

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (!IsRunningFromSquirrelInstall())
        {
            return;
        }

        try
        {
            var accessToken = Environment.GetEnvironmentVariable("SBP_GITHUB_TOKEN");
            using var updateManager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken, prerelease: false));
            cancellationToken.ThrowIfCancellationRequested();

            var updateInfo = await updateManager.CheckForUpdate();

            if (updateInfo.ReleasesToApply.Count == 0)
            {
                _telemetryClient?.TrackEvent("squirrel_update_not_available");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await updateManager.DownloadReleases(updateInfo.ReleasesToApply);
            cancellationToken.ThrowIfCancellationRequested();
            await updateManager.ApplyReleases(updateInfo);

            _telemetryClient?.TrackEvent("squirrel_update_applied", new Dictionary<string, object>
            {
                ["targetVersion"] = updateInfo.FutureReleaseEntry.Version.ToString()
            });

            UpdateManager.RestartApp();
        }
        catch (OperationCanceledException)
        {
            _telemetryClient?.TrackEvent("squirrel_update_cancelled");
        }
        catch (Exception ex)
        {
            _telemetryClient?.TrackException(ex, new Dictionary<string, object>
            {
                ["updateFeed"] = RepositoryUrl
            });
        }
    }

    private static bool IsRunningFromSquirrelInstall()
    {
        try
        {
            var baseDirectory = AppContext.BaseDirectory;
            var updateExePath = Path.GetFullPath(Path.Combine(baseDirectory, "..", "Update.exe"));
            return File.Exists(updateExePath);
        }
        catch
        {
            return false;
        }
    }
}
