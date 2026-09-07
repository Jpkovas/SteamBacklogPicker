using System.ComponentModel;
using System.Diagnostics;

namespace SteamBacklogPicker.Linux.Controls;

internal static class MotionPreferences
{
    public static async Task<bool> PrefersReducedMotionAsync(CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable("SBP_REDUCED_MOTION") is "1" or "true") return true;
        // Avalonia 11 does not expose a cross-platform reduced-motion setting. Respect GNOME's
        // desktop setting when its standard reader is available; no shell or persistent watcher.
        const string settingsReader = "/usr/bin/gsettings";
        if (!OperatingSystem.IsLinux() || !File.Exists(settingsReader)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(settingsReader)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { "get", "org.gnome.desktop.interface", "enable-animations" }
            }
        };
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            return process.ExitCode == 0 && string.Equals((await output.ConfigureAwait(false)).Trim(), "false", StringComparison.Ordinal);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }
}
