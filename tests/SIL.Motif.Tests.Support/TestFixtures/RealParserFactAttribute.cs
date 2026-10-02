using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>Runs Motif integration checks against the pinned PanGloss release.</summary>
/// <remarks>
/// These checks exercise Motif's real parser requests and inspect returned identities, analyses, trace data,
/// and saved evidence. They verify Motif's integration with a released parser; PanGloss owns parser
/// conformance. A missing local executable keeps the checks skipped and makes parser validation incomplete.
/// Release validation supplies and verifies the pinned artifact and checks for skips caused by its absence.
/// </remarks>
public sealed class RealParserFactAttribute : FactAttribute
{
    public RealParserFactAttribute()
    {
        if (PanGlossExecutable.TryLocate() is null)
            Skip = $"pangloss not found; parser validation incomplete. Build it (cargo build --release -p pg-cli) or set " +
                   $"{PanGlossExecutable.PathVariable}.";
    }
}
