using System.Collections.Generic;

namespace SteamBacklogPicker.UI.Journey;

public static class JourneyParityChecklist
{
    public static IReadOnlyList<JourneyStep> DefaultFlow { get; } = new[]
    {
        JourneyStep.OpenApplication,
        JourneyStep.LoadLibrary,
        JourneyStep.FilterLibrary,
        JourneyStep.DrawGame,
        JourneyStep.LaunchGame,
        JourneyStep.ShowNotification,
        JourneyStep.CheckForUpdates,
    };
}
