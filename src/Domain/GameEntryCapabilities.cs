using System;
using System.Linq;

namespace Domain;

public static class GameEntryCapabilities
{
    public static bool SupportsSinglePlayer(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.StoreCategoryIds.Any(id => id == 2);
    }

    public static bool SupportsMultiplayer(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.StoreCategoryIds.Any(id => id is 1 or 9 or 38 or 48 or 49);
    }

    public static bool SupportsVirtualReality(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        foreach (var category in game.StoreCategoryIds)
        {
            switch (category)
            {
                case 31:
                case 32:
                case 33:
                case 34:
                case 35:
                case 36:
                case 37:
                case 38:
                case 39:
                case 52:
                case 53:
                case 54:
                    return true;
            }
        }

        return false;
    }
}
