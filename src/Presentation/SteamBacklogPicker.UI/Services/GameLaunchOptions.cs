namespace SteamBacklogPicker.UI.Services;

/// <summary>
/// Aggregates launch and installation metadata for a specific game selection.
/// </summary>
public sealed record class GameLaunchOptions
{
    public GameLaunchOptions(
        GameLaunchAction launch,
        GameLaunchAction install,
        string? epicAppName,
        string? epicCatalogItemId,
        string? epicCatalogNamespace)
    {
        Launch = launch ?? GameLaunchAction.Unsupported();
        Install = install ?? GameLaunchAction.Unsupported();
        EpicAppName = epicAppName;
        EpicCatalogItemId = epicCatalogItemId;
        EpicCatalogNamespace = epicCatalogNamespace;
    }

    /// <summary>
    /// Gets a placeholder instance that represents an unknown game.
    /// </summary>
    public static GameLaunchOptions Empty { get; } = new(
        GameLaunchAction.Unsupported(),
        GameLaunchAction.Unsupported(),
        null,
        null,
        null);

    /// <summary>
    /// Gets the launch action metadata for the current game.
    /// </summary>
    public GameLaunchAction Launch { get; }

    /// <summary>
    /// Gets the installation action metadata for the current game.
    /// </summary>
    public GameLaunchAction Install { get; }

    /// <summary>
    /// Gets the Epic Games Launcher application name when available.
    /// </summary>
    public string? EpicAppName { get; }

    /// <summary>
    /// Gets the Epic Games catalog item identifier when available.
    /// </summary>
    public string? EpicCatalogItemId { get; }

    /// <summary>
    /// Gets the Epic Games catalog namespace when available.
    /// </summary>
    public string? EpicCatalogNamespace { get; }
}
