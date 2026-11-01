using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Turns an existing affix's grammatical info into another kind of affix info, keeping what carries across.</summary>
/// <param name="Msa">The affix's current grammatical info: inflectional, derivational or unclassified.</param>
/// <param name="Kind">The kind to change to: inflectional, derivational or unclassified.</param>
/// <param name="ToCategory">For a change to derivational only: the category of the word it makes; the attaching category when absent.</param>
/// <param name="Slots">For a change to inflectional only: the existing slots it fills; none when absent.</param>
public sealed record ChangeAffixKindIntent(CanonicalId Msa, LexicalEntryMsaKind Kind,
    CanonicalId? ToCategory = null, IReadOnlyList<CanonicalId>? Slots = null);

/// <summary>Parses the closed JSON intent for changing an affix's kind.</summary>
public static class ChangeAffixKindIntentParser
{
    private const string ConstructName = "ChangeAffixKind";

    /// <summary>Reads the grammatical info, the new kind and the extras only that kind takes.</summary>
    public static ChangeAffixKindIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "msa", "kind", "toCategory", "slots");
        var kind = SoundSystemIntentParsing.Text(authored, "kind") switch
        {
            "inflectional" => LexicalEntryMsaKind.Inflectional,
            "derivational" => LexicalEntryMsaKind.Derivational,
            "unclassified" => LexicalEntryMsaKind.Unclassified,
            var other => throw new ContractParseException(
                $"'{ConstructName}': kind '{other}' must be inflectional, derivational or unclassified."),
        };
        var toCategory = authored.TryGetProperty("toCategory", out _) ? SoundSystemIntentParsing.Id(authored, "toCategory") : (CanonicalId?)null;
        var slots = authored.TryGetProperty("slots", out _) ? SoundSystemIntentParsing.Ids(authored, "slots", ordered: false) : null;
        var intent = new ChangeAffixKindIntent(SoundSystemIntentParsing.Id(authored, "msa"), kind, toCategory, slots);
        ChangeAffixKindComposer.RequireExtrasFitKind(intent);
        return intent;
    }
}

/// <summary>
/// Lowers a change of an affix's kind to a new grammatical info of the other kind, a relink of every sense, ad hoc
/// morpheme prohibition and parser-analysis morph bundle that used the old one, and the deletion of the old one.
/// Black's guidance is to pick a kind and change it later, so the choice (ADR 0079) stays reversible in place;
/// what the new kind cannot hold is reported as a related object, not refused.
/// </summary>
public static class ChangeAffixKindComposer
{
    private const string ConstructName = "ChangeAffixKind";
    private const string Rationale = "Authored by the ChangeAffixKind composer.";

    /// <summary>Checks that each extra belongs to the chosen kind.</summary>
    internal static void RequireExtrasFitKind(ChangeAffixKindIntent intent)
    {
        if (!Enum.IsDefined(intent.Kind) || intent.Kind == LexicalEntryMsaKind.Stem)
            throw new ContractParseException($"'{ConstructName}': kind must be inflectional, derivational or unclassified.");
        if (intent.ToCategory is not null && intent.Kind != LexicalEntryMsaKind.Derivational)
            throw new ContractParseException($"'{ConstructName}': only a change to derivational takes 'toCategory'.");
        if (intent.Slots is not null && intent.Kind != LexicalEntryMsaKind.Inflectional)
            throw new ContractParseException($"'{ConstructName}': only a change to inflectional takes 'slots'.");
    }

