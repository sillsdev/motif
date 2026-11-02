using System.Collections.Generic;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;
using SIL.LCModel;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>
/// Projects the owning links from a derivational affix to its required (From) and resulting (To) feature structures;
/// the structures' own specifications are projected by the feature-structure snapshotters.
/// </summary>
public static class MoDerivAffMsaMsFeaturesSnapshotter
{
    public static ObjectSnapshot Snapshot(LcmCache cache, IMoDerivAffMsa msa)
    {
        _ = cache;
        var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var from = ReferenceFieldSnapshotting.ReadAlternatives(msa.FromMsFeaturesOA);
        if (from.Count > 0) fields[SnapshotFields.MoDerivAffMsaFromMsFeatures] = from;
        var to = ReferenceFieldSnapshotting.ReadAlternatives(msa.ToMsFeaturesOA);
        if (to.Count > 0) fields[SnapshotFields.MoDerivAffMsaToMsFeatures] = to;
        return new ObjectSnapshot(CanonicalId.FromGuid(msa.Guid), fields);
    }
}
