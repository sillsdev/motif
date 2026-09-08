using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.Assess;

/// <summary>What PanGloss's <c>stats</c> command wrote to each stream, untouched.</summary>
/// <param name="StandardOutput">The rows or text PanGloss produced, in whatever form the caller's forwarded arguments requested.</param>
/// <param name="StandardError">Whatever PanGloss wrote to its error stream, kept even on success.</param>
public sealed record PanGlossStatsOutput(string StandardOutput, string StandardError);

/// <summary>
/// Passes a statistics query through to PanGloss's own <c>stats</c> command, verbatim.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam design decision 5 depends on: Motif contributes exactly two arguments — which grammar
/// and which stats cache — and forwards everything else exactly as the caller supplied it. An implementation
/// must never tokenize, normalize, reorder, or otherwise interpret <c>forwardedArguments</c>; a new PanGloss
/// filter or grouping is then usable through Motif the day it ships, with no change here.
/// </para>
/// <para>
/// The interface exists so a caller can be exercised without launching anything.
/// <see cref="PanGlossStatsQueryProcess"/> is the implementation that really launches the parser.
/// </para>
/// </remarks>
public interface IPanGlossStatsQuery
{
    /// <summary>
    /// Queries PanGloss's statistics for <paramref name="grammarPath"/> against <paramref name="cachePath"/>,
    /// appending <paramref name="forwardedArguments"/> after PanGloss's own <c>--cache</c> option, in order
    /// and unchanged. When <paramref name="governor"/> is supplied, the launched process is contained by it
    /// immediately after starting; a caller with no governor passes null and the process runs uncontained.
    /// </summary>
    Task<PanGlossStatsOutput> QueryAsync(string grammarPath, string cachePath,
        IReadOnlyList<string> forwardedArguments, CancellationToken cancellationToken,
        IParserProcessGovernor? governor = null);
}
