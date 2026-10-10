using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

public static class AlternateFormRetirementOperationKinds
{
    public const string DeleteAlternateForm = "lexical/lexEntry/deleteAlternateForm";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(DeleteAlternateForm);
        OperationHandlerRegistry.Register(DeleteAlternateForm, AlternateFormDeleteHandler.Instance);
    }
#pragma warning restore CA2255
}

public sealed record AlternateFormDeletePayload(
    IReadOnlyList<CanonicalId> Forms,
    IReadOnlyList<CanonicalId> DuplicateSurvivors,
    IReadOnlyList<AlternateFormSemanticWitness> FormWitnesses,
    IReadOnlyList<CanonicalId> Before,
    IReadOnlyList<CanonicalId> After,
    RuleReplacementWitness? RuleReplacement)
{
    public static AlternateFormDeletePayload Parse(JsonElement after)
    {
        const string kind = AlternateFormRetirementOperationKinds.DeleteAlternateForm;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after,
            ["forms", "duplicateSurvivors", "formWitnesses", "before", "after", "ruleReplacement"], kind);
        var forms = RetargetPayloadParsing.Ids(after, "forms", kind);
        var survivors = RetargetPayloadParsing.Ids(after, "duplicateSurvivors", kind);
        var witnesses = ParseWitnesses(after, kind);
        var before = RetargetPayloadParsing.Ids(after, "before", kind);
        var desired = RetargetPayloadParsing.Ids(after, "after", kind, allowEmpty: true);
        if (forms.Count == 0 || survivors.Count == 0 || forms.Distinct().Count() != forms.Count ||
            survivors.Distinct().Count() != survivors.Count || forms.Intersect(survivors).Any())
            throw new ContractParseException($"'{kind}' requires distinct retired forms and surviving duplicate forms.");
        if (before.Distinct().Count() != before.Count || desired.Distinct().Count() != desired.Count ||
            !forms.All(before.Contains) || forms.Any(desired.Contains) || !desired.SequenceEqual(before.Except(forms)))
            throw new ContractParseException($"'{kind}' must preserve the ordered alternate-form sequence minus the retired forms.");
        if (witnesses.Select(item => item.Form).Distinct().Count() != witnesses.Count ||
            !witnesses.Select(item => item.Form).ToHashSet().SetEquals(forms.Concat(survivors)))
            throw new ContractParseException($"'{kind}' requires one semantic witness for each retired and surviving form.");
        var ruleReplacement = ParseRuleReplacement(after, kind);
        return new AlternateFormDeletePayload(forms, survivors, witnesses, before, desired, ruleReplacement);
    }

    private static RuleReplacementWitness? ParseRuleReplacement(JsonElement after, string kind)
    {
        if (!after.TryGetProperty("ruleReplacement", out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        ClosedPayloadParsing.RequireObject(value, kind + ".ruleReplacement");
        ClosedPayloadParsing.RejectUnknownProperties(value, ["rule", "inputPhoneme", "outputPhoneme"],
            kind + ".ruleReplacement");
        return new RuleReplacementWitness(
            RetargetPayloadParsing.Id(value, "rule", kind + ".ruleReplacement"),
            RetargetPayloadParsing.Id(value, "inputPhoneme", kind + ".ruleReplacement"),
            RetargetPayloadParsing.Id(value, "outputPhoneme", kind + ".ruleReplacement"));
    }

    private static IReadOnlyList<AlternateFormSemanticWitness> ParseWitnesses(JsonElement after, string kind)
    {
        if (!after.TryGetProperty("formWitnesses", out var array) || array.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{kind}' operation 'after.formWitnesses' must be an array.");
        var result = new List<AlternateFormSemanticWitness>();
        foreach (var item in array.EnumerateArray())
        {
            ClosedPayloadParsing.RequireObject(item, kind + ".formWitnesses");
            ClosedPayloadParsing.RejectUnknownProperties(item, ["form", "semanticDigest"], kind + ".formWitnesses");
            var form = RetargetPayloadParsing.Id(item, "form", kind);
            var digest = ClosedPayloadParsing.GetRequiredString(item, "semanticDigest", kind);
            if (!Sha256Value.IsCanonical(digest))
                throw new ContractParseException($"'{kind}' operation 'after.formWitnesses' requires canonical SHA-256 digests.");
            result.Add(new AlternateFormSemanticWitness(form, digest));
        }
        if (result.Count == 0)
            throw new ContractParseException($"'{kind}' operation 'after.formWitnesses' cannot be empty.");
        return result;
    }
}

public sealed record AlternateFormSemanticWitness(CanonicalId Form, string SemanticDigest);

public sealed record RuleReplacementWitness(CanonicalId Rule, CanonicalId InputPhoneme, CanonicalId OutputPhoneme);

internal sealed class AlternateFormDeleteHandler : IOperationHandler
{
    internal static readonly AlternateFormDeleteHandler Instance = new();
    private AlternateFormDeleteHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.After is not { } after)
            throw new InvalidOperationException($"'{AlternateFormRetirementOperationKinds.DeleteAlternateForm}' requires 'after'.");
        var payload = AlternateFormDeletePayload.Parse(after);
        var (id, entry) = TargetResolution.Resolve<ILexEntry>(cache, operation,
            AlternateFormRetirementOperationKinds.DeleteAlternateForm);
        touchedTargets.Add(id);
        var before = ReadAlternates(entry);
        if (!before.SequenceEqual(payload.Before))
            throw new InvalidOperationException("The entry's alternate-form order changed after retirement composition.");
        var beforeValue = ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS);
        ValidateDelete(cache, entry, payload);
        foreach (var formId in payload.Forms)
            cache.ServiceLocator.GetInstance<IMoAffixAllomorphRepository>().GetObject(formId.ToGuid()).Delete();
        var actual = ReadAlternates(entry);
        if (!actual.SequenceEqual(payload.After))
            throw new InvalidOperationException("LibLCM did not preserve the authored alternate-form order during retirement.");
        return new ExpectedEffect(id, SnapshotFields.LexEntryAlternateForms, beforeValue,
            ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, entry) = TargetResolution.Resolve<ILexEntry>(cache, operation,
            AlternateFormRetirementOperationKinds.DeleteAlternateForm);
        var current = ReadAlternates(entry);
        var alternatives = ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS);
        return new ExpectedEffect(id, SnapshotFields.LexEntryAlternateForms, alternatives, alternatives);
    }

    private static void ValidateDelete(LcmCache cache, ILexEntry entry, AlternateFormDeletePayload payload)
    {
        if (entry.LexemeFormOA is null)
            throw new InvalidOperationException("An entry without a primary form cannot retire an alternate form.");
        var survivors = payload.DuplicateSurvivors.Select(id =>
        {
            if (!cache.ServiceLocator.ObjectRepository.TryGetObject(id.ToGuid(), out var value) ||
                value is not IMoAffixAllomorph form || form.Owner?.Guid != entry.Guid ||
                (entry.LexemeFormOA.Guid != form.Guid && !entry.AlternateFormsOS.Contains(form)))
                throw new InvalidOperationException("A declared duplicate survivor is absent from the owning entry.");
            return form;
        }).ToArray();

        foreach (var formId in payload.Forms)
        {
            if (!cache.ServiceLocator.ObjectRepository.TryGetObject(formId.ToGuid(), out var value) ||
                value is not IMoAffixAllomorph source || source.Owner?.Guid != entry.Guid ||
                !entry.AlternateFormsOS.Contains(source) || source.ClassName != "MoAffixAllomorph" || source.IsAbstract)
                throw new InvalidOperationException("Only an ordinary alternate prefix or suffix form can be retired.");
            RequireSemanticWitness(cache, source, payload);
            var morphType = source.MorphTypeRA?.Guid;
            if (morphType != MoMorphTypeTags.kguidMorphPrefix && morphType != MoMorphTypeTags.kguidMorphSuffix)
                throw new InvalidOperationException("Only an ordinary alternate prefix or suffix form can be retired.");
            foreach (var destination in survivors)
                RequireSemanticWitness(cache, destination, payload);
            var exactDuplicate = survivors.Any(destination => SameDuplicateSignature(cache, source, destination));
            if (!exactDuplicate && (payload.RuleReplacement is not { } replacement ||
                    !CanBeReplacedByRule(cache, source, survivors, replacement)))
                throw new InvalidOperationException("A retired form must have a surviving exact duplicate or an active exact rule replacement.");
            if (AllomorphRetargetCensusReader.HasIncomingReferences(cache, [source.Guid]))
                throw new InvalidOperationException("A retired form still has an incoming reference after retargeting.");
            if (HasOwnedDescendants(cache, source))
                throw new InvalidOperationException("A retired form has owned dependents that deletion would cascade.");
            if (IsZeroForm(cache, source) && HasMorphologicalFeatures(entry))
                throw new InvalidOperationException("Feature-bearing zero morphology cannot be retired as a duplicate form.");
        }
    }

    private static bool CanBeReplacedByRule(LcmCache cache, IMoAffixAllomorph source,
        IReadOnlyList<IMoAffixAllomorph> survivors, RuleReplacementWitness witness)
    {
        if (!TryReadRule(cache, witness, out var rule)) return false;
        return survivors.Any(survivor => RuleBackedAllomorphFormProof.Matches(cache, source, survivor,
            witness.InputPhoneme, witness.OutputPhoneme));
    }

    private static bool TryReadRule(LcmCache cache, RuleReplacementWitness witness, out IPhRegularRule rule)
    {
        rule = null!;
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(witness.Rule.ToGuid(), out var value) ||
            value is not IPhRegularRule candidate || candidate.Disabled || candidate.RightHandSidesOS.Count != 1)
            return false;
        if (candidate.StrucDescOS.Count != 1 || candidate.RightHandSidesOS[0].StrucChangeOS.Count != 1 ||
            candidate.StrucDescOS[0] is not IPhSimpleContextSeg input ||
            candidate.RightHandSidesOS[0].StrucChangeOS[0] is not IPhSimpleContextSeg output ||
            input.FeatureStructureRA?.Guid != witness.InputPhoneme.ToGuid() ||
            output.FeatureStructureRA?.Guid != witness.OutputPhoneme.ToGuid())
            return false;
        rule = candidate;
        return true;
    }


    private static void RequireSemanticWitness(
        LcmCache cache, IMoAffixAllomorph form, AlternateFormDeletePayload payload)
    {
        var id = CanonicalId.FromGuid(form.Guid);
        var witness = payload.FormWitnesses.Single(item => item.Form == id);
        var actual = AllomorphRetirementSemanticDigest.Compute(cache, form);
        if (!StringComparer.Ordinal.Equals(witness.SemanticDigest, actual))
            throw new InvalidOperationException("An allomorph's measured semantic meaning changed after retirement was composed.");
    }

    private static bool SameDuplicateSignature(
        LcmCache cache, IMoAffixAllomorph left, IMoAffixAllomorph right)
    {
        if (left.Guid == right.Guid || left.Owner?.Guid != right.Owner?.Guid || left.IsAbstract || right.IsAbstract ||
            left.MorphTypeRA?.Guid != right.MorphTypeRA?.Guid ||
            !BundleMorphHandler.SameAlternatives(BundleMorphHandler.ReadFormAlternatives(cache, left.Form),
                BundleMorphHandler.ReadFormAlternatives(cache, right.Form)) ||
            left.MsEnvFeaturesOA?.Guid != right.MsEnvFeaturesOA?.Guid)
            return false;
        return left.PhoneEnvRC.Select(item => item.Guid).Order().SequenceEqual(right.PhoneEnvRC.Select(item => item.Guid).Order()) &&
               left.PositionRS.Select(item => item.Guid).SequenceEqual(right.PositionRS.Select(item => item.Guid)) &&
               left.InflectionClassesRC.Select(item => item.Guid).Order()
                   .SequenceEqual(right.InflectionClassesRC.Select(item => item.Guid).Order());
    }

    private static bool IsZeroForm(LcmCache cache, IMoAffixAllomorph form)
    {
        var alternatives = BundleMorphHandler.ReadFormAlternatives(cache, form.Form).Values.ToArray();
        return alternatives.Length == 0 || alternatives.All(value => value.Trim() is "0" or "∅" or "Ø");
    }

    private static bool HasMorphologicalFeatures(ILexEntry entry) =>
        entry.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>().Any(item => item.InflFeatsOA is not null) ||
        entry.MorphoSyntaxAnalysesOC.OfType<IMoDerivAffMsa>()
            .Any(item => item.FromMsFeaturesOA is not null || item.ToMsFeaturesOA is not null);

    private static bool HasOwnedDescendants(LcmCache cache, ICmObject parent)
    {
        foreach (var item in cache.ServiceLocator.ObjectRepository.AllInstances())
            for (var owner = item.Owner; owner is not null; owner = owner.Owner)
                if (owner.Guid == parent.Guid) return true;
        return false;
    }

    private static CanonicalId[] ReadAlternates(ILexEntry entry) => entry.AlternateFormsOS
        .Select(item => CanonicalId.FromGuid(item.Guid)).ToArray();
}

