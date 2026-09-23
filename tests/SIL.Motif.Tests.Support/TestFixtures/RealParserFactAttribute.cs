using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>Skips a test that needs a genuine parser, not merely a parser-shaped process.</summary>
/// <remarks>
/// A fake cannot serve these. They assert that every morpheme the parser names is an object the project
/// contains, and that the fallback engine agrees on which words parse — both of which a fake would satisfy
/// by echoing back whatever the test told it, turning the assertion into a tautology. A green test proving
/// nothing is worse than a skipped one, because it looks like coverage.
/// Assertions about Motif's own plumbing take a fake instead; see <c>GrammarCoverageProvenanceTests</c>.
/// </remarks>
public sealed class RealParserFactAttribute : FactAttribute
{
    public RealParserFactAttribute()
    {
        if (PanGlossExecutable.TryLocate() is null)
            Skip = $"pangloss not found. Build it (cargo build --release -p pg-cli) or set " +
                   $"{PanGlossExecutable.PathVariable}.";
    }
}
