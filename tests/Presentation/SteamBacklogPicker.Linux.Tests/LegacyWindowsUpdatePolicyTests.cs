using FluentAssertions;
using SteamBacklogPicker.UI.Services.Updates;
using Xunit;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class LegacyWindowsUpdatePolicyTests : IDisposable
{
    private readonly string? _originalValue;

    public LegacyWindowsUpdatePolicyTests()
    {
        _originalValue = Environment.GetEnvironmentVariable(LegacyWindowsUpdatePolicy.EnableLegacyUpdateEnvironmentVariable);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    public void IsSquirrelUpdateEnabled_ShouldRequireExplicitOptIn(string? value, bool expected)
    {
        Environment.SetEnvironmentVariable(LegacyWindowsUpdatePolicy.EnableLegacyUpdateEnvironmentVariable, value);

        var result = LegacyWindowsUpdatePolicy.IsSquirrelUpdateEnabled();

        result.Should().Be(expected);
    }

    public void Dispose()
        => Environment.SetEnvironmentVariable(LegacyWindowsUpdatePolicy.EnableLegacyUpdateEnvironmentVariable, _originalValue);
}
