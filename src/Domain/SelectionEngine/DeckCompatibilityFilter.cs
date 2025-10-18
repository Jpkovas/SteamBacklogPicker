using System;

namespace Domain.Selection;

[Flags]
public enum DeckCompatibilityFilter
{
    None = 0,
    Unknown = 1 << 0,
    Verified = 1 << 1,
    Playable = 1 << 2,
    Unsupported = 1 << 3,
    All = Unknown | Verified | Playable | Unsupported,
}
