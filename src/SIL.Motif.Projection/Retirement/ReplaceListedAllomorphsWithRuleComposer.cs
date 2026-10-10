using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Projection.Retirement;

public static class ReplaceListedAllomorphsWithRuleComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache,
        ReplaceListedAllomorphsWithRuleIntent intent, Func<CanonicalId> operationIdFactory)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(operationIdFactory);
        _ = AllomorphRetirementCodec.ToJson(intent);
        if (intent.Retirements.Any(retirement => retirement.Scope != AllomorphRetirementScope.Affix))
            throw new InvalidOperationException(
                "The rule replacement recipe supports ordinary affix alternates only; stem retirement evidence is incomplete.");

        var naturalClassId = CanonicalId.Parse(intent.NaturalClass.Id);
        var ruleId = CanonicalId.Parse(intent.Rule.Id);
        var input = CanonicalId.Parse(intent.Rule.Input.Single());
        var output = CanonicalId.Parse(intent.Rule.Output.Single());
        var usedIds = new HashSet<CanonicalId> { naturalClassId, ruleId };
        CanonicalId Mint()
        {
            var id = operationIdFactory();
            if (!usedIds.Add(id)) throw new InvalidOperationException("The operation id factory returned a duplicate identity.");
            return id;
        }

        IReadOnlyList<OperationEnvelope> classOperations;
        if (intent.NaturalClass.Mode == "create")
        {
            if (cache.ServiceLocator.ObjectRepository.TryGetObject(naturalClassId.ToGuid(), out _))
                throw new InvalidOperationException("The proposed natural-class identity already exists in the Baseline.");
            classOperations = AuthorNaturalClassComposer.Build(cache,
                new AuthorNaturalClassIntent(intent.NaturalClass.Name, intent.NaturalClass.Abbreviation,
                    intent.NaturalClass.Members.Select(CanonicalId.Parse).ToArray()), Mint, naturalClassId);
        }
        else
        {
            ValidateReusedClass(cache, intent.NaturalClass);
            classOperations = [];
        }

        var placement = ResolvePlacement(cache, intent.Rule.Placement);
        var ruleIntent = new AuthorPhonologicalRuleIntent(intent.Rule.Name, PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: input)], [new(Phoneme: output)],
            Contexts(intent.Rule.Left), Contexts(intent.Rule.Right), placement);
        var ruleOperations = AuthorPhonologicalRuleComposer.Build(cache, ruleIntent, Mint,
            intent.NaturalClass.Mode == "create" ? naturalClassId : null, ruleId, classOperations);
        var enabled = ruleOperations.Single(operation => operation.Kind == PhSegmentRuleDisabledOperationKinds.SetDisabled &&
            operation.After is { } after && after.GetProperty("value").GetBoolean() == false);
        var witness = new RuleReplacementWitness(ruleId, input, output);
        var operations = new List<OperationEnvelope>(classOperations.Count + ruleOperations.Count + 8);
        operations.AddRange(classOperations);
        operations.AddRange(ruleOperations);

        foreach (var retirement in intent.Retirements)
        {
            var survivors = retirement.RoleReplacements.Select(item => item.Replacement)
                .Concat(retirement.AdhocReplacements.Select(item => item.Replacement))
                .DistinctBy(item => CanonicalId.Parse(item.Id)).ToArray();
            var composed = RetireAllomorphComposer.Build(cache,
                new RetireAllomorphIntentDocument(retirement, survivors), Mint, witness);
            var deletion = composed[composed.Count - 1];
            var retargets = composed.Take(composed.Count - 1)
                .Select(operation => WithDependency(operation, enabled.OperationId)).ToArray();
            operations.AddRange(retargets);
            operations.Add(deletion);
        }

        return operations;
    }

    private static void ValidateReusedClass(LcmCache cache, RetirementNaturalClass assertion)
    {
        var id = CanonicalId.Parse(assertion.Id);
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(id.ToGuid(), out var value) ||
            value is not IPhNCSegments naturalClass ||
            !cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Contains(naturalClass))
            throw new InvalidOperationException("The reused natural class is absent or is not segment-defined.");
        var members = naturalClass.SegmentsRC.Select(member => CanonicalId.FromGuid(member.Guid).Value)
            .Order(StringComparer.Ordinal).ToArray();
        if (!members.SequenceEqual(assertion.Members.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            naturalClass.Name.get_String(cache.DefaultAnalWs)?.Text?.Normalize(NormalizationForm.FormD) !=
                assertion.Name.Normalize(NormalizationForm.FormD) ||
            naturalClass.Abbreviation.get_String(cache.DefaultAnalWs)?.Text?.Normalize(NormalizationForm.FormD) !=
                assertion.Abbreviation.Normalize(NormalizationForm.FormD) ||
            !StringComparer.Ordinal.Equals(assertion.SemanticDigest, NaturalClassDigest(cache, naturalClass)))
            throw new InvalidOperationException("The reused natural class no longer matches its exact authored identity and membership.");
    }

    private static string NaturalClassDigest(LcmCache cache, IPhNCSegments naturalClass)
    {
        var state = new
        {
            className = naturalClass.ClassName,
            name = naturalClass.Name.get_String(cache.DefaultAnalWs)?.Text?.Normalize(NormalizationForm.FormD),
            abbreviation = naturalClass.Abbreviation.get_String(cache.DefaultAnalWs)?.Text?.Normalize(NormalizationForm.FormD),
            members = naturalClass.SegmentsRC.Select(member => CanonicalId.FromGuid(member.Guid).Value)
                .Order(StringComparer.Ordinal).ToArray()
        };
        var canonical = CanonicalJson.Canonicalize(JsonSerializer.Serialize(state));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static Placement? ResolvePlacement(LcmCache cache, RetirementRulePlacement placement)
    {
        var rules = cache.LangProject.PhonologicalDataOA.PhonRulesOS
            .Select(item => CanonicalId.FromGuid(item.Guid)).ToArray();
        return placement.Kind switch
        {
            "first" => rules.Length == 0 ? null : new Placement(null, rules[0]),
            "last" => rules.Length == 0 ? null : new Placement(rules[^1], null),
            "before" => new Placement(null, CanonicalId.Parse(placement.Anchor!)),
            "after" => new Placement(CanonicalId.Parse(placement.Anchor!), null),
            _ => throw new InvalidOperationException("The rule placement is unsupported.")
        };
    }

    private static EnvironmentContext[] Contexts(IReadOnlyList<RetirementContextAtom> atoms) => atoms.Select(atom =>
        atom.Kind switch
        {
            "segment" => new EnvironmentContext(Phoneme: CanonicalId.Parse(atom.Id)),
            "natural-class" => new EnvironmentContext(NaturalClass: CanonicalId.Parse(atom.Id)),
            "boundary" => BoundaryContext(CanonicalId.Parse(atom.Id)),
            _ => throw new InvalidOperationException("The rule context is unsupported.")
        }).ToArray();

    private static EnvironmentContext BoundaryContext(CanonicalId id) => id.ToGuid() switch
    {
        var guid when guid == LangProjectTags.kguidPhRuleWordBdry => new EnvironmentContext(Boundary: "word"),
        var guid when guid == LangProjectTags.kguidPhRuleMorphBdry => new EnvironmentContext(Boundary: "morpheme"),
        _ => new EnvironmentContext(BoundaryMarker: id),
    };

    private static OperationEnvelope WithDependency(OperationEnvelope operation, CanonicalId prerequisite) =>
        new(operation.OperationId, operation.Kind, operation.EntityId, operation.Target, operation.After,
            operation.Placement, operation.DependsOn.Append(new OperationDependency(prerequisite))
                .DistinctBy(item => item.OperationId).ToArray(), operation.StorageIdOverride, operation.Rationale,
            operation.Confidence, operation.Provenance, operation.Extensions);
}
