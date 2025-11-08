using Domain;

namespace SteamBacklogPicker.UI.Services;

/// <summary>
/// Resolves storefront-specific launch and installation metadata for games.
/// </summary>
public interface IGameLaunchService
{
    /// <summary>
    /// Builds storefront-specific launch metadata for the provided <paramref name="game"/>.
    /// </summary>
    /// <param name="game">The game entry to evaluate.</param>
    /// <returns>The resolved <see cref="GameLaunchOptions"/>.</returns>
    GameLaunchOptions GetLaunchOptions(GameEntry game);
}
