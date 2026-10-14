using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Apply;

/// <summary>
/// Binds the definitions and ownership an ordinary form relies on,
/// including external references on targets created by the same Proposal. These reads belong only to
/// the footprint: they are prerequisites, never changed effects.
/// </summary>
internal static class AffixAuthoringPrerequisites
{
    private static readonly HashSet<string> ReferenceKinds = new(StringComparer.Ordinal)
    {
        MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses,
        MoStemAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv,
        MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv,
        MoInflAffMsaPartOfSpeechOperationKinds.SetPartOfSpeech,
        MoStemMsaPartOfSpeechOperationKinds.SetPartOfSpeech,
        MoDerivAffMsaFromPartOfSpeechOperationKinds.SetFromPartOfSpeech,
        MoDerivAffMsaToPartOfSpeechOperationKinds.SetToPartOfSpeech,
        MoUnclassifiedAffixMsaPartOfSpeechOperationKinds.SetPartOfSpeech,
        MoStemMsaInflectionClassOperationKinds.SetInflectionClass,
        MoInflAffMsaSlotsOperationKinds.AddRefSlots,
    };

    internal static IReadOnlyList<ExpectedEffect> Read(LcmCache cache, Proposal proposal)
    {
        var relevant = proposal.Operations.Where(operation => ReferenceKinds.Contains(operation.Kind) ||
            operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms ||
            operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm).ToArray();
        if (relevant.Length == 0) return [];
        var minted = proposal.Operations.Where(operation => operation.EntityId is not null)
            .Select(operation => operation.EntityId!.Value.ToGuid()).ToHashSet();
        var stagedReads = new HashSet<Guid>();
        var soundScopeRead = false;
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var snapshots = new Dictionary<Guid, ObjectSnapshot>();
        IReadOnlyDictionary<CanonicalId, ObjectSnapshot>? sound = null;
        foreach (var operation in relevant)
        {
            if (operation.After is { } after)
                foreach (var key in new[] { "member", "ref", "morphType" })
                    if (after.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String &&
                        CanonicalId.TryParse(value.GetString(), out var id)) Include(id.ToGuid());
            if (operation.Target is { } target && !minted.Contains(target.ToGuid()) &&
                repository.IsValidObjectId(target.ToGuid()))
            {
                var owner = repository.GetObject(target.ToGuid());
                if (owner is ILexEntry entry)
                {
                    IncludeForm(entry.LexemeFormOA);
                    foreach (var form in entry.AlternateFormsOS) IncludeForm(form);
                    foreach (var msa in entry.MorphoSyntaxAnalysesOC) IncludeMsa(msa);
                }
                else if (owner is IMoAffixForm form && form.Owner is ILexEntry formEntry)
                    foreach (var msa in formEntry.MorphoSyntaxAnalysesOC) IncludeMsa(msa);
                else if (owner is IMoInflAffMsa msa) IncludeMsa(msa);
            }
        }
        return snapshots.Values.OrderBy(snapshot => snapshot.CanonicalId.Value, StringComparer.Ordinal)
            .SelectMany(snapshot => snapshot.AlternativesFields.Select(field =>
                new ExpectedEffect(snapshot.CanonicalId, field.Key, field.Value, field.Value))).ToArray();

        void Include(Guid guid)
        {
            if (minted.Contains(guid)) { IncludeStaged(guid); return; }
            if (snapshots.ContainsKey(guid)) return;
            if (!repository.IsValidObjectId(guid))
            {
                var missingId = CanonicalId.FromGuid(guid);
                snapshots[guid] = new(missingId, new Dictionary<string, IReadOnlyDictionary<string, string>>
                    { ["class"] = new Dictionary<string, string> { ["value"] = "missing" } });
                return;
            }
            var item = repository.GetObject(guid);
            var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
                { ["class"] = new Dictionary<string, string> { ["value"] = item.ClassName } };
            if (item.Owner is { } parent)
                fields["owner"] = new Dictionary<string, string> { ["ref"] = CanonicalId.FromGuid(parent.Guid).Value };
            snapshots[guid] = new(CanonicalId.FromGuid(guid), fields);
            switch (item)
            {
                case IMoInflClass cls:
                    Add(MoInflClassSnapshotter.Snapshot(cache, cls));
                    Add(MoInflClassAuthoringSnapshotter.Snapshot(cache, cls));
                    if (cls.Owner is { } clsOwner) Include(clsOwner.Guid);
                    break;
                case IPartOfSpeech category:
                    Add(CmPossibilitySnapshotter.Snapshot(cache, category));
                    Add(CmPossibilityAuthoringSnapshotter.Snapshot(cache, category));
                    if (category.Owner is IPartOfSpeech ancestor) Include(ancestor.Guid);
                    break;
                case IMoInflAffixSlot slot:
                    Add(MoInflAffixSlotSnapshotter.Snapshot(cache, slot));
                    Add(MoInflAffixSlotAuthoringSnapshotter.Snapshot(cache, slot));
                    if (slot.Owner is { } slotOwner) Include(slotOwner.Guid);
                    break;
                case IFsFeatDefn feature:
                    Add(FsFeatDefnSnapshotter.Snapshot(cache, feature));
                    Add(FsFeatDefnAuthoringSnapshotter.Snapshot(cache, feature));
                    break;
                case IFsSymFeatVal value:
                    Add(FsSymFeatValSnapshotter.Snapshot(cache, value));
                    Add(FsSymFeatValAuthoringSnapshotter.Snapshot(cache, value));
                    if (value.Owner is { } featureOwner) Include(featureOwner.Guid);
                    break;
                case IPhEnvironment environment:
                    sound ??= SoundSystemSnapshots.Read(cache);
                    Add(sound[CanonicalId.FromGuid(guid)]);
                    ReadSoundScope();
                    var pattern = environment.StringRepresentation?.Text ?? string.Empty;
                    // Indexed environment references must bind the same class definition as an unindexed reference.
                    var abbreviations = Regex.Matches(pattern, @"\[\s*([^\[\]]+?)\s*\]")
                        .Select(match => Regex.Replace(match.Groups[1].Value.Trim(), @"\^[0-9]$", string.Empty))
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var naturalClass in cache.LangProject.PhonologicalDataOA.NaturalClassesOS)
                        if (abbreviations.Contains(naturalClass.Abbreviation.BestAnalysisAlternative.Text))
                            IncludeSound(naturalClass.Guid);
                    break;
            }

            void Add(ObjectSnapshot snapshot)
            {
                foreach (var field in snapshot.AlternativesFields) fields[field.Key] = field.Value;
            }
        }

