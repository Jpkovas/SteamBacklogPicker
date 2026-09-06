using Xunit;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "Requires Linux process and filesystem update semantics.";
        }
    }
}
