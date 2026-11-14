using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Which stems a query selects; every given condition must hold and at least one must be given.</summary>
/// <param name="Category">The stem's part of speech.</param>
/// <param name="FinalLetters">The letters the entry's lexeme form ends with, in its default vernacular writing system.</param>
/// <param name="HasFeature">An exception feature the stem already carries.</param>
/// <param name="LacksFeature">An exception feature the stem does not carry.</param>
/// <param name="GlossContains">Text a gloss of a sense using the stem contains, ignoring case.</param>
public sealed record StemQuery(CanonicalId? Category = null, string? FinalLetters = null,
    CanonicalId? HasFeature = null, CanonicalId? LacksFeature = null, string? GlossContains = null);

/// <summary>Tags, or untags, every stem a query selects with one exception feature, choosing the cheapest polarity FieldWorks can express.</summary>
/// <param name="Query">Selects the stems that belong to the class the gate treats as "in".</param>
/// <param name="ExceptionFeature">The exception feature to give, or take away.</param>
/// <param name="Gate">
/// What reads the feature. "rule": a phonological rule, which can require or exclude it, so the smaller of the matching
/// stems and the other stems of the same scope is tagged. "affix" (the default): an inflectional or derivational affix,
/// whose restrictions only require it, so the matching stems are tagged even when the others are fewer.
/// </param>
/// <param name="Action">"tag" (the default) or "untag", which removes the feature from the matching stems that carry it.</param>
public sealed record TagStemsByQueryIntent(
    StemQuery Query, CanonicalId ExceptionFeature, string? Gate = null, string? Action = null);

/// <summary>Parses the closed JSON intent for tagging stems by query.</summary>
public static class TagStemsByQueryIntentParser
{
    private const string Name = "TagStemsByQuery";

    /// <summary>Reads the query, the feature, the gate and the action.</summary>
    public static TagStemsByQueryIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, Name, "query", "exceptionFeature", "gate", "action");
        if (!authored.TryGetProperty("query", out var query)) throw new ContractParseException($"'{Name}': 'query' is required.");
        SoundSystemIntentParsing.Object(query, Name + " query", "category", "finalLetters", "hasFeature", "lacksFeature",
            "glossContains");
        return new TagStemsByQueryIntent(
            new StemQuery(Id(query, "category"), Text(query, "finalLetters"), Id(query, "hasFeature"),
                Id(query, "lacksFeature"), Text(query, "glossContains")),
            SoundSystemIntentParsing.Id(authored, "exceptionFeature"),
            Choice(authored, "gate", "rule", "affix"), Choice(authored, "action", "tag", "untag"));
    }

    private static CanonicalId? Id(JsonElement element, string name) =>
        element.TryGetProperty(name, out _) ? SoundSystemIntentParsing.Id(element, name) : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out _) ? SoundSystemIntentParsing.Text(element, name) : null;

    private static string? Choice(JsonElement element, string name, params string[] allowed)
    {
        var value = Text(element, name);
        return value is null || allowed.Contains(value) ? value
            : throw new ContractParseException($"'{Name}': '{name}' must be one of {string.Join(", ", allowed)}.");
    }
}

/// <summary>
/// Evaluates a <see cref="StemQuery"/> in one pass over the lexicon and lowers the tags to add or remove operations,
/// reusing the exception-feature composers' operation kinds. The pass costs O(stems), plus O(senses) only when a gloss
/// condition is given; no condition parses or builds anything per stem beyond reading its own form.
/// </summary>
public static class TagStemsByQueryComposer
{
    private const string ConstructName = "TagStemsByQuery";
    private const string Rationale = "Authored by the TagStemsByQuery composer.";

    /// <summary>The kind a summary carries in the related objects of a composed intent.</summary>
    public const string SummaryKind = "tag-by-query-summary";

    /// <summary>What a query selected and which stems a gate needs tagged, before any operation is built.</summary>
    /// <param name="Scope">Every stem grammatical info the query ranges over.</param>
    /// <param name="Matching">The stems the query selects.</param>
    /// <param name="Targets">The stems to tag or untag.</param>
    /// <param name="Polarity">"matching" or "others" for a tag; "matching" for an untag.</param>
    /// <param name="Limitation">The FieldWorks limitation that forced the polarity, or null.</param>
    public sealed record Plan(IReadOnlyList<IMoStemMsa> Scope, IReadOnlyList<IMoStemMsa> Matching,
        IReadOnlyList<IMoStemMsa> Targets, string Polarity, string? Limitation);

