using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

public static class AllomorphRetargetOperationKinds
{
    public const string SetBundleMorph = "analysis/wfiMorphBundle/setMorph";
    public const string RetargetAdhocReferences = "grammar/moAlloAdhocProhib/retargetReferences";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(SetBundleMorph);
        OperationHandlerRegistry.Register(SetBundleMorph, BundleMorphHandler.Instance);
        OperationKindRegistry.Register(RetargetAdhocReferences);
        OperationHandlerRegistry.Register(RetargetAdhocReferences, AdhocReferencesHandler.Instance);
    }
#pragma warning restore CA2255
}

public sealed record BundleMorphRetargetPayload(
    IReadOnlyList<CanonicalId> RetiredForms, string CensusDigest, CanonicalId RetiredForm, CanonicalId Replacement)
{
    public static BundleMorphRetargetPayload Parse(JsonElement after)
    {
        ClosedPayloadParsing.RequireObject(after, AllomorphRetargetOperationKinds.SetBundleMorph);
        ClosedPayloadParsing.RejectUnknownProperties(after,
            ["retiredForms", "censusDigest", "retiredForm", "replacement"],
            AllomorphRetargetOperationKinds.SetBundleMorph);
        return new BundleMorphRetargetPayload(
            RetargetPayloadParsing.Ids(after, "retiredForms", AllomorphRetargetOperationKinds.SetBundleMorph),
            ClosedPayloadParsing.GetRequiredString(after, "censusDigest", AllomorphRetargetOperationKinds.SetBundleMorph),
            RetargetPayloadParsing.Id(after, "retiredForm", AllomorphRetargetOperationKinds.SetBundleMorph),
            RetargetPayloadParsing.Id(after, "replacement", AllomorphRetargetOperationKinds.SetBundleMorph));
    }
}

public sealed record AdhocRetargetField(string Field, IReadOnlyList<CanonicalId> Before, IReadOnlyList<CanonicalId> After);

public sealed record AdhocRetargetPayload(
    IReadOnlyList<CanonicalId> RetiredForms, string CensusDigest, IReadOnlyList<AdhocRetargetField> Fields)
{
    public static AdhocRetargetPayload Parse(JsonElement after)
    {
        const string kind = AllomorphRetargetOperationKinds.RetargetAdhocReferences;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["retiredForms", "censusDigest", "fields"], kind);
        if (!after.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{kind}' operation 'after.fields' must be an array.");

        var parsed = new List<AdhocRetargetField>();
        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object)
                throw new ContractParseException($"'{kind}' operation field entries must be objects.");
            ClosedPayloadParsing.RejectUnknownProperties(field, ["field", "before", "after"], kind);
            var name = ClosedPayloadParsing.GetRequiredString(field, "field", kind);
            if (name is not ("FirstAllomorph" or "RestOfAllos" or "Allomorphs"))
                throw new ContractParseException($"'{kind}' operation has unsupported field '{name}'.");
            var before = RetargetPayloadParsing.Ids(field, "before", kind);
            var desired = RetargetPayloadParsing.Ids(field, "after", kind);
            if (before.Count != desired.Count)
                throw new ContractParseException($"'{kind}' operation must retain each restriction sequence position.");
            if (name == "FirstAllomorph" && before.Count != 1)
                throw new ContractParseException($"'{kind}' operation FirstAllomorph requires one before and after value.");
            if (!parsed.All(item => item.Field != name))
                throw new ContractParseException($"'{kind}' operation repeats field '{name}'.");
            parsed.Add(new AdhocRetargetField(name, before, desired));
        }
        if (parsed.Count == 0)
            throw new ContractParseException($"'{kind}' operation requires at least one changed field.");
        return new AdhocRetargetPayload(
            RetargetPayloadParsing.Ids(after, "retiredForms", kind),
            ClosedPayloadParsing.GetRequiredString(after, "censusDigest", kind), parsed);
    }
}

internal sealed class BundleMorphHandler : ICompoundOperationHandler
{
    internal static readonly BundleMorphHandler Instance = new();
    private BundleMorphHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var payload = Parse(operation, AllomorphRetargetOperationKinds.SetBundleMorph);
        var (id, bundle) = TargetResolution.Resolve<IWfiMorphBundle>(cache, operation,
            AllomorphRetargetOperationKinds.SetBundleMorph);
        touchedTargets.Add(id);
        var source = ResolveAffix(cache, payload.RetiredForm, AllomorphRetargetOperationKinds.SetBundleMorph);
        var replacement = ResolveAffix(cache, payload.Replacement, AllomorphRetargetOperationKinds.SetBundleMorph);
        ValidateOrdinaryRetarget(cache, source, replacement);
        if (bundle.MorphRA?.Guid != source.Guid)
            throw new InvalidOperationException("The bundle no longer refers to the declared retired form.");
        ValidateBundleText(cache, bundle, source, replacement);

