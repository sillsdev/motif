using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.Responses;

/// <summary>A compact summary of one operation list and its observed effects.</summary>
/// <param name="OperationCount">The number of Proposal operations.</param>
/// <param name="OperationsByKind">The Proposal operation count for each kind.</param>
/// <param name="EffectCount">The number of observed effects.</param>
/// <param name="Warnings">Warnings reported with this result.</param>
/// <param name="ProjectHashBefore">The semantic project hash before the work, when recorded.</param>
/// <param name="ProjectHashAfter">The semantic project hash after the work, when recorded.</param>
/// <param name="EffectsFile">The file containing the complete effects list, when written.</param>
public sealed record OperationEffectSummary(
    int OperationCount,
    IReadOnlyDictionary<string, int> OperationsByKind,
    int EffectCount,
    IReadOnlyList<string> Warnings,
    string? ProjectHashBefore,
    string? ProjectHashAfter,
    string? EffectsFile)
{
    /// <summary>The total number of warnings before presentation limits.</summary>
    public int WarningCount { get; init; } = Warnings.Count;

    /// <summary>The number of warnings omitted from the previews.</summary>
    public int WarningsOmitted => WarningCount - Warnings.Count;

    /// <summary>Builds sorted counts from operation kinds and the supplied result metadata.</summary>
    public static OperationEffectSummary Create(IEnumerable<string> operationKinds, int effectCount,
        IReadOnlyList<string>? warnings = null, string? projectHashBefore = null,
        string? projectHashAfter = null, string? effectsFile = null)
    {
        var counts = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var kind in operationKinds)
        {
            counts.TryGetValue(kind, out var count);
            counts[kind] = count + 1;
        }
        var allWarnings = warnings ?? [];
        var shown = allWarnings.Take(10).Select(warning =>
        {
            var text = warning.Length > 240 ? warning[..239] + "…" : warning;
            return new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        }).ToArray();
        return new OperationEffectSummary(counts.Values.Sum(), counts, effectCount, shown,
            projectHashBefore, projectHashAfter, effectsFile) { WarningCount = allWarnings.Count };
    }
}