    /// <summary>Builds the operations without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, ChangeAffixKindIntent intent, Func<CanonicalId>? mintId = null)
    {
        var plan = Inspect(cache, intent);
        if (plan is null) return [];
        var source = mintId ?? (() => CanonicalId.Mint());
        CanonicalId Mint() => LexicalFormAuthoring.Normalize(source());
        var operations = new List<OperationEnvelope>();
        var entryId = CanonicalId.FromGuid(plan.Entry.Guid);
        var newId = Mint();
        var create = Add(LexEntryMorphoSyntaxAnalysesOperationKinds.Create, entryId, new { @class = ClassName(intent.Kind) },
            entityId: newId);
        var afterCreate = new[] { new OperationDependency(create) };
        void Set(string kind, CanonicalId reference) => Add(kind, newId, new { @ref = reference.Value }, dependsOn: afterCreate);
        void Member(string kind, CanonicalId member) => Add(kind, newId, new { member = member.Value }, dependsOn: afterCreate);
        switch (intent.Kind)
        {
            case LexicalEntryMsaKind.Inflectional:
                if (plan.Category is { } inflCategory) Set(MoInflAffMsaPartOfSpeechOperationKinds.SetPartOfSpeech, inflCategory);
                foreach (var slot in plan.Slots) Member(MoInflAffMsaSlotsOperationKinds.AddRefSlots, slot);
                foreach (var feature in plan.CarriedExceptionFeatures)
                    Member(MoInflAffMsaFromProdRestrictOperationKinds.AddRefFromProdRestrict, feature);
                break;
            case LexicalEntryMsaKind.Derivational:
                if (plan.Category is { } from) Set(MoDerivAffMsaFromPartOfSpeechOperationKinds.SetFromPartOfSpeech, from);
                if (plan.ToCategory is { } to) Set(MoDerivAffMsaToPartOfSpeechOperationKinds.SetToPartOfSpeech, to);
                foreach (var feature in plan.CarriedExceptionFeatures)
                    Member(MoDerivAffMsaFromProdRestrictOperationKinds.AddRefFromProdRestrict, feature);
                break;
            case LexicalEntryMsaKind.Unclassified:
                if (plan.Category is { } category)
                    Set(MoUnclassifiedAffixMsaPartOfSpeechOperationKinds.SetPartOfSpeech, category);
                break;
        }

        var oldId = CanonicalId.FromGuid(plan.Old.Guid);
        var moveUse = new { from = oldId.Value, to = newId.Value };
        // LibLCM clears bundles naming an info once no sense uses it, so the senses move last.
        var relinks = plan.Prohibitions.Select(rule => Add(MsaReferenceRetargetOperationKinds.RetargetProhibitionMorphemes,
                CanonicalId.FromGuid(rule.Guid), moveUse, dependsOn: afterCreate))
            .Concat(plan.Bundles.Select(bundle => Add(MsaReferenceRetargetOperationKinds.SetBundleMsa,
                CanonicalId.FromGuid(bundle.Guid), moveUse, dependsOn: afterCreate))).ToArray();
        var senseDependencies = relinks.Select(id => new OperationDependency(id)).Concat(afterCreate).ToArray();
        var senseRelinks = plan.Senses.Select(sense => Add(LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis,
            CanonicalId.FromGuid(sense.Guid), new { @ref = newId.Value }, dependsOn: senseDependencies)).ToArray();
        Add(MorphoSyntaxAnalysisDeleteOperationKinds.Delete, oldId, new { },
            dependsOn: relinks.Concat(senseRelinks).Select(id => new OperationDependency(id)).Concat(afterCreate).ToArray());
        return operations;

        CanonicalId Add(string kind, CanonicalId target, object after, CanonicalId? entityId = null,
            IReadOnlyList<OperationDependency>? dependsOn = null)
        {
            var operationId = Mint();
            operations.Add(new OperationEnvelope(operationId, kind, entityId: entityId, target: target,
                after: JsonSerializer.SerializeToElement(after), dependsOn: dependsOn, rationale: Rationale));
            return operationId;
        }
    }

    /// <summary>Reports what the change cannot carry across, so the person sees it before applying.</summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadWarnings(LcmCache cache, ChangeAffixKindIntent intent) =>
        Inspect(cache, intent)?.Warnings ?? [];

    private sealed record Plan(IMoMorphSynAnalysis Old, ILexEntry Entry, ILexSense[] Senses,
        IMoMorphAdhocProhib[] Prohibitions, IWfiMorphBundle[] Bundles, CanonicalId? Category,
        CanonicalId? ToCategory, CanonicalId[] Slots, CanonicalId[] CarriedExceptionFeatures,
        IReadOnlyList<ComposedRelatedObject> Warnings);

