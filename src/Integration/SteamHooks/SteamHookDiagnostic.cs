using System.Collections.Immutable;

namespace SteamBacklogPicker.Integration.SteamHooks;

/// <summary>
/// Represents a structured diagnostic event emitted by Steam hook experimental flows.
/// </summary>
public sealed record class SteamHookDiagnostic(
    string EventName,
    ImmutableDictionary<string, string> Properties)
{
    /// <summary>
    /// Creates a new <see cref="SteamHookDiagnostic"/> from a mutable dictionary.
    /// </summary>
    public static SteamHookDiagnostic Create(string eventName, IDictionary<string, string>? properties = null)
        => new(eventName, properties?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase) ?? ImmutableDictionary<string, string>.Empty);
}
