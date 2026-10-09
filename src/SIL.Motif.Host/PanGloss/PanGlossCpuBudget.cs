using System.Globalization;

namespace SIL.Motif.Host.PanGloss;

internal static class PanGlossCpuBudget
{
    internal const int CpuRateHardCapBasisPoints = 5000;
    internal const int LinuxCpuQuotaPeriod = 100000;

    internal static int DefaultBatchThreadCount => BatchThreadCount(Environment.ProcessorCount);

    internal static int BatchThreadCount(int processorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processorCount);
        return Math.Max(1, processorCount / 2);
    }

    internal static string LinuxCpuMax(int processorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processorCount);
        var quota = (long)processorCount * LinuxCpuQuotaPeriod / 2;
        return quota.ToString(CultureInfo.InvariantCulture) + " " +
            LinuxCpuQuotaPeriod.ToString(CultureInfo.InvariantCulture);
    }
}
