using System;
using System.Net.NetworkInformation;

namespace SteamBacklogPicker.UI.Services;

public sealed class NetworkStatusService : INetworkStatusService
{
    public bool IsOffline()
    {
        try
        {
            return !NetworkInterface.GetIsNetworkAvailable();
        }
        catch (NetworkInformationException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
