using System;
using System.Collections.Generic;
using System.IO;

namespace SteamTestUtilities.ValveFormat;

public static class VdfFixtureLoader
{
    private static readonly Lazy<string> FixtureRoot = new(() =>
    {
        var assemblyLocation = typeof(VdfFixtureLoader).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation);
        if (directory is null)
        {
            throw new InvalidOperationException("Unable to resolve the Steam test utilities directory.");
        }

        return Path.Combine(directory, "Fixtures");
    });

    public static string RootDirectory => FixtureRoot.Value;

    public static string GetFixturePath(string relativePath)
    {
        var fullPath = Path.Combine(FixtureRoot.Value, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"VDF fixture '{relativePath}' was not found.", fullPath);
        }

        return fullPath;
    }

    public static string ReadTextFixture(string relativePath)
    {
        var path = GetFixturePath(relativePath);
        return File.ReadAllText(path);
    }

    public static byte[] ReadBinaryFixture(string relativePath)
    {
        var path = GetFixturePath(relativePath);
        return File.ReadAllBytes(path);
    }

    public static IReadOnlyDictionary<string, string> ReadKeyValueFixture(string relativePath)
    {
        var content = ReadTextFixture(relativePath);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }
}
