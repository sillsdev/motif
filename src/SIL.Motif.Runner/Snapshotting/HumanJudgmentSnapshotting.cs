using System.Collections.Generic;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>
/// Reads a Notebook record's reserved judgment field into the semantic snapshot. The field is a project-local
/// custom field, so its id is resolved by name at run time rather than generated from the LibLCM model.
/// </summary>
internal static class HumanJudgmentSnapshotting
{
    /// <summary>
    /// Returns no alternatives when the project has no reserved field, so a project that never initialized
    /// judgments keeps its digest, pinned by `ProjectWithoutTheJudgmentFieldEmitsNoJudgmentAlternatives`.
    /// An ambiguous or incompatible field throws, pinned by `AnAmbiguousJudgmentFieldRefusesTheSnapshot`.
    /// </summary>
    public static Dictionary<string, string> ReadRecord(LcmCache cache, IRnGenericRec record)
    {
        var field = HumanJudgmentCustomFieldHandler.TryResolveField(cache);
        if (field is null) return new();

        return StringFieldSnapshotting.ReadAlternatives(cache,
            cache.DomainDataByFlid.get_StringProp(record.Hvo, field.Value));
    }
}
