using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Apply;

internal static class AffixProcessPrerequisiteFootprint
{
    internal static IReadOnlyList<ExpectedEffect> Read(LcmCache cache, Proposal proposal)
    {
        var processes = proposal.Operations.Where(operation =>
                operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm && operation.After is { } after &&
                after.ValueKind == System.Text.Json.JsonValueKind.Object && after.TryGetProperty("class", out var concreteClass) &&
                concreteClass.ValueKind == System.Text.Json.JsonValueKind.String && concreteClass.GetString() == "MoAffixProcess")
            .Select(operation => operation.EntityId).Where(id => id is not null).Select(id => id!.Value).ToHashSet(CanonicalId.ByGuid);
        var existing = AffixProcessSemanticValidator.FindAffectedProcesses(cache, proposal.Operations);
        processes.UnionWith(existing.Select(process => CanonicalId.FromGuid(process.Guid)));
        if (processes.Count == 0) return Array.Empty<ExpectedEffect>();
        var inputs = Created(MoAffixProcessInputOperationKinds.Create, processes);
        var outputs = Created(MoAffixProcessOutputOperationKinds.Create, processes);
        inputs.UnionWith(existing.SelectMany(process => process.InputOS).Select(input => CanonicalId.FromGuid(input.Guid)));
        outputs.UnionWith(existing.SelectMany(process => process.OutputOS).Select(output => CanonicalId.FromGuid(output.Guid)));
        var entries = proposal.Operations.Where(operation => operation.EntityId is { } entity && processes.Contains(entity))
            .Select(operation => operation.Target).Where(id => id is not null).Select(id => id!.Value).ToHashSet(CanonicalId.ByGuid);
        entries.UnionWith(existing.Select(process => process.Owner).OfType<ILexEntry>()
            .Select(entry => CanonicalId.FromGuid(entry.Guid)));
        var msas = Created(LexEntryMorphoSyntaxAnalysesOperationKinds.Create, entries);
        msas.UnionWith(existing.Select(process => process.Owner).OfType<ILexEntry>()
            .SelectMany(entry => entry.MorphoSyntaxAnalysesOC).Select(msa => CanonicalId.FromGuid(msa.Guid)));
        var minted = FootprintPlan.TargetsMintedWithinProposal(proposal);
        var objects = new Dictionary<CanonicalId, ObjectSnapshot>(CanonicalId.ByGuid);
        var usesPhonemes = false;
        var sound = SoundSystemSnapshots.Read(cache);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        foreach (var process in existing)
        {
            Add(process);
            if (process.MorphTypeRA is { } morphType) Ownership(CanonicalId.FromGuid(morphType.Guid));
            foreach (var input in process.InputOS)
            {
                Add(input);
                if (input is IPhSimpleContextSeg { FeatureStructureRA: { } phone }) Phone(CanonicalId.FromGuid(phone.Guid));
                if (input is IPhSimpleContextNC { FeatureStructureRA: { } naturalClass }) NaturalClass(CanonicalId.FromGuid(naturalClass.Guid));
            }
            foreach (var output in process.OutputOS)
            {
                Add(output);
                if (output is IMoInsertPhones insert)
                    foreach (var phone in insert.ContentRS.OfType<IPhPhoneme>()) Phone(CanonicalId.FromGuid(phone.Guid));
            }
        }
        foreach (var operation in proposal.Operations)
        {
            if (operation.Target is not { } target || operation.After is not { } after) continue;
            if (inputs.Contains(target) && operation.Kind == PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure)
                Phone(PhSimpleContextSegFeatureStructureSetPayload.Parse(after));
            if (inputs.Contains(target) && operation.Kind == PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure)
                NaturalClass(PhSimpleContextNCFeatureStructureSetPayload.Parse(after));
            if (outputs.Contains(target) && operation.Kind == MoInsertPhonesContentOperationKinds.AddRefContent)
                Phone(MoInsertPhonesContentMemberPayload.Parse(after, operation.Kind));
            if (msas.Contains(target))
            {
                if (operation.Kind == MoInflAffMsaSlotsOperationKinds.AddRefSlots)
                    Ownership(MoInflAffMsaSlotsMemberPayload.Parse(after, operation.Kind));
                else if (operation.Kind is MoInflAffMsaPartOfSpeechOperationKinds.SetPartOfSpeech or
                    MoDerivAffMsaFromPartOfSpeechOperationKinds.SetFromPartOfSpeech or
                    MoDerivAffMsaToPartOfSpeechOperationKinds.SetToPartOfSpeech or
                    MoUnclassifiedAffixMsaPartOfSpeechOperationKinds.SetPartOfSpeech)
                    Ownership(ClosedPayloadParsing.GetRequiredCanonicalId(after, "ref", operation.Kind));
            }
        }
        foreach (var operation in proposal.Operations.Where(operation => operation.EntityId is { } entity && processes.Contains(entity)))
            if (operation.After is { } after)
                Ownership(ClosedPayloadParsing.GetRequiredCanonicalId(after, "morphType", operation.Kind));
        var result = objects.Values.OrderBy(value => value.CanonicalId.Value, StringComparer.Ordinal)
            .SelectMany(value => value.AlternativesFields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ExpectedEffect(value.CanonicalId, "prerequisite/" + pair.Key, pair.Value, pair.Value))).ToList();
        var firstSet = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
        var firstSetValue = firstSet is null ? new Dictionary<string, string>() :
            new Dictionary<string, string> { ["ref"] = CanonicalId.FromGuid(firstSet.Guid).Value };
        result.Add(new ExpectedEffect(CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.Guid),
            "prerequisite/firstPhonemeSet", firstSetValue, firstSetValue));
        if (usesPhonemes)
        {
            var defaultVernacular = new Dictionary<string, string>
            {
                ["tag"] = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            };
            result.Add(new ExpectedEffect(CanonicalId.FromGuid(cache.LangProject.Guid),
                "prerequisite/defaultVernacularWritingSystem", defaultVernacular, defaultVernacular));
        }
        return result;

        HashSet<CanonicalId> Created(string kind, HashSet<CanonicalId> owners) => proposal.Operations
            .Where(operation => operation.Kind == kind && operation.Target is { } owner && owners.Contains(owner) && operation.EntityId is not null)
            .Select(operation => operation.EntityId!.Value).ToHashSet(CanonicalId.ByGuid);

        void RequireExisting(CanonicalId id, string what)
        {
            if (!repository.IsValidObjectId(id.ToGuid()))
                throw new InvalidOperationException($"'{what}': '{id.Value}' does not exist in this project.");
        }

        void Ownership(CanonicalId id)
        {
            if (minted.Contains(id)) return;
            RequireExisting(id, "affix prerequisite");
            var value = repository.GetObject(id.ToGuid());
            for (ICmObject? current = value; current is not null; current = current.Owner)
            {
                var currentId = CanonicalId.FromGuid(current.Guid);
                if (objects.ContainsKey(currentId)) continue;
                var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
                {
                    ["class"] = new Dictionary<string, string> { ["value"] = current.ClassName },
                };
                if (current.Owner is { } owner)
                    fields["owner"] = new Dictionary<string, string> { ["ref"] = CanonicalId.FromGuid(owner.Guid).Value };
                objects[currentId] = new ObjectSnapshot(currentId, fields);
            }
        }
        void Add(ICmObject value)
        {
            var id = CanonicalId.FromGuid(value.Guid);
            if (minted.Contains(id)) return;
            Ownership(id);
            if (sound.TryGetValue(id, out var snapshot)) objects[id] = snapshot;
        }
        void Structure(IFsFeatStruc? structure)
        {
            if (structure is null) return;
            Add(structure);
            foreach (var spec in structure.FeatureSpecsOC)
            {
                Add(spec);
                if (spec.FeatureRA is { } feature) Ownership(CanonicalId.FromGuid(feature.Guid));
                if (spec is IFsClosedValue closed && closed.ValueRA is { } value) Ownership(CanonicalId.FromGuid(value.Guid));
            }
        }
        void Phone(CanonicalId id)
        {
            usesPhonemes = true;
            if (minted.Contains(id)) return;
            RequireExisting(id, "affix prerequisite phoneme");
            var phone = ReferenceFieldLowering.Resolve<IPhPhoneme>(cache, id, "affix prerequisite");
            Add(phone);
            foreach (var code in phone.CodesOS) Add(code);
            Structure(phone.FeaturesOA);
        }
        void NaturalClass(CanonicalId id)
        {
            usesPhonemes = true;
            if (minted.Contains(id))
            {
                foreach (var member in proposal.Operations.Where(operation => operation.Target == id &&
                    operation.Kind == PhNCSegmentsSegmentsOperationKinds.AddRefSegments && operation.After is not null))
                    Phone(PhNCSegmentsSegmentsMemberPayload.Parse(member.After!.Value, member.Kind));
                return;
            }
            RequireExisting(id, "affix prerequisite natural class");
            var naturalClass = ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, id, "affix prerequisite");
            Add(naturalClass);
            if (naturalClass is IPhNCSegments segments)
                foreach (var phone in segments.SegmentsRC) Phone(CanonicalId.FromGuid(phone.Guid));
            if (naturalClass is IPhNCFeatures features)
            {
                Structure(features.FeaturesOA);
                var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
                if (set is not null)
                {
                    Add(set);
                    var setId = CanonicalId.FromGuid(set.Guid);
                    objects[setId] = new ObjectSnapshot(setId, objects[setId].AlternativesFields
                        .Where(pair => pair.Key is "class" or "owner" || pair.Key == SnapshotFields.PhPhonemeSetPhonemes)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
                    foreach (var phone in set.PhonemesOC) Phone(CanonicalId.FromGuid(phone.Guid));
                }
            }
        }
    }
}
