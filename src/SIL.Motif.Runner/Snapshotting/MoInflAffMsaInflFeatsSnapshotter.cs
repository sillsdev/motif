using System.Collections.Generic;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;
using SIL.LCModel;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>
/// Projects the owning link from an inflectional affix's grammatical info to its required inflection feature structure;
/// the structure's own specifications are projected by the feature-structure snapshotters.
/// </summary>
public static class MoInflAffMsaInflFeatsSnapshotter
{
    public static ObjectSnapshot Snapshot(LcmCache cache, IMoInflAffMsa msa)
    {
        _ = cache;
        var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var structure = ReferenceFieldSnapshotting.ReadAlternatives(msa.InflFeatsOA);
        if (structure.Count > 0) fields[SnapshotFields.MoInflAffMsaInflFeats] = structure;
        return new ObjectSnapshot(CanonicalId.FromGuid(msa.Guid), fields);
    }
}
