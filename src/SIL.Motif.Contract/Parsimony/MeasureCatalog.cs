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
            ParsimonyQueryIds.AdhocDuplicate, Threshold(), [ParsimonyViews.AdhocContext], ["black-2018",
            "bender-poulson-drellishak-evans-2007"], "prohibitions", ParsimonyCapabilities.AdhocSignature,
            ParsimonyCapabilities.EditAdhocDisabled, ParsimonyCapabilities.GroupedAdhocLoad,
            ParsimonyCapabilities.LoadFacts, ParsimonyCapabilities.OrderedAdhocTargets,
            ParsimonyCapabilities.PairedVerification),
        DefineGroupRegistered("P-allo-duplicate-form", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
            ParsimonyGroupKind.AllomorphDuplicateForms, ParsimonyQueryIds.AllomorphDuplicateForm, Threshold(),
            [ParsimonyViews.AllomorphContext], ["black-2018"], "allomorphs",
            ParsimonyCapabilities.AllomorphContext, ParsimonyCapabilities.AllomorphEnvironments,
            ParsimonyCapabilities.AllomorphForms, ParsimonyCapabilities.EntryIdentities,
            ParsimonyCapabilities.MorphType, ParsimonyCapabilities.RetireAllomorphIntent),
        DefineGroupRegistered("P-statement-unused", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
            ParsimonyGroupKind.UnusedStatement, ParsimonyQueryIds.StatementUnused, Threshold(),
            [ParsimonyViews.StatementUsage], ["bender-poulson-drellishak-evans-2007"], "statements",
            ParsimonyCapabilities.StatementLoadFacts, ParsimonyCapabilities.StatementReference,
            ParsimonyCapabilities.RetireStatement),
        DefineRegistered("R-word-negative-accepted", ParsimonyAxis.Restrictiveness, ParsimonyTier.Parser,
            ParsimonyAttachmentKind.WordCase, null, ParsimonyQueryIds.NegativeAccepted, Threshold(),
            [ParsimonyViews.ParserCases], ["gold-1967", "bender-poulson-drellishak-evans-2007",
            "prince-tesar-2004"], "reviewed negative cases", ParsimonyCapabilities.EditAffixTemplate,
            ParsimonyCapabilities.ParserCases, ParsimonyCapabilities.ParserOverlay,
            ParsimonyCapabilities.ReviewedNegativeExpectations),
        DefineRegistered("R-word-disapproved-produced", ParsimonyAxis.Restrictiveness, ParsimonyTier.Parser,
            ParsimonyAttachmentKind.WordCase, null, ParsimonyQueryIds.DisapprovedProduced, Threshold(),
            [ParsimonyViews.ParserCases], ["bender-poulson-drellishak-evans-2007", "kol-nir-wintner-2014"],
            "disapproved morphologies", ParsimonyCapabilities.ApprovedSignatures,
            ParsimonyCapabilities.CaseOutcomes, ParsimonyCapabilities.DisapprovedOpinions,
            ParsimonyCapabilities.EditAllomorphCondition, ParsimonyCapabilities.ParserOverlay,
            ParsimonyCapabilities.ParserSignatures),
        DefineRegistered("B-affix-unslotted", ParsimonyAxis.Both, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.InflectionalMsa,
            ParsimonyQueryIds.UnslottedAffix, Threshold(), [ParsimonyViews.UnslottedAffixes,
            ParsimonyViews.AffixContext, ParsimonyViews.ApprovedMorphSequences], ["black-2018", "stump-1993",
            "durrett-deniro-2013", "kibrik-2005", "ahlberg-forsberg-hulden-2015"],
            "unslotted inflectional MSAs", ParsimonyCapabilities.AffixSlotMembership,
            ParsimonyCapabilities.ApprovedSignatures, ParsimonyCapabilities.AuthorAffixSlot, ParsimonyCapabilities.AuthorAffixTemplate,
            ParsimonyCapabilities.EditInflectionalAffix, ParsimonyCapabilities.EffectiveObjects,
            ParsimonyCapabilities.RootedMorph),
        DefineRegistered("R-tmpl-precedence", ParsimonyAxis.Restrictiveness, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.AffixTemplate,
            ParsimonyQueryIds.TemplatePrecedence, Threshold(), [ParsimonyViews.TemplateOrder,
            ParsimonyViews.ApprovedMorphSequences, ParsimonyViews.AffixContext], ["black-2018", "hyman-2003",
            "hyman-mchombo-1992", "stump-1993", "ryan-2010"], "approved-analysis-cases",
            ParsimonyCapabilities.ApprovedSignatures, ParsimonyCapabilities.AuthorAffixTemplate,
            ParsimonyCapabilities.EditAffixTemplate, ParsimonyCapabilities.EffectiveObjects,
            ParsimonyCapabilities.TemplateEmbeddings),
        DefineRegistered("R-slot-blocking", ParsimonyAxis.Restrictiveness, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.AffixSlot,
            ParsimonyQueryIds.SlotBlocking, Threshold(), [ParsimonyViews.SlotContext,
            ParsimonyViews.TemplateOrder, ParsimonyViews.ApprovedMorphSequences], ["black-2018",
            "bender-poulson-drellishak-evans-2007"], "approved-analysis-cases",
            ParsimonyCapabilities.ApprovedSignatures, ParsimonyCapabilities.EditAffixSlot,
            ParsimonyCapabilities.SlotUsers, ParsimonyCapabilities.TemplateEmbeddings,
            ParsimonyCapabilities.ZeroMorphology),
        DefineRegistered("R-allo-unconditioned", ParsimonyAxis.Restrictiveness, ParsimonyTier.Static,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.Allomorph,
            ParsimonyQueryIds.AlloUnconditioned, Threshold(), [ParsimonyViews.AllomorphContext], ["black-2018",
            "bender-poulson-drellishak-evans-2007"], "allomorphs", ParsimonyCapabilities.AllomorphEnvironments,
            ParsimonyCapabilities.EditAllomorphCondition, ParsimonyCapabilities.EffectiveAllomorphOrder,
            ParsimonyCapabilities.EffectiveContextMatches, ParsimonyCapabilities.EffectiveObjects,
            ParsimonyCapabilities.OrderAllomorphs),
        DefineRegistered("R-env-broad", ParsimonyAxis.Restrictiveness, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.Allomorph,
            ParsimonyQueryIds.EnvironmentExcess, Threshold(), [ParsimonyViews.EnvironmentExcess,
            ParsimonyViews.AllomorphContext, ParsimonyViews.NaturalClassContext,
            ParsimonyViews.ApprovedMorphSequences], ["prince-tesar-2004", "tenenbaum-griffiths-2001",
            "gold-1967", "wexler-1993", "wexler-manzini-1987"], "environment-sites",
            ParsimonyCapabilities.AuthorEnvironment, ParsimonyCapabilities.ContextUniverse,
            ParsimonyCapabilities.EditAllomorphCondition, ParsimonyCapabilities.EffectiveContextMatches,
            ParsimonyCapabilities.ObservedContext),
        DefineRegistered("R-nc-excess", ParsimonyAxis.Restrictiveness, ParsimonyTier.Text,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.NaturalClass,
            ParsimonyQueryIds.NaturalClassExcess, Threshold(), [ParsimonyViews.NaturalClassExcess,
            ParsimonyViews.NaturalClassContext, ParsimonyViews.ApprovedMorphSequences], ["chomsky-halle-1968",
            "albright-hayes-2003", "gold-1967"], "class-usage-sites", ParsimonyCapabilities.AuthorNaturalClass,
            ParsimonyCapabilities.ClassExtension, ParsimonyCapabilities.EditNaturalClass,
            ParsimonyCapabilities.FeatureAssignments, ParsimonyCapabilities.ObservedContext,
            ParsimonyCapabilities.RelinkNaturalClass),
        DefineGroupRegistered("P-allo-alternation-family", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
            ParsimonyGroupKind.AlternationFamily, ParsimonyQueryIds.AlloAlternationFamily,
            AtLeastTwoMorphemes(), [ParsimonyViews.AlternationFamilies, ParsimonyViews.AllomorphContext],
            ["chomsky-halle-1968", "albright-hayes-2003", "ellison-1994", "lan-rasin-katzir-2019",
            "rasin-berger-lan-katzir-2018"], "morphemes", ParsimonyCapabilities.ApprovedSignatures,
            ParsimonyCapabilities.AuthorPhonologicalRule, ParsimonyCapabilities.FeatureAssignments,
            ParsimonyCapabilities.ObservedContext, ParsimonyCapabilities.PhonemeTokenization,
            ParsimonyCapabilities.RetireAllomorphIntent),
        DefineGroupRegistered("B-adhoc-is-slot-order", ParsimonyAxis.Both, ParsimonyTier.Text,
            ParsimonyGroupKind.AdhocSlotOrder, ParsimonyQueryIds.AdhocSlotOrder, Threshold(),
            [ParsimonyViews.AdhocContext, ParsimonyViews.ApprovedMorphSequences, ParsimonyViews.TemplateOrder],
            ["black-2018", "ryan-2010", "stump-1993", "bender-poulson-drellishak-evans-2007"], "prohibitions",
            ParsimonyCapabilities.AuthorAffixTemplate, ParsimonyCapabilities.EditAffixTemplate,
            ParsimonyCapabilities.EditAdhocDisabled, ParsimonyCapabilities.OrderedAdhocTargets,
            ParsimonyCapabilities.PairedVerification, ParsimonyCapabilities.TemplateEmbeddings),
        DefineRegistered("B-affix-null-vs-optional", ParsimonyAxis.Both, ParsimonyTier.Static,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.InflectionalMsa,
            ParsimonyQueryIds.NullOptional, Threshold(), [ParsimonyViews.NullOptional,
            ParsimonyViews.AffixContext, ParsimonyViews.AllomorphContext, ParsimonyViews.ApprovedMorphSequences,
            ParsimonyViews.SlotContext], ["black-2018", "becker-2024", "alekseeva-myachykov-shtyrov-2022"],
            "authored loaded zero-only MSAs", ParsimonyCapabilities.AffixSlotMembership,
            ParsimonyCapabilities.ApprovedSignatures, ParsimonyCapabilities.FeatureContributions,
            ParsimonyCapabilities.IncomingReferences, ParsimonyCapabilities.LoadedZeroRealizations,
            ParsimonyCapabilities.RetireRedundantZeroAffixIntent),
    ]);

    private static readonly IReadOnlyDictionary<string, MeasureDefinition> ById =
        Definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);

    static MeasureCatalog() => Validate(Definitions);

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

    // A typo in a capability, view or query id fails the first use of the catalog instead of shipping.
    private static void Validate(IReadOnlyList<MeasureDefinition> definitions)
    {
        foreach (var measure in definitions)
        {
            foreach (var capability in measure.RequiredCapabilities.Where(name => !ParsimonyCapabilities.All.Contains(name)))
                throw new InvalidOperationException($"Measure '{measure.Id}' requires unknown capability '{capability}'.");
            foreach (var view in measure.EvidenceViews.Where(name => ParsimonyViewCatalog.Find(name) is null))
                throw new InvalidOperationException($"Measure '{measure.Id}' names unknown evidence view '{view}'.");
            if (measure.QueryId is { } queryId && !ParsimonyQueryIds.All.Contains(queryId))
                throw new InvalidOperationException($"Measure '{measure.Id}' names unknown query id '{queryId}'.");
        }

        var duplicate = definitions.Where(measure => measure.QueryId is not null)
            .GroupBy(measure => measure.QueryId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Query id '{duplicate.Key}' is registered by more than one measure.");
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
            $"guide:parsimony/recipes/{id.ToLowerInvariant()}",
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
            $"guide:parsimony/recipes/{id.ToLowerInvariant()}",
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
