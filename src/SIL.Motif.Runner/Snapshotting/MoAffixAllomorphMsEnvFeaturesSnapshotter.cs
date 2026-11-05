using System.Collections.Generic;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;
using SIL.LCModel;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>
/// Projects the owning link from an allomorph to its Required Features structure;
/// the structure's own specifications are projected by the feature-structure snapshotters.
/// </summary>
public static class MoAffixAllomorphMsEnvFeaturesSnapshotter
{
    public static ObjectSnapshot Snapshot(LcmCache cache, IMoAffixAllomorph form)
    {
        _ = cache;
        var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var structure = ReferenceFieldSnapshotting.ReadAlternatives(form.MsEnvFeaturesOA);
        if (structure.Count > 0) fields[SnapshotFields.MoAffixAllomorphMsEnvFeatures] = structure;
        return new ObjectSnapshot(CanonicalId.FromGuid(form.Guid), fields);
    }
}
