using SteamClientAdapter;

namespace SteamBacklogPicker.UI.Services.Runtime;

public interface ISteamEnvironment
{
    string GetSteamDirectory();

    void TryInitializeSteamApi(ISteamClientAdapter adapter);
}
