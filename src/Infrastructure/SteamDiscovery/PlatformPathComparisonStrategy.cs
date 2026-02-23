namespace SteamDiscovery;

public interface IPathComparisonStrategy
{
    StringComparer Comparer { get; }

    StringComparison Comparison { get; }

    bool Equals(string? left, string? right);
}

public sealed class PlatformPathComparisonStrategy : IPathComparisonStrategy
{
    public PlatformPathComparisonStrategy(IPlatformProvider platformProvider)
    {
        if (platformProvider is null)
        {
            throw new ArgumentNullException(nameof(platformProvider));
        }

        var isWindows = platformProvider.IsWindows();
        Comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Comparer = isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    public StringComparer Comparer { get; }

    public StringComparison Comparison { get; }

    public bool Equals(string? left, string? right) => string.Equals(left, right, Comparison);
}
