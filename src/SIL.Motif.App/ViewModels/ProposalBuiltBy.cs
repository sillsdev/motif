using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Finds the changes in the open Proposal that create the entities a Trial reading is built from.</summary>
public static class ProposalBuiltBy
{
    /// <summary>Names every Dry Run operation whose created entity one of the reading's morphs uses.</summary>
    /// <param name="trial">The Trial whose Dry Run lists the Proposal's operations.</param>
    /// <param name="reading">The reading the Trial built for a word.</param>
    /// <returns>One entry per creating operation, in the Proposal's declared order.</returns>
    public static IReadOnlyList<BuiltByEntry> For(TrialEvidence trial, ParseAnalysis reading)
    {
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(reading);
        if (trial.Trial.DryRun is not { } dryRun) return [];
        var used = reading.Morphs.SelectMany(morph => new[] { morph.Form, morph.Msa, morph.InflType })
            .OfType<string>().Select(Portable).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return dryRun.Operations.Where(operation => operation.EntityId is { } id && used.Contains(id))
            .Select(operation => new BuiltByEntry(operation.OperationId, ChangeVocabulary.Of(operation.Kind).Title)).ToArray();
    }

    private static string? Portable(string guid) =>
        Guid.TryParse(guid, out var parsed) ? CanonicalId.FromGuid(parsed).Value : null;
}
