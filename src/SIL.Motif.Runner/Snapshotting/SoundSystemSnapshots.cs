using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>
/// Sound-system semantic state keyed by exact portable identity. Ownership, concrete class and sequence
/// positions participate, so new objects and changed membership remain distinct from text edits.
/// </summary>
public static class SoundSystemSnapshots
{
    public static IReadOnlyDictionary<CanonicalId, ObjectSnapshot> Read(LcmCache cache)
    {
        var objects = new Dictionary<CanonicalId, ObjectSnapshot>();
        var active = new HashSet<Guid>();
        var data = cache.LangProject.PhonologicalDataOA;
        Add(data);
        foreach (var set in data.PhonemeSetsOS)
        {
            Add(set);
            foreach (var terminal in set.PhonemesOC.Cast<IPhTerminalUnit>().Concat(set.BoundaryMarkersOC))
            {
                Add(terminal);
                foreach (var code in terminal.CodesOS) Add(code);
                if (terminal is IPhPhoneme phoneme) Structure(phoneme.FeaturesOA);
            }
        }
        foreach (var naturalClass in data.NaturalClassesOS)
        {
            Add(naturalClass);
            if (naturalClass is IPhNCFeatures features) Structure(features.FeaturesOA);
        }
        foreach (var environment in data.EnvironmentsOS) Add(environment);
        foreach (var rule in data.PhonRulesOS)
        {
            Add(rule);
            if (rule is IPhRegularRule regular)
            {
                foreach (var context in rule.StrucDescOS) Add(context);
                foreach (var rhs in regular.RightHandSidesOS) Add(rhs);
            }
        }
        foreach (var context in data.ContextsOS) Add(context);
        return objects;

        void Structure(IFsFeatStruc? structure)
        {
            if (structure is null) return;
            Add(structure);
            foreach (var spec in structure.FeatureSpecsOC) Add(spec);
        }

        void Add(ICmObject value)
        {
            var id = CanonicalId.FromGuid(value.Guid);
            if (objects.ContainsKey(id) || !active.Add(value.Guid)) return;
            var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["class"] = new Dictionary<string, string> { ["value"] = value.ClassName },
            };
            if (value.Owner is { } owner) fields["owner"] = new Dictionary<string, string>
            {
                ["ref"] = CanonicalId.FromGuid(owner.Guid).Value,
            };
            switch (value)
            {
                case IPhPhonData phonData:
                    Members(SnapshotFields.PhPhonDataPhonemeSets, phonData.PhonemeSetsOS, true);
                    Members(SnapshotFields.PhPhonDataNaturalClasses, phonData.NaturalClassesOS, true);
                    Members(SnapshotFields.PhPhonDataEnvironments, phonData.EnvironmentsOS, true);
                    Members(SnapshotFields.PhPhonDataPhonRules, phonData.PhonRulesOS, true);
                    Members(SnapshotFields.PhPhonDataContexts, phonData.ContextsOS, false);
                    break;
                case IPhPhonemeSet set:
                    Members(SnapshotFields.PhPhonemeSetPhonemes, set.PhonemesOC, false);
                    Members(SnapshotFields.PhPhonemeSetBoundaryMarkers, set.BoundaryMarkersOC, false);
                    break;
                case IPhTerminalUnit terminal:
                    Fields(PhTerminalUnitAuthoringSnapshotter.Snapshot(cache, terminal));
                    Members(SnapshotFields.PhTerminalUnitCodes, terminal.CodesOS, true);
                    if (terminal is IPhPhoneme p) Reference(SnapshotFields.PhPhonemeFeatures, p.FeaturesOA);
                    break;
                case IPhCode code:
                    Fields(PhCodeSnapshotter.Snapshot(cache, code));
                    break;
                case IPhNaturalClass naturalClass:
                    Fields(PhNaturalClassSnapshotter.Snapshot(cache, naturalClass));
                    Fields(PhNaturalClassAuthoringSnapshotter.Snapshot(cache, naturalClass));
                    if (naturalClass is IPhNCSegments segments) Members(SnapshotFields.PhNCSegmentsSegments, segments.SegmentsRC, false);
                    if (naturalClass is IPhNCFeatures features) Reference(SnapshotFields.PhNCFeaturesFeatures, features.FeaturesOA);
                    break;
                case IPhEnvironment environment:
                    Fields(PhEnvironmentAuthoringSnapshotter.Snapshot(cache, environment));
                    if (environment.StringRepresentation is { Length: > 0 } text)
                        fields[SnapshotFields.PhEnvironmentStringRepresentation] = new Dictionary<string, string>
                        {
                            ["text"] = text.get_NormalizedForm(FwNormalizationMode.knmNFSC).Text,
                        };
                    break;
                case IPhSegRuleRHS rhs:
                    Fields(PhSegRuleRHSRelationsSnapshotter.Snapshot(cache, rhs));
                    Members(SnapshotFields.PhSegRuleRHSStrucChange, rhs.StrucChangeOS, true);
                    Reference(SnapshotFields.PhSegRuleRHSLeftContext, rhs.LeftContextOA);
                    Reference(SnapshotFields.PhSegRuleRHSRightContext, rhs.RightContextOA);
                    foreach (var context in rhs.StrucChangeOS) Add(context);
                    if (rhs.LeftContextOA is { } leftContext) Add(leftContext);
                    if (rhs.RightContextOA is { } rightContext) Add(rightContext);
                    break;
                case IPhSegmentRule rule:
                    Fields(PhSegmentRuleSnapshotter.Snapshot(cache, rule));
                    Fields(PhSegmentRuleAuthoringSnapshotter.Snapshot(cache, rule));
                    Fields(PhSegmentRuleDirectionSnapshotter.Snapshot(cache, rule));
                    if (rule is IPhRegularRule regular)
                        Members(SnapshotFields.PhRegularRuleRightHandSides, regular.RightHandSidesOS, true);
                    Members(SnapshotFields.PhSegmentRuleStrucDesc, rule.StrucDescOS, true);
                    break;
                case IPhSimpleContextSeg segmentContext:
                    Fields(PhSimpleContextSegRelationsSnapshotter.Snapshot(cache, segmentContext));
                    break;
                case IPhSimpleContextNC classContext:
                    Fields(PhSimpleContextNCRelationsSnapshotter.Snapshot(cache, classContext));
                    break;
                case IPhSimpleContextBdry boundaryContext:
                    Fields(PhSimpleContextBdryRelationsSnapshotter.Snapshot(cache, boundaryContext));
                    break;
                case IPhSequenceContext sequenceContext:
                    Fields(PhSequenceContextRelationsSnapshotter.Snapshot(cache, sequenceContext));
                    foreach (var member in sequenceContext.MembersRS) Add(member);
                    break;
                case IFsFeatStruc structure:
                    Members(SnapshotFields.FsFeatStrucFeatureSpecs, structure.FeatureSpecsOC, false);
                    break;
                case IFsFeatureSpecification spec:
                    Reference(SnapshotFields.FsFeatureSpecificationFeature, spec.FeatureRA);
                    if (spec is IFsClosedValue closed) Reference(SnapshotFields.FsClosedValueValue, closed.ValueRA);
                    break;
            }
            objects.Add(id, new ObjectSnapshot(id, fields));
            active.Remove(value.Guid);

            void Fields(ObjectSnapshot snapshot)
            {
                foreach (var (key, field) in snapshot.AlternativesFields) fields[key] = field;
            }
            void Reference(string key, ICmObject? reference)
            {
                if (reference is not null) fields[key] = new Dictionary<string, string> { ["ref"] = CanonicalId.FromGuid(reference.Guid).Value };
            }
            void Members<T>(string key, IEnumerable<T> members, bool ordered) where T : ICmObject
            {
                var ids = members.Select(m => CanonicalId.FromGuid(m.Guid).Value);
                if (!ordered) ids = ids.Order(StringComparer.Ordinal);
                var map = ids.Select((id, index) => (id, index)).ToDictionary(p => p.index.ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.id);
                if (map.Count != 0) fields[key] = map;
            }
        }
    }

    /// <summary>Compares exact identities and normalized fields; no label or form matching is performed.</summary>
    public static IReadOnlyList<ExpectedEffect> Compare(IReadOnlyDictionary<CanonicalId, ObjectSnapshot> before,
        IReadOnlyDictionary<CanonicalId, ObjectSnapshot> after)
    {
        var effects = new List<ExpectedEffect>();
        foreach (var id in before.Keys.Union(after.Keys).OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            var oldFields = before.TryGetValue(id, out var oldValue) ? oldValue.AlternativesFields : ObjectSnapshot.Empty(id).AlternativesFields;
            var newFields = after.TryGetValue(id, out var newValue) ? newValue.AlternativesFields : ObjectSnapshot.Empty(id).AlternativesFields;
            foreach (var field in oldFields.Keys.Union(newFields.Keys).Order(StringComparer.Ordinal))
            {
                var oldMap = oldFields.TryGetValue(field, out var oldField) ? oldField : new Dictionary<string, string>();
                var newMap = newFields.TryGetValue(field, out var newField) ? newField : new Dictionary<string, string>();
                if (oldMap.Count == newMap.Count && oldMap.All(p => newMap.TryGetValue(p.Key, out var v) && v == p.Value)) continue;
                effects.Add(new(id, field, oldMap, newMap));
            }
        }
        return effects;
    }
}
