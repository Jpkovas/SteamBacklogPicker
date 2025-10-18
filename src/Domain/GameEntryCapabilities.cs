using System;
using System.Collections.Generic;
using System.Linq;

namespace Domain;

public static class GameEntryCapabilities
{
    public static bool SupportsSinglePlayer(GameEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.StoreCategoryIds.Any(id => id == 2);
    }

    public static bool SupportsMultiplayer(GameEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.StoreCategoryIds.Any(id => id is 1 or 9 or 38 or 48 or 49);
    }

    public static bool SupportsVirtualReality(GameEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        foreach (var category in entry.StoreCategoryIds)
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

    public static bool MatchesMoodTags(GameEntry entry, IReadOnlyCollection<string> moodTags)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(moodTags);

        if (moodTags.Count == 0)
        {
            return true;
        }

        var tagSet = moodTags as ISet<string> ?? new HashSet<string>(moodTags, StringComparer.OrdinalIgnoreCase);
        var gameTags = entry.Tags;
        if (gameTags is null)
        {
            return false;
        }

        foreach (var tag in gameTags)
        {
            if (tag is null)
            {
                continue;
            }

            if (tagSet.Contains(tag))
            {
                return true;
            }

            var trimmed = tag.Trim();
            if (trimmed.Length > 0 && !ReferenceEquals(trimmed, tag) && tagSet.Contains(trimmed))
            {
                return true;
            }
        }

        return false;
    }
}