public static class RuleBackedAllomorphFormProof
{
    public static bool Matches(LcmCache cache, IMoAffixAllomorph source, IMoAffixAllomorph survivor,
        CanonicalId input, CanonicalId output)
    {
        if (!SameNonFormSignature(source, survivor)) return false;
        var sourceAlternatives = BundleMorphHandler.ReadFormAlternatives(cache, source.Form);
        var survivorAlternatives = BundleMorphHandler.ReadFormAlternatives(cache, survivor.Form);
        return sourceAlternatives.Count > 0 &&
            sourceAlternatives.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(survivorAlternatives.Keys) &&
            !BundleMorphHandler.SameAlternatives(sourceAlternatives, survivorAlternatives) &&
            sourceAlternatives.All(pair => HasSingleRuleCorrespondence(cache, pair.Key, pair.Value,
                survivorAlternatives[pair.Key], input, output));
    }

    private static bool SameNonFormSignature(IMoAffixAllomorph left, IMoAffixAllomorph right) =>
        left.Guid != right.Guid && left.Owner?.Guid == right.Owner?.Guid && !left.IsAbstract && !right.IsAbstract &&
        left.MorphTypeRA?.Guid == right.MorphTypeRA?.Guid && left.MsEnvFeaturesOA is null &&
        right.MsEnvFeaturesOA is null && left.PhoneEnvRC.Count == 0 && right.PhoneEnvRC.Count == 0 &&
        left.PositionRS.Count == 0 && right.PositionRS.Count == 0 && left.InflectionClassesRC.Count == 0 &&
        right.InflectionClassesRC.Count == 0;

