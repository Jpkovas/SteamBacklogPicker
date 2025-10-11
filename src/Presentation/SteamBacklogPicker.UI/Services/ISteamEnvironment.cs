using SteamClientAdapter;

namespace SteamBacklogPicker.UI.Services;

public interface ISteamEnvironment
{
    string GetSteamDirectory();

    void TryInitializeSteamApi(ISteamClientAdapter adapter);
}