        void ReadSoundScope()
        {
            if (soundScopeRead) return;
            soundScopeRead = true;
            var firstSet = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
            var projectId = CanonicalId.FromGuid(cache.LangProject.Guid);
            snapshots[projectId.ToGuid()] = new(projectId, new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["environmentSoundScope"] = new Dictionary<string, string>
                {
                    ["firstPhonemeSet"] = firstSet is null ? string.Empty : CanonicalId.FromGuid(firstSet.Guid).Value,
                    ["defaultVernacular"] = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
                },
            });
            // The first alphabet defines segmentation, so a new overlapping code can change an environment.
            if (firstSet is not null) IncludeSound(firstSet.Guid);
        }

        void IncludeStaged(Guid guid)
        {
            if (!stagedReads.Add(guid)) return;
            var create = proposal.Operations.FirstOrDefault(operation => operation.EntityId?.ToGuid() == guid);
            if (create is not null && create.Target is { } target &&
                create.Kind is PartOfSpeechInflectionClassesOperationKinds.Create or MoInflClassSubclassesOperationKinds.Create)
                Include(target.ToGuid());
            foreach (var operation in proposal.Operations.Where(operation => operation.Target?.ToGuid() == guid))
            {
                if (operation.Kind is PhNCFeaturesFeaturesOperationKinds.Create or
                    PhPhonemeFeaturesOperationKinds.Create or FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs &&
                    operation.EntityId is { } child)
                    IncludeSound(child.ToGuid());
                else if (operation.Kind is FsFeatureSpecificationFeatureOperationKinds.SetFeature or
                    FsClosedValueValueOperationKinds.SetValue && operation.After is { } featureReference)
                    IncludeSound(ClosedPayloadParsing.GetRequiredCanonicalId(featureReference, "ref", operation.Kind).ToGuid());
                else if (operation.Kind == PhEnvironmentStringRepresentationOperationKinds.Set && operation.After is { } after)
                {
                    ReadSoundScope();
                    foreach (var context in AuthorEnvironmentIntentParser.Contexts(after, "left")
                        .Concat(AuthorEnvironmentIntentParser.Contexts(after, "right")))
                    {
                        if (context.Phoneme is { } phone) IncludeSound(phone.ToGuid());
                        if (context.NaturalClass is { } cls) IncludeSound(cls.ToGuid());
                        if (context.BoundaryMarker is { } marker) IncludeSound(marker.ToGuid());
                    }
                }
                else if (operation.Kind == PhNCSegmentsSegmentsOperationKinds.AddRefSegments && operation.After is { } memberPayload)
                    IncludeSound(ClosedPayloadParsing.GetRequiredCanonicalId(memberPayload, "member", operation.Kind).ToGuid());
                else if (ReferenceKinds.Contains(operation.Kind) && operation.After is { } reference)
                    foreach (var key in new[] { "ref", "member" })
                        if (reference.TryGetProperty(key, out var value)) Include(CanonicalId.Parse(value.GetString()!).ToGuid());
            }
        }

        void IncludeSound(Guid guid)
        {
            if (minted.Contains(guid)) { IncludeStaged(guid); return; }
            if (snapshots.ContainsKey(guid)) return;
            sound ??= SoundSystemSnapshots.Read(cache);
            if (!sound.TryGetValue(CanonicalId.FromGuid(guid), out var snapshot)) { Include(guid); return; }
            snapshots[guid] = snapshot;
            var item = repository.GetObject(guid);
            switch (item)
            {
                case IPhPhonemeSet set:
                    foreach (var terminal in set.PhonemesOC.Cast<IPhTerminalUnit>().Concat(set.BoundaryMarkersOC)) IncludeSound(terminal.Guid);
                    break;
                case IPhTerminalUnit terminal:
                    foreach (var code in terminal.CodesOS) IncludeSound(code.Guid);
                    if (terminal is IPhPhoneme phoneme && phoneme.FeaturesOA is { } phoneFeatures)
                        IncludeSound(phoneFeatures.Guid);
                    break;
                case IPhNCSegments segments:
                    foreach (var member in segments.SegmentsRC) IncludeSound(member.Guid);
                    break;
                case IPhNCFeatures features when features.FeaturesOA is { } classFeatures:
                    IncludeSound(classFeatures.Guid);
                    break;
                case IFsFeatStruc structure:
                    foreach (var spec in structure.FeatureSpecsOC) IncludeSound(spec.Guid);
                    break;
                case IFsFeatureSpecification specification:
                    if (specification.FeatureRA is { } feature) Include(feature.Guid);
                    if (specification is IFsClosedValue closed && closed.ValueRA is { } value) Include(value.Guid);
                    break;
            }
        }

        void IncludeForm(IMoForm? form)
        {
            if (form is null || minted.Contains(form.Guid)) return;
            var parts = new List<ObjectSnapshot> { MoFormSnapshotter.Snapshot(cache, form), MoFormRelationsSnapshotter.Snapshot(cache, form) };
            if (form is IMoAffixForm affix) parts.Add(MoAffixFormRelationsSnapshotter.Snapshot(cache, affix));
            if (form is IMoAffixAllomorph ordinary) parts.Add(MoAffixAllomorphRelationsSnapshotter.Snapshot(cache, ordinary));
            if (form is IMoStemAllomorph stem) parts.Add(MoStemAllomorphRelationsSnapshotter.Snapshot(cache, stem));
            Include(form.Guid);
            var fields = snapshots[form.Guid].AlternativesFields.ToDictionary(field => field.Key, field => field.Value);
            foreach (var part in parts) foreach (var field in part.AlternativesFields) fields[field.Key] = field.Value;
            snapshots[form.Guid] = new(CanonicalId.FromGuid(form.Guid), fields);
            if (form is IMoAffixForm restricted)
                foreach (var cls in restricted.InflectionClassesRC) Include(cls.Guid);
            if (form is IMoAffixAllomorph affixForm)
                foreach (var environment in affixForm.PhoneEnvRC.Concat(affixForm.PositionRS)) Include(environment.Guid);
            if (form is IMoStemAllomorph stemForm)
                foreach (var environment in stemForm.PhoneEnvRC) Include(environment.Guid);
        }

        void IncludeMsa(IMoMorphSynAnalysis msa)
        {
            if (minted.Contains(msa.Guid)) return;
            var category = msa switch
            {
                IMoInflAffMsa inflectional => inflectional.PartOfSpeechRA,
                IMoStemMsa stem => stem.PartOfSpeechRA,
                IMoDerivAffMsa derivational => derivational.FromPartOfSpeechRA,
                IMoUnclassifiedAffixMsa unclassified => unclassified.PartOfSpeechRA,
                _ => null,
            };
            Include(msa.Guid);
            var fields = snapshots[msa.Guid].AlternativesFields.ToDictionary(field => field.Key, field => field.Value);
            fields["category"] = ReferenceFieldSnapshotting.ReadAlternatives(category);
            if (msa is IMoInflAffMsa infl)
            {
                foreach (var field in MoInflAffMsaRelationsSnapshotter.Snapshot(cache, infl).AlternativesFields) fields[field.Key] = field.Value;
            }
            snapshots[msa.Guid] = new(CanonicalId.FromGuid(msa.Guid), fields);
            if (category is not null) Include(category.Guid);
        }
    }
}
