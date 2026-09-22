using System;
using System.Linq;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The <see cref="ProjectAnalysis.Key"/> a parser reading would have if the project held it, so a reading can
/// be compared with the analysis chosen at one occurrence: equal exactly when the two have the same morphs,
/// grammatical info and inflection types, in order (ADR 0027 and ADR 0038).
/// </summary>
public static class ProjectAnalysisKey
{
    /// <summary>The key of <paramref name="reading"/>'s morph bundles, computed the way a stored analysis's is.</summary>
    public static string For(ParseAnalysis reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return AnalysisContent.ComputeDigest(
            reading.Morphs.Select(morph => new MorphBundleContent(morph.Form, morph.Msa, morph.InflType)).ToList());
    }
}
