using Xunit;

namespace SIL.Motif.Tests.QueryScale;

/// <summary>Runs a scale test when the scheduled slow-scale suite is enabled.</summary>
public sealed class SlowScaleFactAttribute : FactAttribute
{
    /// <summary>Creates a scale test fact and skips it outside the slow-scale suite.</summary>
    public SlowScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_SLOW_SCALE") != "1")
            Skip = "Set MOTIF_SLOW_SCALE=1 to run representative-scale measurements.";
    }
}
