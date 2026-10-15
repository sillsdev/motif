using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>Reads lexical process graphs by identity, including class, owner, input/output order and references.</summary>
public static class AffixProcessSnapshots
{
    public static IReadOnlyDictionary<CanonicalId, ObjectSnapshot> Read(LcmCache cache)
    {
        var result = new Dictionary<CanonicalId, ObjectSnapshot>();
        foreach (var entry in cache.LangProject.LexDbOA.Entries)
        {
            var processes = entry.AlternateFormsOS.OfType<IMoAffixProcess>().ToList();
            if (entry.LexemeFormOA is IMoAffixProcess lexeme) processes.Add(lexeme);
            if (processes.Count == 0) continue;
            Add(entry, LexEntryRelationsSnapshotter.Snapshot(cache, entry), LexiconOwningSnapshots.Snapshot(cache, entry),
                LexEntryAlternateFormsSnapshotter.Snapshot(cache, entry));
            foreach (var sense in entry.SensesOS)
                Add(sense, LexSenseSnapshotter.Snapshot(cache, sense), LexSenseAuthoringSnapshotter.Snapshot(cache, sense),
                    LexSenseRelationsSnapshotter.Snapshot(cache, sense));
            foreach (var msa in entry.MorphoSyntaxAnalysesOC)
                Add(msa, msa switch
                {
                    IMoInflAffMsa inflectional => MoInflAffMsaRelationsSnapshotter.Snapshot(cache, inflectional),
                    IMoDerivAffMsa derivational => MoDerivAffMsaRelationsSnapshotter.Snapshot(cache, derivational),
                    IMoUnclassifiedAffixMsa unclassified => MoUnclassifiedAffixMsaRelationsSnapshotter.Snapshot(cache, unclassified),
                    _ => ObjectSnapshot.Empty(CanonicalId.FromGuid(msa.Guid)),
                });
            foreach (var process in processes)
            {
                Add(process, MoFormSnapshotter.Snapshot(cache, process), MoFormRelationsSnapshotter.Snapshot(cache, process),
                    Members(process, SnapshotFields.MoAffixProcessInput, process.InputOS),
                    Members(process, SnapshotFields.MoAffixProcessOutput, process.OutputOS));
                foreach (var input in process.InputOS)
                    Add(input, input switch
                    {
                        IPhSimpleContextSeg segment => PhSimpleContextSegRelationsSnapshotter.Snapshot(cache, segment),
                        IPhSimpleContextNC naturalClass => PhSimpleContextNCRelationsSnapshotter.Snapshot(cache, naturalClass),
                        _ => ObjectSnapshot.Empty(CanonicalId.FromGuid(input.Guid)),
                    });
                foreach (var output in process.OutputOS)
                    Add(output, output switch
                    {
                        IMoCopyFromInput copy => MoCopyFromInputRelationsSnapshotter.Snapshot(cache, copy),
                        IMoInsertPhones insert => Members(insert, SnapshotFields.MoInsertPhonesContent, insert.ContentRS),
                        _ => ObjectSnapshot.Empty(CanonicalId.FromGuid(output.Guid)),
                    });
            }
        }
        return result;

        void Add(ICmObject value, params ObjectSnapshot[] parts)
        {
            var id = CanonicalId.FromGuid(value.Guid);
            var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["class"] = new Dictionary<string, string> { ["value"] = value.ClassName },
            };
            if (value.Owner is { } owner)
                fields["owner"] = new Dictionary<string, string> { ["ref"] = CanonicalId.FromGuid(owner.Guid).Value };
            foreach (var part in parts)
                foreach (var (field, alternatives) in part.AlternativesFields) fields[field] = alternatives;
            result[id] = new ObjectSnapshot(id, fields);
        }
    }

    internal static void AppendGraphEffects(IReadOnlyDictionary<CanonicalId, ObjectSnapshot> before,
        LcmCache cache, List<ExpectedEffect> effects)
    {
        var captured = effects.Select(effect => (effect.CanonicalId, effect.Field)).ToHashSet();
        foreach (var effect in SoundSystemSnapshots.Compare(before, Read(cache)))
            if (!captured.Contains((effect.CanonicalId, effect.Field))) effects.Add(effect);
    }

    private static ObjectSnapshot Members<T>(ICmObject value, string field, IEnumerable<T> members) where T : ICmObject =>
        new(CanonicalId.FromGuid(value.Guid), new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [field] = members.Select((member, index) => (member, index)).ToDictionary(
                pair => pair.index.ToString(CultureInfo.InvariantCulture), pair => CanonicalId.FromGuid(pair.member.Guid).Value),
        });
}
