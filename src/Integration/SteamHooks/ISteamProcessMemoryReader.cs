namespace SteamBacklogPicker.Integration.SteamHooks;

public interface ISteamProcessMemoryReader : IDisposable
{
    bool TryReadMemory(nuint address, int readLength, out ReadOnlyMemory<byte> data);
}