    private static Plan? Inspect(LcmCache cache, ChangeAffixKindIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        try { RequireExtrasFitKind(intent); }
        catch (ContractParseException problem) { throw new InvalidOperationException(problem.Message); }
        var msaId = LexicalFormAuthoring.Normalize(intent.Msa);
        var old = LexicalFormAuthoring.Resolve<IMoMorphSynAnalysis>(cache, msaId, ConstructName);
        var current = KindOf(old) ?? throw new InvalidOperationException(
            $"'{ConstructName}': '{msaId.Value}' is not the grammatical info of an affix. A stem's grammatical info " +
            "belongs to a stem; only an affix is inflectional, derivational or unclassified.");
        if (old.Owner is not ILexEntry entry)
            throw new InvalidOperationException($"'{ConstructName}': '{msaId.Value}' does not belong to an entry.");
        if (entry.LexemeFormOA is not IMoAffixAllomorph && entry.LexemeFormOA is not IMoAffixProcess)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{msaId.Value}' belongs to an entry whose lexeme form is not an affix form.");
        if (current == intent.Kind) return null;

        var category = old switch
        {
            IMoInflAffMsa infl => infl.PartOfSpeechRA,
            IMoDerivAffMsa deriv => deriv.FromPartOfSpeechRA,
            IMoUnclassifiedAffixMsa unclassified => unclassified.PartOfSpeechRA,
            _ => null,
        };
        var categoryId = category is null ? (CanonicalId?)null : CanonicalId.FromGuid(category.Guid);
        CanonicalId? toCategory = null;
        if (intent.Kind == LexicalEntryMsaKind.Derivational)
        {
            toCategory = intent.ToCategory is { } named
                ? CanonicalId.FromGuid(LexicalFormAuthoring.Resolve<IPartOfSpeech>(cache, named, ConstructName).Guid)
                : categoryId;
        }

        var slots = (intent.Slots ?? []).Select(LexicalFormAuthoring.Normalize).ToArray();
        if (slots.Select(id => id.ToGuid()).Distinct().Count() != slots.Length)
            throw new InvalidOperationException($"'{ConstructName}': slots must not repeat.");
        foreach (var slotId in slots)
        {
            var slot = LexicalFormAuthoring.Resolve<IMoInflAffixSlot>(cache, slotId, ConstructName);
            if (category is null || !LexicalFormAuthoring.InCategoryOrAncestor(slot.Owner as IPartOfSpeech, category))
                throw new InvalidOperationException(
                    $"'{ConstructName}': slot '{slotId.Value}' does not belong to the affix category or one of its ancestors.");
        }

        var senses = entry.AllSenses.Where(sense => sense.MorphoSyntaxAnalysisRA?.Guid == old.Guid).ToArray();
        var senseIds = senses.Select(sense => sense.Guid).ToHashSet();
        var prohibitions = old.ReferringObjects.OfType<IMoMorphAdhocProhib>()
            .OrderBy(rule => rule.Guid).ToArray();
        var bundles = old.ReferringObjects.OfType<IWfiMorphBundle>().OrderBy(bundle => bundle.Guid).ToArray();
        // Senses, morpheme prohibitions and morph bundles are the users LibLCM holds, and each is moved in place.
        var other = old.ReferringObjects.FirstOrDefault(user => !senseIds.Contains(user.Guid) && !ReferenceEquals(user, old) &&
            user is not IMoMorphAdhocProhib && user is not IWfiMorphBundle);
        if (other is not null)
            throw new InvalidOperationException(
                $"'{ConstructName}': a {other.ClassName} ({other.Guid:D}) still refers to '{msaId.Value}' and Motif has no way " +
                "to point that kind of object at another grammatical info; retarget or remove that use first.");

        var carried = CarriedExceptionFeatures(old, intent.Kind);
        var warnings = Warnings(cache, entry, old, intent, category, toCategory, slots, carried, prohibitions, bundles);
        return new Plan(old, entry, senses, prohibitions, bundles, categoryId, toCategory, slots, carried, warnings);
    }

    private static CanonicalId[] CarriedExceptionFeatures(IMoMorphSynAnalysis old, LexicalEntryMsaKind kind)
    {
        if (kind == LexicalEntryMsaKind.Unclassified) return [];
        IEnumerable<ICmObject> features = old switch
        {
            IMoInflAffMsa infl => infl.FromProdRestrictRC,
            IMoDerivAffMsa deriv => deriv.FromProdRestrictRC,
            _ => [],
        };
        return features.Select(item => CanonicalId.FromGuid(item.Guid)).OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
    }

