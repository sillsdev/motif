using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>Projects entry membership and the senses and grammatical info each entry owns.</summary>
public static class LexiconOwningSnapshots
{
    /// <summary>Projects the lexicon's virtual, unordered entry membership by exact canonical identity.</summary>
    public static ObjectSnapshot Snapshot(LcmCache cache, ILexDb lexicon) =>
        Members(lexicon, (SnapshotFields.LexDbEntries, lexicon.Entries.Cast<ICmObject>(), false));

    /// <summary>Projects the entry's ordered senses and unordered grammatical info.</summary>
    public static ObjectSnapshot Snapshot(LcmCache cache, ILexEntry entry) =>
        Members(entry, (SnapshotFields.LexEntrySenses, entry.SensesOS.Cast<ICmObject>(), true),
            (SnapshotFields.LexEntryMorphoSyntaxAnalyses, entry.MorphoSyntaxAnalysesOC.Cast<ICmObject>(), false));

    private static ObjectSnapshot Members(ICmObject owner,
        params (string Field, IEnumerable<ICmObject> Members, bool Ordered)[] collections)
    {
        var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (field, members, ordered) in collections)
        {
            var identities = members.Select(member => CanonicalId.FromGuid(member.Guid).Value);
            if (!ordered) identities = identities.Order(StringComparer.Ordinal);
            var alternatives = identities.Select((id, index) => (id, index))
                .ToDictionary(pair => pair.index.ToString(CultureInfo.InvariantCulture), pair => pair.id);
            if (alternatives.Count != 0) fields.Add(field, alternatives);
        }
        return new(CanonicalId.FromGuid(owner.Guid), fields);
    }
}