    private static bool HasSingleRuleCorrespondence(LcmCache cache, string writingSystem, string source,
        string survivor, CanonicalId input, CanonicalId output)
    {
        var phones = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS
            .SelectMany(set => set.PhonemesOC).ToArray();
        var sourceTokens = Tokenize(cache, phones, writingSystem, source);
        var survivorTokens = Tokenize(cache, phones, writingSystem, survivor);
        if (sourceTokens is null || survivorTokens is null || sourceTokens.Count != survivorTokens.Count) return false;
        var changed = -1;
        for (var index = 0; index < sourceTokens.Count; index++)
        {
            if (sourceTokens[index] == survivorTokens[index]) continue;
            if (changed >= 0 || sourceTokens[index] != output || survivorTokens[index] != input) return false;
            changed = index;
        }
        return changed >= 0;
    }

    private static IReadOnlyList<CanonicalId>? Tokenize(LcmCache cache, IReadOnlyList<IPhPhoneme> phones,
        string writingSystem, string form)
    {
        var ws = cache.WritingSystemFactory.GetWsFromStr(writingSystem);
        var mappings = phones.SelectMany(phone => phone.CodesOS.Select(code =>
                (Grapheme: code.Representation.get_String(ws)?.Text, Phoneme: CanonicalId.FromGuid(phone.Guid))))
            .Where(item => !string.IsNullOrEmpty(item.Grapheme))
            .Select(item => (Grapheme: item.Grapheme!.Normalize(System.Text.NormalizationForm.FormD), item.Phoneme))
            .Distinct().OrderByDescending(item => item.Grapheme.Length)
            .ThenBy(item => item.Grapheme, StringComparer.Ordinal).ToArray();
        var normalized = form.Normalize(System.Text.NormalizationForm.FormD);
        var paths = new List<IReadOnlyList<CanonicalId>>();
        void Visit(int offset, List<CanonicalId> path)
        {
            if (paths.Count > 1 || path.Count > 256) return;
            if (offset == normalized.Length)
            {
                paths.Add(path.ToArray());
                return;
            }
            foreach (var mapping in mappings)
            {
                if (offset + mapping.Grapheme.Length > normalized.Length ||
                    !normalized.AsSpan(offset, mapping.Grapheme.Length).SequenceEqual(mapping.Grapheme.AsSpan())) continue;
                path.Add(mapping.Phoneme);
                Visit(offset + mapping.Grapheme.Length, path);
                path.RemoveAt(path.Count - 1);
                if (paths.Count > 1) return;
            }
        }
        Visit(0, []);
        return paths.Count == 1 ? paths[0] : null;
    }
}