    private static List<ComposedRelatedObject> Warnings(LcmCache cache, ILexEntry entry, IMoMorphSynAnalysis old,
        ChangeAffixKindIntent intent, IPartOfSpeech? category, CanonicalId? toCategory, CanonicalId[] slots,
        CanonicalId[] carried, IMoMorphAdhocProhib[] prohibitions, IWfiMorphBundle[] bundles)
    {
        var warnings = new List<ComposedRelatedObject>();
        void Lost(string kind, ICmObject item, string what) => warnings.Add(new ComposedRelatedObject(kind,
            CanonicalId.FromGuid(item.Guid).Value, $"{NameOf(item)} - {what}"));
        var leavesInflectional = old is IMoInflAffMsa && intent.Kind != LexicalEntryMsaKind.Inflectional;
        if (old is IMoInflAffMsa infl)
        {
            if (leavesInflectional)
                foreach (var slot in infl.SlotsRC)
                    Lost("slot", slot, "no longer filled by this affix; only an inflectional affix fills a template slot");
            if (intent.Kind == LexicalEntryMsaKind.Unclassified)
                foreach (var feature in infl.FromProdRestrictRC) Lost("exception-feature", feature, "dropped by the change");
            if (infl.InflFeatsOA is { FeatureSpecsOC.Count: > 0 } inflFeatures)
                Lost("inflection-features", inflFeatures, "dropped by the change");
            if (leavesInflectional && FormInflectionClasses(entry) is { Length: > 0 } classes)
                foreach (var cls in classes)
                    Lost("inflection-class", cls,
                        "stays on the affix form; compose-edit-derivational-affix-restriction restricts a derivational affix");
        }

        if (old is IMoDerivAffMsa deriv)
        {
            if (deriv.FromInflectionClassRA is { } from)
                Lost("inflection-class", from, "From Inflection Class dropped by the change");
            if (deriv.ToInflectionClassRA is { } to) Lost("inflection-class", to, "To Inflection Class dropped by the change");
            foreach (var feature in deriv.ToProdRestrictRC) Lost("exception-feature", feature, "To Exception Feature dropped by the change");
            if (intent.Kind == LexicalEntryMsaKind.Unclassified)
                foreach (var feature in deriv.FromProdRestrictRC) Lost("exception-feature", feature, "dropped by the change");
            if (deriv.FromStemNameRA is { } stemName) Lost("stem-name", stemName, "From Stem Name dropped by the change");
            if (deriv.FromMsFeaturesOA is { FeatureSpecsOC.Count: > 0 } fromFeatures)
                Lost("features", fromFeatures, "From features dropped by the change");
            if (deriv.ToMsFeaturesOA is { FeatureSpecsOC.Count: > 0 } toFeatures)
                Lost("features", toFeatures, "To features dropped by the change");
            if (deriv.ToPartOfSpeechRA is { } outputCategory && outputCategory.Guid != deriv.FromPartOfSpeechRA?.Guid)
                Lost("category", outputCategory, "To category dropped by the change; the other kinds keep one category");
        }

        foreach (var rule in prohibitions)
        {
            var inertUntilSlot = intent.Kind == LexicalEntryMsaKind.Inflectional && slots.Length == 0
                ? "; it has no effect until the affix fills a template slot" : "";
            warnings.Add(new ComposedRelatedObject("ad-hoc-prohibition", CanonicalId.FromGuid(rule.Guid).Value,
                $"repointed to the new {KindWord(intent.Kind)} grammatical info (it names the {KindWord(KindOf(old))} one now); " +
                "it still bans the same affix, but a prohibition written for one kind of affix now applies to the " +
                $"{KindWord(intent.Kind)} one{inertUntilSlot}"));
        }

        if (bundles.Length > 0)
            warnings.Add(new ComposedRelatedObject("morph-bundles", CanonicalId.FromGuid(old.Guid).Value,
                $"{bundles.Length} parser-analysis morph bundle(s) repointed from the {KindWord(KindOf(old))} to the " +
                $"{KindWord(intent.Kind)} grammatical info; those analyses now state the affix as {KindWord(intent.Kind)}"));
        if (intent.Kind == LexicalEntryMsaKind.Inflectional && slots.Length == 0)
            warnings.Add(new ComposedRelatedObject("affix", CanonicalId.FromGuid(entry.Guid).Value,
                "the affix fills no template slot yet; an inflectional affix applies only in a slot " +
                "(compose-edit-inflectional-affix adds one)"));
        if (intent.Kind == LexicalEntryMsaKind.Derivational && toCategory is null && category is not null)
            warnings.Add(new ComposedRelatedObject("affix", CanonicalId.FromGuid(entry.Guid).Value,
                "no To category could be set"));
        return warnings.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
    }

    private static string KindWord(LexicalEntryMsaKind? kind) => kind switch
    {
        LexicalEntryMsaKind.Inflectional => "inflectional",
        LexicalEntryMsaKind.Derivational => "derivational",
        _ => "unclassified",
    };

    private static ICmObject[] FormInflectionClasses(ILexEntry entry) =>
        new IMoForm?[] { entry.LexemeFormOA }.Concat(entry.AlternateFormsOS).OfType<IMoAffixForm>()
            .SelectMany(form => form.InflectionClassesRC).DistinctBy(cls => cls.Guid).Cast<ICmObject>().ToArray();

    private static string NameOf(ICmObject item) => item switch
    {
        IMoInflAffixSlot slot => slot.Name.BestAnalysisAlternative.Text,
        ICmPossibility possibility => possibility.Name.BestAnalysisAlternative.Text,
        IMoStemName stemName => stemName.Name.BestAnalysisAlternative.Text,
        _ => item.ClassName,
    };

    private static LexicalEntryMsaKind? KindOf(IMoMorphSynAnalysis msa) => msa switch
    {
        IMoInflAffMsa => LexicalEntryMsaKind.Inflectional,
        IMoDerivAffMsa => LexicalEntryMsaKind.Derivational,
        IMoUnclassifiedAffixMsa => LexicalEntryMsaKind.Unclassified,
        _ => null,
    };

    private static string ClassName(LexicalEntryMsaKind kind) => kind switch
    {
        LexicalEntryMsaKind.Inflectional => "MoInflAffMsa",
        LexicalEntryMsaKind.Derivational => "MoDerivAffMsa",
        _ => "MoUnclassifiedAffixMsa",
    };
}
