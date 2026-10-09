using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class PanGlossCpuBudgetTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(20, 10)]
    public void BatchThreadCountUsesAtMostHalfTheProcessors(int processorCount, int expectedThreads)
    {
        Assert.Equal(expectedThreads, PanGlossCpuBudget.BatchThreadCount(processorCount));
    }

    [Theory]
    [InlineData(1, "50000 100000")]
    [InlineData(3, "150000 100000")]
    [InlineData(20, "1000000 100000")]
    public void LinuxCpuMaxLimitsAJobToHalfTheProcessors(int processorCount, string expected)
    {
        Assert.Equal(expected, PanGlossCpuBudget.LinuxCpuMax(processorCount));
    }

    [Fact]
    public void CpuHardCapIsHalfOfTotalMachineCapacity()
    {
        Assert.Equal(5000, PanGlossCpuBudget.CpuRateHardCapBasisPoints);
    }
}