        var beforeMorph = ReferenceFieldSnapshotting.ReadAlternatives(bundle.MorphRA);
        var beforeForm = MultiAlternativesFieldSnapshotting.ReadAlternatives(cache, bundle.Form);
        bundle.MorphRA = replacement;
        var afterMorph = ReferenceFieldSnapshotting.ReadAlternatives(bundle.MorphRA);
        var afterForm = MultiAlternativesFieldSnapshotting.ReadAlternatives(cache, bundle.Form);
        return
        [
            new ExpectedEffect(id, SnapshotFields.WfiMorphBundleMorph, beforeMorph, afterMorph),
            new ExpectedEffect(id, SnapshotFields.WfiMorphBundleForm, beforeForm, afterForm)
        ];
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) =>
        ReadCurrentFootprintEffects(cache, operation)[0];

    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var payload = Parse(operation, AllomorphRetargetOperationKinds.SetBundleMorph);
        var (id, bundle) = TargetResolution.Resolve<IWfiMorphBundle>(cache, operation,
            AllomorphRetargetOperationKinds.SetBundleMorph);
        RequireCensus(cache, payload.RetiredForms, payload.CensusDigest);
        var source = ResolveAffix(cache, payload.RetiredForm, AllomorphRetargetOperationKinds.SetBundleMorph);
        var replacement = ResolveAffix(cache, payload.Replacement, AllomorphRetargetOperationKinds.SetBundleMorph);
        var morph = ReferenceFieldSnapshotting.ReadAlternatives(bundle.MorphRA);
        var form = MultiAlternativesFieldSnapshotting.ReadAlternatives(cache, bundle.Form);
        return
        [
            new ExpectedEffect(id, SnapshotFields.WfiMorphBundleMorph, morph, morph),
            new ExpectedEffect(id, SnapshotFields.WfiMorphBundleForm, form, form),
            AllomorphRetargetContextReader.BundleEffect(cache, id, bundle, source, replacement),
            CensusEffect(cache, payload.RetiredForms, id)
        ];
    }

    private static BundleMorphRetargetPayload Parse(OperationEnvelope operation, string kind)
    {
        if (operation.Target is null || operation.After is null)
            throw new InvalidOperationException($"'{kind}' operation requires 'target' and 'after'.");
        return BundleMorphRetargetPayload.Parse(operation.After.Value);
    }

    internal static IMoAffixAllomorph ResolveAffix(LcmCache cache, CanonicalId id, string kind) =>
        ReferenceFieldLowering.Resolve<IMoAffixAllomorph>(cache, id, kind);

    internal static void ValidateOrdinaryRetarget(
        LcmCache cache, IMoAffixAllomorph source, IMoAffixAllomorph replacement)
    {
        if (source.Guid == replacement.Guid)
            throw new InvalidOperationException("An allomorph cannot be retargeted to itself.");
        var entry = source.Owner as ILexEntry;
        if (entry is null || replacement.Owner?.Guid != entry.Guid || !entry.AlternateFormsOS.Contains(source))
            throw new InvalidOperationException("Allomorph retargeting requires source and replacement in one entry.");
        if (!entry.AlternateFormsOS.Contains(replacement) && entry.LexemeFormOA?.Guid != replacement.Guid)
            throw new InvalidOperationException("The replacement must be an alternate or lexeme form in the same entry.");
        var sourceType = source.MorphTypeRA?.Guid;
        var type = sourceType;
        if ((type != MoMorphTypeTags.kguidMorphPrefix && type != MoMorphTypeTags.kguidMorphSuffix) ||
            replacement.MorphTypeRA?.Guid != type)
            throw new InvalidOperationException("Retargeting supports matching simple prefix and suffix forms only.");
        if (source.IsAbstract || replacement.IsAbstract)
            throw new InvalidOperationException("Pattern and abstract allomorphs cannot be retargeted.");
        _ = cache;
    }

    private static void ValidateBundleText(
        LcmCache cache, IWfiMorphBundle bundle, IMoAffixAllomorph source, IMoAffixAllomorph replacement)
    {
        var sourceText = ReadFormAlternatives(cache, source.Form);
        var bundleText = ReadBundleAlternatives(cache, bundle.Form, requirePlain: true);
        var replacementText = ReadFormAlternatives(cache, replacement.Form);
        if (!SameAlternatives(sourceText, bundleText))
            throw new InvalidOperationException("The bundle Form contains customized text and cannot be overwritten safely.");
        if (sourceText.Keys.Any(ws => !replacementText.ContainsKey(ws)))
            throw new InvalidOperationException("The replacement lacks a populated writing-system alternative on the source form.");
    }

    internal static IReadOnlyDictionary<string, string> ReadFormAlternatives(LcmCache cache, IMultiAccessorBase form)
    {
        var alternatives = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ws in form.AvailableWritingSystemIds)
        {
            var text = form.get_String(ws)?.Text?.Normalize(System.Text.NormalizationForm.FormD);
            if (!string.IsNullOrEmpty(text))
                alternatives[cache.WritingSystemFactory.GetStrFromWs(ws)] = text;
        }
        return alternatives;
    }

    private static IReadOnlyDictionary<string, string> ReadBundleAlternatives(
        LcmCache cache, IMultiAccessorBase form, bool requirePlain)
    {
        var alternatives = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ws in form.AvailableWritingSystemIds)
        {
            var value = form.get_String(ws);
            var text = value?.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text;
            if (string.IsNullOrEmpty(text)) continue;
            if (requirePlain) RequirePlain(value!, ws);
            alternatives[cache.WritingSystemFactory.GetStrFromWs(ws)] =
                text.Normalize(System.Text.NormalizationForm.FormD);
        }
        return alternatives;
    }

    private static void RequirePlain(ITsString value, int expectedWritingSystem)
    {
        if (value.RunCount != 1)
            throw new InvalidOperationException("Rich or multi-run bundle Form text cannot be retargeted safely.");
        var properties = value.get_Properties(0);
        var plainProperties = TsStringUtils.MakeString(value.Text, expectedWritingSystem).get_Properties(0);
        if (!SameProperties(properties, plainProperties))
            throw new InvalidOperationException("Bundle Form carries non-writing-system text properties.");
    }

    private static bool SameProperties(ITsTextProps actual, ITsTextProps plain)
    {
        if (actual.IntPropCount != plain.IntPropCount || actual.StrPropCount != plain.StrPropCount)
            return false;
        for (var index = 0; index < actual.IntPropCount; index++)
        {
            actual.GetIntProp(index, out var actualType, out var actualValue);
            plain.GetIntProp(index, out var plainType, out var plainValue);
            if (actualType != plainType || actualValue != plainValue) return false;
        }
        for (var index = 0; index < actual.StrPropCount; index++)
        {
            var actualValue = actual.GetStrProp(index, out var actualType);
            var plainValue = plain.GetStrProp(index, out var plainType);
            if (actualType != plainType || actualValue != plainValue) return false;
        }
        return true;
    }

    internal static bool SameAlternatives(
        IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    internal static void RequireCensus(LcmCache cache, IReadOnlyList<CanonicalId> forms, string expected)
    {
        var actual = AllomorphRetargetCensusReader.ComputeDigest(cache, forms.Select(item => item.ToGuid()));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("The allomorph reference footprint changed after retarget composition.");
    }

    internal static ExpectedEffect CensusEffect(
        LcmCache cache, IReadOnlyList<CanonicalId> forms, CanonicalId target)
    {
        var digest = AllomorphRetargetCensusReader.ComputeDigest(cache, forms.Select(item => item.ToGuid()));
        var value = new Dictionary<string, string> { ["digest"] = digest };
        return new ExpectedEffect(target, SnapshotFields.AllomorphRetargetCensus, value, value);
    }
}

