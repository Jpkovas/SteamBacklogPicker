namespace SteamBacklogPicker.UI.Services.Updates;

public static class LegacyWindowsUpdatePolicy
{
    public const string EnableLegacyUpdateEnvironmentVariable = "SBP_ENABLE_LEGACY_WINDOWS_UPDATE";

    public static bool IsSquirrelUpdateEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnableLegacyUpdateEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
