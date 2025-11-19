using SteamClientAdapter;

namespace SteamBacklogPicker.UI.Services.Environment;

public interface ISteamEnvironment
{
    string GetSteamDirectory();

    void TryInitializeSteamApi(ISteamClientAdapter adapter);
}
