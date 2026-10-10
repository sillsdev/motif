using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>Closed metadata for the registered Parsimony review measures.</summary>
public static class MeasureCatalog
{
    /// <summary>The version of the registered measure set recorded in Parsimony Reports.</summary>
    public const int MeasureSetVersion = 1;

    private static readonly IReadOnlyList<MeasureDefinition> Definitions = Array.AsReadOnly(
    [
        DefineRegistered("P-adhoc-duplicate", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.AdhocProhibition,
            "adhoc-duplicate/v1", Threshold(), ["adhoc-context"],
            ["black-2018", "bender-poulson-drellishak-evans-2007"], "prohibitions",
            "adhoc-signature", "edit-adhoc-disabled", "grouped-adhoc-load", "load-facts", "ordered-adhoc-targets",
            "paired-verification"),
        DefineGroupRegistered("P-allo-duplicate-form", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
            ParsimonyGroupKind.AllomorphDuplicateForms,
            "allomorph-duplicate-form/v1", Threshold(), ["allomorph-context"], ["black-2018"], "allomorphs",
            "allomorph-context", "allomorph-environments", "allomorph-forms", "entry-identities",
            "morph-type", "retire-allomorph-intent"),
        DefineGroupRegistered("P-statement-unused", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
            ParsimonyGroupKind.UnusedStatement,
            "statement-unused/v1", Threshold(), ["statement-usage"],
            ["bender-poulson-drellishak-evans-2007"], "statements",
            "statement-load-facts", "statement-reference", "retire-statement"),
        DefineRegistered("R-word-negative-accepted", ParsimonyAxis.Restrictiveness, ParsimonyTier.Parser,
            ParsimonyAttachmentKind.WordCase, null, "negative-accepted/v1", Threshold(), ["parser-cases"],
            ["gold-1967", "bender-poulson-drellishak-evans-2007", "prince-tesar-2004"],
            "reviewed negative cases", "edit-affix-template", "parser-cases", "parser-overlay",
            "reviewed-negative-expectations"),
        DefineRegistered("R-word-disapproved-produced", ParsimonyAxis.Restrictiveness, ParsimonyTier.Parser,
            ParsimonyAttachmentKind.WordCase, null, "disapproved-produced/v1", Threshold(), ["parser-cases"],
            ["bender-poulson-drellishak-evans-2007", "kol-nir-wintner-2014"], "disapproved morphologies",
            "approved-signatures", "case-outcomes", "disapproved-opinions", "edit-allomorph-condition",
            "parser-overlay", "parser-signatures"),
        DefineRegistered("B-affix-unslotted", ParsimonyAxis.Both, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.InflectionalMsa,
            "unslotted-affix/v1", Threshold(),
            ["unslotted-affixes", "affix-context", "approved-morph-sequences"],
            ["black-2018", "stump-1993", "durrett-deniro-2013", "kibrik-2005", "ahlberg-forsberg-hulden-2015"],
            "unslotted inflectional MSAs", "affix-slot-membership", "approved-signatures", "author-affix-slot",
            "author-affix-template", "edit-inflectional-affix", "effective-objects", "rooted-morph"),
        DefineRegistered("R-tmpl-precedence", ParsimonyAxis.Restrictiveness, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.AffixTemplate,
            "template-precedence/v1", Threshold(), ["template-order", "approved-morph-sequences", "affix-context"],
            ["black-2018", "hyman-2003", "hyman-mchombo-1992", "stump-1993", "ryan-2010"],
            "approved-analysis-cases", "approved-signatures", "author-affix-template", "edit-affix-template",
            "effective-objects", "template-embeddings"),
        DefineRegistered("R-slot-blocking", ParsimonyAxis.Restrictiveness, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.AffixSlot,
            "slot-blocking/v1", Threshold(), ["slot-context", "template-order", "approved-morph-sequences"],
            ["black-2018", "bender-poulson-drellishak-evans-2007"], "approved-analysis-cases",
            "approved-signatures", "edit-affix-slot", "slot-users", "template-embeddings", "zero-morphology"),
        DefineRegistered("R-allo-unconditioned", ParsimonyAxis.Restrictiveness, ParsimonyTier.Static,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.Allomorph,
            "allo-unconditioned/v1", Threshold(), ["allomorph-context"],
            ["black-2018", "bender-poulson-drellishak-evans-2007"], "allomorphs",
            "allomorph-environments", "edit-allomorph-condition", "effective-allomorph-order",
            "effective-context-matches", "effective-objects", "order-allomorphs"),
        DefineRegistered("R-env-broad", ParsimonyAxis.Restrictiveness, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.Allomorph,
            "environment-excess/v1", Threshold(),
            ["environment-excess", "allomorph-context", "natural-class-context", "approved-morph-sequences"],
            ["prince-tesar-2004", "tenenbaum-griffiths-2001", "gold-1967", "wexler-1993", "wexler-manzini-1987"],
            "environment-sites", "author-environment", "context-universe", "edit-allomorph-condition",
            "effective-context-matches", "observed-context"),
        DefineRegistered("R-nc-excess", ParsimonyAxis.Restrictiveness, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.NaturalClass,
            "natural-class-excess/v1", Threshold(),
            ["natural-class-excess", "natural-class-context", "approved-morph-sequences"],
            ["chomsky-halle-1968", "albright-hayes-2003", "gold-1967"], "class-usage-sites",
            "author-natural-class", "class-extension", "edit-natural-class", "feature-assignments",
            "observed-context", "relink-natural-class"),
        DefineGroupRegistered("P-allo-alternation-family", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
            ParsimonyGroupKind.AlternationFamily,
            "allo-alternation-family/v1", AtLeastTwoMorphemes(), ["alternation-families", "allomorph-context"],
            ["chomsky-halle-1968", "albright-hayes-2003", "ellison-1994", "lan-rasin-katzir-2019",
                "rasin-berger-lan-katzir-2018"],
            "morphemes",
            "approved-signatures", "author-phonological-rule", "feature-assignments", "observed-context",
            "phoneme-tokenization", "retire-allomorph-intent"),
        DefineGroupRegistered("B-adhoc-is-slot-order", ParsimonyAxis.Both, ParsimonyTier.Text,
            ParsimonyGroupKind.AdhocSlotOrder,
            "adhoc-slot-order/v1", Threshold(),
            ["adhoc-context", "approved-morph-sequences", "template-order"],
            ["black-2018", "ryan-2010", "stump-1993", "bender-poulson-drellishak-evans-2007"],
            "prohibitions",
            "author-affix-template", "edit-affix-template", "edit-adhoc-disabled", "ordered-adhoc-targets",
            "paired-verification", "template-embeddings"),
        DefineRegistered("B-affix-null-vs-optional", ParsimonyAxis.Both, ParsimonyTier.Static,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.InflectionalMsa,
            "null-optional/v1", Threshold(),
            ["null-optional", "affix-context", "allomorph-context", "approved-morph-sequences", "slot-context"],
            ["black-2018", "becker-2024", "alekseeva-myachykov-shtyrov-2022"],
            "authored loaded zero-only MSAs", "affix-slot-membership", "approved-signatures",
            "feature-contributions", "incoming-references",
            "loaded-zero-realizations", "retire-redundant-zero-affix-intent"),
    ]);

