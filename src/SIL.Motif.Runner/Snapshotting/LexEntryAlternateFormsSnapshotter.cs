using System.Collections.Generic;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;
using SIL.LCModel;

namespace SIL.Motif.Runner.Snapshotting;

public static class LexEntryAlternateFormsSnapshotter
{
    public static ObjectSnapshot Snapshot(LcmCache cache, ILexEntry entry)
    {
        _ = cache;
        var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var alternates = ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS);
        if (alternates.Count > 0) fields[SnapshotFields.LexEntryAlternateForms] = alternates;
        return new ObjectSnapshot(CanonicalId.FromGuid(entry.Guid), fields);
    }
}
