using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>Runs representative-scale measurements when MOTIF_SLOW_SCALE is set to 1.</summary>
public sealed class SlowScaleFactAttribute : FactAttribute
{
    public SlowScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_SLOW_SCALE") != "1")
            Skip = "Set MOTIF_SLOW_SCALE=1 to run representative-scale measurements.";
    }
}