    /// <summary>Evaluates the query and chooses the polarity.</summary>
    public static Plan Evaluate(LcmCache cache, TagStemsByQueryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var query = intent.Query;
        if (query.Category is null && query.FinalLetters is null && query.HasFeature is null &&
            query.LacksFeature is null && query.GlossContains is null)
            throw new InvalidOperationException($"'{ConstructName}': 'query' needs at least one condition.");
        if (intent.Gate is not (null or "rule" or "affix") || intent.Action is not (null or "tag" or "untag"))
            throw new InvalidOperationException($"'{ConstructName}': 'gate' is rule or affix and 'action' is tag or untag.");
        var feature = LexicalFormAuthoring.Normalize(intent.ExceptionFeature);
        var features = new[] { feature }.Concat(new[] { query.HasFeature, query.LacksFeature }.OfType<CanonicalId>())
            .Select(LexicalFormAuthoring.Normalize).ToArray();
        ExceptionFeatureTargets.RequireProjectFeatures(cache, features, ConstructName);

        var category = query.Category is { } c ? LexicalFormAuthoring.Normalize(c).ToGuid() : (Guid?)null;
        var has = query.HasFeature is { } h ? LexicalFormAuthoring.Normalize(h).ToGuid() : (Guid?)null;
        var lacks = query.LacksFeature is { } l ? LexicalFormAuthoring.Normalize(l).ToGuid() : (Guid?)null;
        var final = query.FinalLetters?.Normalize(NormalizationForm.FormD);
        var scope = new List<IMoStemMsa>();
        var matching = new List<IMoStemMsa>();
        foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances())
        foreach (var msa in entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>())
        {
            if (category is { } wanted && msa.PartOfSpeechRA?.Guid != wanted) continue;
            scope.Add(msa);
            if (has is { } hasGuid && msa.ProdRestrictRC.All(item => item.Guid != hasGuid)) continue;
            if (lacks is { } lacksGuid && msa.ProdRestrictRC.Any(item => item.Guid == lacksGuid)) continue;
            if (final is not null && Form(cache, entry)?.EndsWith(final, StringComparison.Ordinal) != true) continue;
            if (query.GlossContains is { } gloss && !HasGloss(cache, entry, msa, gloss)) continue;
            matching.Add(msa);
        }

        if (intent.Action == "untag")
            return new Plan(scope, matching, matching.Where(msa => msa.ProdRestrictRC.Any(i => i.Guid == feature.ToGuid())).ToArray(),
                "matching", null);
        var matchingSet = matching.ToHashSet();
        var others = scope.Where(msa => !matchingSet.Contains(msa)).ToArray();
        if (intent.Gate != "rule")
            return new Plan(scope, matching, matching, "matching", others.Length < matching.Count
                ? $"FieldWorks affix restrictions only require a stem exception feature, never exclude one, so the {matching.Count} " +
                  $"matching stems must carry it although the other {others.Length} would need fewer tags. " +
                  "Motif adds no negative mechanism; record this as a FieldWorks limitation."
                : null);
        return others.Length < matching.Count
            ? new Plan(scope, matching, others, "others", null)
            : new Plan(scope, matching, matching, "matching", null);
    }

    /// <summary>Builds the additions or removals; a stem already in the wanted state contributes nothing.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, TagStemsByQueryIntent intent)
    {
        var plan = Evaluate(cache, intent);
        var feature = LexicalFormAuthoring.Normalize(intent.ExceptionFeature);
        var untag = intent.Action == "untag";
        var kinds = (Add: MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict,
            Remove: MoStemMsaProdRestrictOperationKinds.RemoveRefProdRestrict);
        return plan.Targets
            .Where(msa => msa.ProdRestrictRC.Any(item => item.Guid == feature.ToGuid()) == untag)
            .OrderBy(msa => msa.Guid)
            .Select(msa => new OperationEnvelope(CanonicalId.Mint(), untag ? kinds.Remove : kinds.Add,
                target: CanonicalId.FromGuid(msa.Guid),
                after: JsonSerializer.SerializeToElement(new { member = feature.Value }), rationale: Rationale))
            .ToArray();
    }

    /// <summary>One summary stating the counts first, then the polarity and any FieldWorks limitation.</summary>
    public static IReadOnlyList<ComposedRelatedObject> Summarize(LcmCache cache, TagStemsByQueryIntent intent)
    {
        var plan = Evaluate(cache, intent);
        var feature = LexicalFormAuthoring.Normalize(intent.ExceptionFeature);
        var verb = intent.Action == "untag" ? "untag" : "tag";
        var text = $"{plan.Matching.Count} of {plan.Scope.Count} stems match; {verb} {plan.Targets.Count} " +
                   $"({plan.Polarity}). ";
        if (verb == "tag")
            text += plan.Polarity == "others"
                ? "The rule must EXCLUDE this feature (ExcludedFeatures), because the other stems are fewer. "
                : intent.Gate == "rule" ? "The rule must REQUIRE this feature (RequiredFeatures). "
                : "The affix must require this feature (From Exception Features). ";
        if (plan.Limitation is not null) text += plan.Limitation;
        return [new ComposedRelatedObject(SummaryKind, feature.Value, text.Trim())];
    }

    private static string? Form(LcmCache cache, ILexEntry entry) =>
        entry.LexemeFormOA?.Form.get_String(cache.DefaultVernWs)?.Text?.Normalize(NormalizationForm.FormD);

    private static bool HasGloss(LcmCache cache, ILexEntry entry, IMoStemMsa msa, string text) =>
        entry.SensesOS.Any(sense => sense.MorphoSyntaxAnalysisRA == msa &&
            sense.Gloss.get_String(cache.DefaultAnalWs)?.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
}
