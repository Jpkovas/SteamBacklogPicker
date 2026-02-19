using System;
using System.Diagnostics;

namespace SteamBacklogPicker.UI.Services.Launch;

/// <summary>
/// Represents the launch or installation capability for a game, including the
/// protocol URI and <see cref="ProcessStartInfo"/> required to invoke it.
/// </summary>
public sealed record class GameLaunchAction
{
    private GameLaunchAction(bool isSupported, string? protocolUri, ProcessStartInfo? processStartInfo, string? errorMessage)
    {
        IsSupported = isSupported;
        ProtocolUri = protocolUri;
        ProcessStartInfo = processStartInfo;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets a value indicating whether the action is supported for the current game state.
    /// </summary>
    public bool IsSupported { get; }

    /// <summary>
    /// Gets the URI that should be invoked to trigger the action.
    /// </summary>
    public string? ProtocolUri { get; }

    /// <summary>
    /// Gets the <see cref="ProcessStartInfo"/> configured to launch the protocol URI.
    /// </summary>
    public ProcessStartInfo? ProcessStartInfo { get; }

    /// <summary>
    /// Gets a human-friendly error message describing why the action is unavailable.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a supported action using the provided protocol URI.
    /// </summary>
    /// <param name="protocolUri">The URI that should be invoked.</param>
    /// <returns>A <see cref="GameLaunchAction"/> with a ready to use <see cref="ProcessStartInfo"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="protocolUri"/> is null or whitespace.</exception>
    public static GameLaunchAction Supported(string protocolUri)
    {
        if (string.IsNullOrWhiteSpace(protocolUri))
        {
            throw new ArgumentException("Protocol URI must be provided.", nameof(protocolUri));
        }

        return new GameLaunchAction(true, protocolUri, CreateProcessStartInfo(protocolUri), null);
    }

    /// <summary>
    /// Creates an unsupported action with an optional error message.
    /// </summary>
    /// <param name="errorMessage">The message describing why the action is not supported.</param>
    /// <returns>A <see cref="GameLaunchAction"/> that cannot be executed.</returns>
    public static GameLaunchAction Unsupported(string? errorMessage = null)
    {
        return new GameLaunchAction(false, null, null, errorMessage);
    }

    private static ProcessStartInfo CreateProcessStartInfo(string protocolUri)
    {
        return new ProcessStartInfo(protocolUri)
        {
            UseShellExecute = true,
        };
    }
}
