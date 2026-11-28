using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>The theory form of <see cref="RealParserFactAttribute"/>: skipped when the pinned PanGloss is not found.</summary>
public class RealParserTheoryAttribute : TheoryAttribute
{
    public RealParserTheoryAttribute()
    {
        if (PanGlossExecutable.TryLocate() is null)
            Skip = $"pangloss not found; parser validation incomplete. Build it (cargo build --release -p pg-cli) or set " +
                   $"{PanGlossExecutable.PathVariable}.";
    }
}