    private static readonly IReadOnlyDictionary<string, MeasureDefinition> ById =
        Definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);

    /// <summary>All supported measures in stable measure-ID order.</summary>
    public static IReadOnlyList<MeasureDefinition> All { get; } =
        Array.AsReadOnly(Definitions.OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray());

    /// <summary>Finds a measure by its exact stable measure ID.</summary>
    /// <param name="id">The exact measure ID.</param>
    public static MeasureDefinition? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>Returns required capability names absent from the caller's supported set.</summary>
    /// <param name="measureId">The exact measure ID.</param>
    /// <param name="supportedCapabilities">Capabilities available to the current execution.</param>
    public static IReadOnlyList<string> MissingCapabilities(
        string measureId,
        IEnumerable<string> supportedCapabilities)
    {
        ArgumentNullException.ThrowIfNull(supportedCapabilities);
        var measure = Find(measureId)
            ?? throw new ArgumentException($"Unknown measure ID '{measureId}'.", nameof(measureId));
        var supported = new HashSet<string>(supportedCapabilities, StringComparer.Ordinal);
        return Array.AsReadOnly(measure.RequiredCapabilities
            .Where(capability => !supported.Contains(capability))
            .ToArray());
    }

    private static MeasureDefinition Define(
        string id,
        ParsimonyAxis axis,
        ParsimonyTier tier,
        ParsimonyAttachmentKind attachmentKind,
        ParsimonyAuthoredObjectKind? authoredObjectKind,
        string[] citationKeys,
        params string[] requiredCapabilities) =>
        DefineCore(id, axis, tier, attachmentKind, authoredObjectKind, null, null, [], citationKeys, "items",
            requiredCapabilities);

    private static MeasureDefinition DefineRegistered(
        string id,
        ParsimonyAxis axis,
        ParsimonyTier tier,
        ParsimonyAttachmentKind attachmentKind,
        ParsimonyAuthoredObjectKind? authoredObjectKind,
        string queryId,
        ParsimonyMeasureThreshold threshold,
        string[] evidenceViews,
        string[] citationKeys,
        string unit,
        params string[] requiredCapabilities) =>
        DefineCore(id, axis, tier, attachmentKind, authoredObjectKind, queryId, threshold, evidenceViews,
            citationKeys, unit, requiredCapabilities);

    private static MeasureDefinition DefineCore(
        string id,
        ParsimonyAxis axis,
        ParsimonyTier tier,
        ParsimonyAttachmentKind attachmentKind,
        ParsimonyAuthoredObjectKind? authoredObjectKind,
        string? queryId,
        ParsimonyMeasureThreshold? threshold,
        string[] evidenceViews,
        string[] citationKeys,
        string unit,
        string[] requiredCapabilities) =>
        new(
            id,
            axis,
            tier,
            attachmentKind,
            authoredObjectKind,
            null,
            Array.AsReadOnly(requiredCapabilities.Order(StringComparer.Ordinal).ToArray()),
            $"guide:parsimony/recipes/{id}",
            queryId,
            threshold,
            Array.AsReadOnly(evidenceViews.Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(citationKeys.ToArray()),
            unit);

    private static MeasureDefinition DefineGroup(
        string id,
        ParsimonyAxis axis,
        ParsimonyTier tier,
        ParsimonyGroupKind groupKind,
        string[] citationKeys,
        params string[] requiredCapabilities) =>
        DefineGroupCore(id, axis, tier, groupKind, null, null, [], citationKeys, "items", requiredCapabilities);

    private static MeasureDefinition DefineGroupRegistered(
        string id,
        ParsimonyAxis axis,
        ParsimonyTier tier,
        ParsimonyGroupKind groupKind,
        string queryId,
        ParsimonyMeasureThreshold threshold,
        string[] evidenceViews,
        string[] citationKeys,
        string unit,
        params string[] requiredCapabilities) =>
        DefineGroupCore(id, axis, tier, groupKind, queryId, threshold, evidenceViews, citationKeys, unit,
            requiredCapabilities);

    private static MeasureDefinition DefineGroupCore(
        string id,
        ParsimonyAxis axis,
        ParsimonyTier tier,
        ParsimonyGroupKind groupKind,
        string? queryId,
        ParsimonyMeasureThreshold? threshold,
        string[] evidenceViews,
        string[] citationKeys,
        string unit,
        string[] requiredCapabilities) =>
        new(
            id,
            axis,
            tier,
            ParsimonyAttachmentKind.Group,
            null,
            groupKind,
            Array.AsReadOnly(requiredCapabilities.Order(StringComparer.Ordinal).ToArray()),
            $"guide:parsimony/recipes/{id}",
            queryId,
            threshold,
            Array.AsReadOnly(evidenceViews.Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(citationKeys.ToArray()),
            unit);

    private static ParsimonyMeasureThreshold Threshold() =>
        new(ParsimonyThresholdOperator.GreaterThan, 0, "1");

    private static ParsimonyMeasureThreshold AtLeastTwoMorphemes() =>
        new(ParsimonyThresholdOperator.GreaterThanOrEqual, 2, "1");
}

/// <summary>One fixed measure definition and its evidence, identity, capability, and recipe contracts.</summary>
/// <param name="Id">The stable measure ID.</param>
/// <param name="Axis">The axis examined by the measure.</param>
/// <param name="Tier">The strongest evidence tier required for its recommendation.</param>
/// <param name="AttachesTo">The closed category of identity named by its findings.</param>
/// <param name="AuthoredObjectKind">The authored object type when <paramref name="AttachesTo"/> is authored-object.</param>
/// <param name="GroupKind">The group type when <paramref name="AttachesTo"/> is group.</param>
/// <param name="RequiredCapabilities">The named evidence and update capabilities needed by its recipe.</param>
/// <param name="RecipeLink">The stable Guide code shared with Help and findings.</param>
/// <param name="QueryId">The registered fixed query and version, when this build can execute the measure.</param>
/// <param name="Threshold">The typed versioned numeric trigger, when one is declared.</param>
/// <param name="EvidenceViews">The named evidence views used by the fixed query.</param>
/// <param name="CitationKeys">The closed keys that resolve to the recipe's Grounding citations.</param>
/// <param name="Unit">The counted item kind in full-eligibility output.</param>
public sealed record MeasureDefinition(
    string Id,
    ParsimonyAxis Axis,
    ParsimonyTier Tier,
    ParsimonyAttachmentKind AttachesTo,
    ParsimonyAuthoredObjectKind? AuthoredObjectKind,
    ParsimonyGroupKind? GroupKind,
    IReadOnlyList<string> RequiredCapabilities,
    string RecipeLink,
    string? QueryId,
    ParsimonyMeasureThreshold? Threshold,
    IReadOnlyList<string> EvidenceViews,
    IReadOnlyList<string> CitationKeys,
    string Unit);
