namespace SteamBacklogPicker.UI.Services.Launch;

/// <summary>
/// Aggregates launch and installation metadata for a specific game selection.
/// </summary>
public sealed record class GameLaunchOptions
{
    public GameLaunchOptions(
        GameLaunchAction launch,
        GameLaunchAction install)
    {
        Launch = launch ?? GameLaunchAction.Unsupported();
        Install = install ?? GameLaunchAction.Unsupported();
    }

    /// <summary>
    /// Gets a placeholder instance that represents an unknown game.
    /// </summary>
    public static GameLaunchOptions Empty { get; } = new(
        GameLaunchAction.Unsupported(),
        GameLaunchAction.Unsupported());

    /// <summary>
    /// Gets the launch action metadata for the current game.
    /// </summary>
    public GameLaunchAction Launch { get; }

    /// <summary>
    /// Gets the installation action metadata for the current game.
    /// </summary>
    public GameLaunchAction Install { get; }
}