internal sealed class AdhocReferencesHandler : ICompoundOperationHandler
{
    internal static readonly AdhocReferencesHandler Instance = new();
    private AdhocReferencesHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var payload = Parse(operation);
        var (id, rule) = TargetResolution.Resolve<IMoAlloAdhocProhib>(cache, operation,
            AllomorphRetargetOperationKinds.RetargetAdhocReferences);
        touchedTargets.Add(id);
        ValidateAndApply(cache, rule, payload, apply: false);
        var before = ReadChangedFields(rule, payload.Fields);
        ValidateAndApply(cache, rule, payload, apply: true);
        var after = ReadChangedFields(rule, payload.Fields);
        return payload.Fields.Select(field => new ExpectedEffect(id, SnapshotField(field.Field),
            before[field.Field], after[field.Field])).ToArray();
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) =>
        ReadCurrentFootprintEffects(cache, operation)[0];

    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var payload = Parse(operation);
        var (id, rule) = TargetResolution.Resolve<IMoAlloAdhocProhib>(cache, operation,
            AllomorphRetargetOperationKinds.RetargetAdhocReferences);
        BundleMorphHandler.RequireCensus(cache, payload.RetiredForms, payload.CensusDigest);
        var current = ReadChangedFields(rule, payload.Fields);
        var effects = payload.Fields.Select(field => new ExpectedEffect(id, SnapshotField(field.Field),
            current[field.Field], current[field.Field])).ToList();
        effects.Add(AllomorphRetargetContextReader.AdhocEffect(cache, id, rule, payload));
        effects.Add(BundleMorphHandler.CensusEffect(cache, payload.RetiredForms, id));
        return effects;
    }

    private static AdhocRetargetPayload Parse(OperationEnvelope operation)
    {
        if (operation.Target is null || operation.After is null)
            throw new InvalidOperationException($"'{AllomorphRetargetOperationKinds.RetargetAdhocReferences}' operation requires 'target' and 'after'.");
        return AdhocRetargetPayload.Parse(operation.After.Value);
    }

    private static void ValidateAndApply(
        LcmCache cache, IMoAlloAdhocProhib rule, AdhocRetargetPayload payload, bool apply)
    {
        var retired = payload.RetiredForms.Select(item => item.ToGuid()).ToHashSet();
        var firstAfter = CurrentIds(rule.FirstAllomorphRA);
        var restAfter = CurrentIds(rule.RestOfAllosRS);

        foreach (var field in payload.Fields)
        {
            var current = CurrentIds(rule, field.Field);
            if (!current.SequenceEqual(field.Before.Select(item => item.ToGuid())))
                throw new InvalidOperationException($"Ad hoc field '{field.Field}' changed after retarget composition.");
            var before = field.Before.Select(item => item.ToGuid()).ToArray();
            var after = field.After.Select(item => item.ToGuid()).ToArray();
            for (var index = 0; index < before.Length; index++)
            {
                if (retired.Contains(before[index]) ? before[index] == after[index] : before[index] != after[index])
                    throw new InvalidOperationException("Every retired reference must change and every other reference must stay in place.");
                if (retired.Contains(after[index]))
                    throw new InvalidOperationException("A retired form cannot remain an ad hoc destination.");
            }
            if (after.Distinct().Count() != after.Length)
                throw new InvalidOperationException("Retargeting cannot collapse conjunctive allomorph members.");
            for (var index = 0; index < before.Length; index++)
            {
                if (before[index] == after[index]) continue;
                var source = BundleMorphHandler.ResolveAffix(cache, CanonicalId.FromGuid(before[index]),
                    AllomorphRetargetOperationKinds.RetargetAdhocReferences);
                var replacement = BundleMorphHandler.ResolveAffix(cache, CanonicalId.FromGuid(after[index]),
                    AllomorphRetargetOperationKinds.RetargetAdhocReferences);
                BundleMorphHandler.ValidateOrdinaryRetarget(cache, source, replacement);
            }
            if (!rule.Disabled)
                ValidateActiveDenotation(cache, before, after);

            switch (field.Field)
            {
                case "FirstAllomorph": firstAfter = after.ToList(); break;
                case "RestOfAllos": restAfter = after.ToList(); break;
            }
        }

        if (firstAfter.Count == 1 && restAfter.Contains(firstAfter[0]))
            throw new InvalidOperationException("Retargeting would make FirstAllomorph overlap RestOfAllos.");
        if (!apply) return;

        foreach (var field in payload.Fields)
        {
            var values = field.After.Select(item => ReferenceFieldLowering.Resolve<IMoForm>(cache, item,
                AllomorphRetargetOperationKinds.RetargetAdhocReferences)).ToArray();
            switch (field.Field)
            {
                case "FirstAllomorph": rule.FirstAllomorphRA = values[0]; break;
                case "RestOfAllos": ReplaceSequence(rule.RestOfAllosRS, values); break;
                case "Allomorphs": ReplaceSequence(rule.AllomorphsRS, values); break;
            }
        }
    }

    private static void ValidateActiveDenotation(LcmCache cache, IReadOnlyList<Guid> before, IReadOnlyList<Guid> after)
    {
        for (var index = 0; index < before.Count; index++)
        {
            if (before[index] == after[index]) continue;
            var source = cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(before[index]) as IMoAffixAllomorph;
            var replacement = cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(after[index]) as IMoAffixAllomorph;
            if (source is null || replacement is null || source.Owner?.Guid != replacement.Owner?.Guid ||
                source.MorphTypeRA?.Guid != replacement.MorphTypeRA?.Guid || source.IsAbstract || replacement.IsAbstract ||
                source.MsEnvFeaturesOA is not null || replacement.MsEnvFeaturesOA is not null ||
                source.PhoneEnvRC.Count != 0 || replacement.PhoneEnvRC.Count != 0 ||
                source.PositionRS.Count != 0 || replacement.PositionRS.Count != 0 ||
                !BundleMorphHandler.SameAlternatives(BundleMorphHandler.ReadFormAlternatives(cache, source.Form),
                    BundleMorphHandler.ReadFormAlternatives(cache, replacement.Form)))
                throw new InvalidOperationException("Active allomorph prohibition meaning is not provably unchanged.");
        }
    }

    private static void ReplaceSequence(IList<IMoForm> sequence, IReadOnlyList<IMoForm> values)
    {
        if (sequence.Count != values.Count)
            throw new InvalidOperationException("An ad hoc sequence retarget must preserve its member count.");
        for (var index = 0; index < values.Count; index++) sequence[index] = values[index];
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> ReadChangedFields(
        IMoAlloAdhocProhib rule, IReadOnlyList<AdhocRetargetField> fields) => fields.ToDictionary(
        field => field.Field,
        field => FieldSnapshot(rule, field.Field),
        StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> FieldSnapshot(IMoAlloAdhocProhib rule, string field) => field switch
    {
        "FirstAllomorph" => ReferenceFieldSnapshotting.ReadAlternatives(rule.FirstAllomorphRA),
        "RestOfAllos" => ReferenceSequenceFieldSnapshotting.ReadAlternatives(rule.RestOfAllosRS),
        "Allomorphs" => ReferenceSequenceFieldSnapshotting.ReadAlternatives(rule.AllomorphsRS),
        _ => throw new InvalidOperationException($"Unsupported ad hoc field '{field}'.")
    };

    private static string SnapshotField(string field) => field switch
    {
        "FirstAllomorph" => SnapshotFields.MoAlloAdhocProhibFirstAllomorph,
        "RestOfAllos" => SnapshotFields.MoAlloAdhocProhibRestOfAllos,
        "Allomorphs" => SnapshotFields.MoAlloAdhocProhibAllomorphs,
        _ => throw new InvalidOperationException($"Unsupported ad hoc field '{field}'.")
    };

    private static IReadOnlyList<Guid> CurrentIds(ICmObject? value) => value is null ? [] : [value.Guid];
    private static IReadOnlyList<Guid> CurrentIds(IEnumerable<ICmObject> values) => values.Select(item => item.Guid).ToArray();
    private static IReadOnlyList<Guid> CurrentIds(IMoAlloAdhocProhib rule, string field) => field switch
    {
        "FirstAllomorph" => CurrentIds(rule.FirstAllomorphRA),
        "RestOfAllos" => CurrentIds(rule.RestOfAllosRS),
        "Allomorphs" => CurrentIds(rule.AllomorphsRS),
        _ => throw new InvalidOperationException($"Unsupported ad hoc field '{field}'.")
    };
}

internal static class RetargetPayloadParsing
{
    public static CanonicalId Id(JsonElement value, string name, string kind) =>
        ClosedPayloadParsing.GetRequiredCanonicalId(value, name, kind);

    public static IReadOnlyList<CanonicalId> Ids(JsonElement value, string name, string kind, bool allowEmpty = false)
    {
        if (!value.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{kind}' operation 'after.{name}' must be an array.");
        var result = new List<CanonicalId>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String ||
                !CanonicalId.TryParse(element.GetString(), out var id))
                throw new ContractParseException($"'{kind}' operation 'after.{name}' must contain canonical ids.");
            if (result.Contains(id))
                throw new ContractParseException($"'{kind}' operation 'after.{name}' cannot repeat an id.");
            result.Add(id);
        }
        if (!allowEmpty && result.Count == 0)
            throw new ContractParseException($"'{kind}' operation 'after.{name}' cannot be empty.");
        return result;
    }
}
