using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class PanGlossMemoryLimitSettingTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsetDefaultsToThreeQuartersOfPhysicalMemory(string? setting)
    {
        Assert.Equal(12 * GiB, PanGlossMemoryLimit.Resolve(setting, 16 * GiB));
    }

    [Fact]
    public void AByteCountIsTakenAsGiven()
    {
        Assert.Equal(12884901888UL, PanGlossMemoryLimit.Resolve("12884901888", 16 * GiB));
    }

    [Theory]
    [InlineData("12GiB", 12UL * GiB)]
    [InlineData("12gib", 12UL * GiB)]
    [InlineData("512MiB", 512UL * 1024 * 1024)]
    [InlineData("64KiB", 64UL * 1024)]
    [InlineData("1TiB", 1024UL * GiB)]
    [InlineData(" 3GiB ", 3UL * GiB)]
    public void ABinarySuffixScalesTheCount(string setting, ulong expected)
    {
        Assert.Equal(expected, PanGlossMemoryLimit.Resolve(setting, 16 * GiB));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData(" None ")]
    public void NoneMeansNoCeilingAtAll(string setting)
    {
        Assert.Null(PanGlossMemoryLimit.Resolve(setting, 16 * GiB));
    }

    [Theory]
    [InlineData("lots")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("12GB")]
    [InlineData("1.5GiB")]
    [InlineData("GiB")]
    [InlineData("99999999999999TiB")]
    [InlineData("18446744073709551616")]
    public void AnInvalidValueIsRefusedNamingTheVariableTheValueAndTheAcceptedForms(string setting)
    {
        var refusal = Assert.Throws<PanGlossMemoryLimitSettingException>(() =>
            PanGlossMemoryLimit.Resolve(setting, 16 * GiB));

        Assert.Contains("MOTIF_PARSER_MEMORY_LIMIT", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"'{setting}'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("none", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("GiB", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicalMemoryIsReportedAndTheDefaultIsAQuarterBelowIt()
    {
        var physical = PanGlossMemoryLimit.PhysicalMemoryBytes();

        Assert.True(physical > 0);
        Assert.Equal(physical / 4 * 3, PanGlossMemoryLimit.Resolve(null, physical));
    }

    [Fact]
    public void ALimitIsDescribedInGibibytes()
    {
        Assert.Equal("12 GiB", PanGlossMemoryLimit.Format(12 * GiB));
        Assert.Equal("1.5 GiB", PanGlossMemoryLimit.Format(GiB + GiB / 2));
    }
}
