using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>Closed metadata for one public fixed Parsimony view.</summary>
public sealed record ParsimonyViewDefinition(
    string Code,
    int Version,
    string Description,
    IReadOnlyList<string> RequiredCapabilities,
    string FilterShape);

/// <summary>Lists the documented, fixed Parsimony views available to callers.</summary>
public static class ParsimonyViewCatalog
{
    private static readonly IReadOnlyList<ParsimonyViewDefinition> Definitions = Array.AsReadOnly(
    [
        Define(ParsimonyViews.AffixContext, "object-guid", ParsimonyCapabilities.Evidence,
            ParsimonyCapabilities.Msas, ParsimonyCapabilities.Entries, ParsimonyCapabilities.Allomorphs,
            ParsimonyCapabilities.Templates),
        Define(ParsimonyViews.AdhocContext, "object-guid", ParsimonyCapabilities.AdhocProhibitions,
            ParsimonyCapabilities.LoadAccounting),
        Define(ParsimonyViews.AllomorphContext, "object-guid", ParsimonyCapabilities.Evidence,
            ParsimonyCapabilities.Allomorphs, ParsimonyCapabilities.Environments),
        Define(ParsimonyViews.AlternationFamilies, "none", ParsimonyCapabilities.Allomorphs,
            ParsimonyCapabilities.Entries, ParsimonyCapabilities.Msas, ParsimonyCapabilities.Features,
            ParsimonyCapabilities.Phonology, ParsimonyCapabilities.Environments,
            ParsimonyCapabilities.Patterns),
        Define(ParsimonyViews.ApprovedMorphSequences, "object-guid?", ParsimonyCapabilities.Evidence,
            ParsimonyCapabilities.Categories, ParsimonyCapabilities.Templates, ParsimonyCapabilities.Msas,
            ParsimonyCapabilities.Entries, ParsimonyCapabilities.Allomorphs),
        Define(ParsimonyViews.EnvironmentExcess, "object-guid?", ParsimonyCapabilities.Evidence,
            ParsimonyCapabilities.CompiledMappings, ParsimonyCapabilities.Environments,
            ParsimonyCapabilities.Allomorphs, ParsimonyCapabilities.Entries, ParsimonyCapabilities.Features,
            ParsimonyCapabilities.Msas, ParsimonyCapabilities.Patterns, ParsimonyCapabilities.Phonology),
        Define(ParsimonyViews.JoinQuality, "scope", ParsimonyCapabilities.Evidence),
        Define(ParsimonyViews.NaturalClassExcess, "object-guid?", ParsimonyCapabilities.Allomorphs,
            ParsimonyCapabilities.CompiledMappings, ParsimonyCapabilities.Entries,
            ParsimonyCapabilities.Environments, ParsimonyCapabilities.Evidence, ParsimonyCapabilities.Features,
            ParsimonyCapabilities.Msas, ParsimonyCapabilities.Patterns, ParsimonyCapabilities.Phonology),
        Define(ParsimonyViews.NaturalClassContext, "object-guid", ParsimonyCapabilities.Features,
            ParsimonyCapabilities.Patterns, ParsimonyCapabilities.Environments,
            ParsimonyCapabilities.LoadAccounting),
        Define(ParsimonyViews.ParserCases, "case-key?", ParsimonyCapabilities.ParserOverlay,
            ParsimonyCapabilities.Stats),
        Define(ParsimonyViews.ParsimonyActiveFindings, "report-id,measure-id?,subject-key?,search?",
            ParsimonyCapabilities.HumanJudgments, ParsimonyCapabilities.ReportFindings),
        Define(ParsimonyViews.ParsimonySuppressed, "report-id?,measure-id?,subject-key?,disposition?,search?",
            ParsimonyCapabilities.HumanJudgments),
        Define(ParsimonyViews.ParsimonySuppressionHistory,
            "report-id?,measure-id?,subject-key?,disposition?,state?,search?",
            ParsimonyCapabilities.HumanJudgments),
        Define(ParsimonyViews.StatementUsage, "statement-kind?,object-guid?",
            ParsimonyCapabilities.StatementReference, ParsimonyCapabilities.AdhocProhibitions,
            ParsimonyCapabilities.AffixProcesses, ParsimonyCapabilities.Allomorphs,
            ParsimonyCapabilities.CompoundRules, ParsimonyCapabilities.Entries,
            ParsimonyCapabilities.Environments, ParsimonyCapabilities.LoadAccounting,
            ParsimonyCapabilities.Msas, ParsimonyCapabilities.Patterns, ParsimonyCapabilities.Phonology,
            ParsimonyCapabilities.Templates),
        Define(ParsimonyViews.SlotContext, "object-guid", ParsimonyCapabilities.Categories,
            ParsimonyCapabilities.Templates, ParsimonyCapabilities.Msas),
        Define(ParsimonyViews.TemplateOrder, "template-guid|category-guid", ParsimonyCapabilities.Categories,
            ParsimonyCapabilities.Templates),
        Define(ParsimonyViews.UnslottedAffixes, "object-guid?", ParsimonyCapabilities.AffixSlotMembership,
            ParsimonyCapabilities.ApprovedSignatures, ParsimonyCapabilities.EffectiveObjects,
            ParsimonyCapabilities.RootedMorph),
        Define(ParsimonyViews.NullOptional, "object-guid?", ParsimonyCapabilities.AffixSlotMembership,
            ParsimonyCapabilities.FeatureContributions, ParsimonyCapabilities.IncomingReferences,
            ParsimonyCapabilities.LoadedZeroRealizations),
    ]);

    private static readonly IReadOnlyDictionary<string, ParsimonyViewDefinition> ByCode =
        Definitions.ToDictionary(definition => definition.Code, StringComparer.Ordinal);

    static ParsimonyViewCatalog()
    {
        if (!ParsimonyViews.All.SetEquals(ByCode.Keys))
            throw new InvalidOperationException("The view catalog and the closed view codes differ.");
        foreach (var definition in Definitions)
            foreach (var capability in definition.RequiredCapabilities.Where(name => !ParsimonyCapabilities.All.Contains(name)))
                throw new InvalidOperationException($"View '{definition.Code}' requires unknown capability '{capability}'.");
    }

    /// <summary>All fixed views in stable code order.</summary>
    public static IReadOnlyList<ParsimonyViewDefinition> All { get; } =
        Array.AsReadOnly(Definitions.OrderBy(definition => definition.Code, StringComparer.Ordinal).ToArray());

    /// <summary>Finds a fixed view by its exact public code.</summary>
    public static ParsimonyViewDefinition? Find(string code) => ByCode.GetValueOrDefault(code);

    private static ParsimonyViewDefinition Define(string code, string filterShape, params string[] capabilities) =>
        new(code, 1, Description(code), Array.AsReadOnly(capabilities.Order(StringComparer.Ordinal).ToArray()),
            filterShape);

    private static string Description(string code) => code switch
    {
        ParsimonyViews.AffixContext => "Reads one MSA with its authored categories, slots, and Approved witnesses.",
        ParsimonyViews.AdhocContext => "Reads one full ordered prohibition and its final known loader state.",
        ParsimonyViews.AllomorphContext => "Reads one allomorph's forms, environment conditions, and Approved witnesses.",
        ParsimonyViews.AlternationFamilies => "Reads repeated phoneme-level changes across independent morphemes and their limits.",
        ParsimonyViews.ApprovedMorphSequences => "Reads the Approved words and their ordered recorded morphemes.",
        ParsimonyViews.EnvironmentExcess => "Reads compiler-ordered one-neighbor contexts beside uniquely aligned Approved triggers.",
        ParsimonyViews.JoinQuality => "Reports distinct project and evidence-scope denominators.",
        ParsimonyViews.NaturalClassExcess => "Reads a natural class usage site's effective members, observed triggers, and feature intersection.",
        ParsimonyViews.NaturalClassContext => "Reads one natural class's listed and effective members and references.",
        ParsimonyViews.ParserCases => "Reads Assessment origins, case completion, ordered morphs, supported counters, and reviewed-negative matches.",
        ParsimonyViews.ParsimonyActiveFindings => "Lists current Report findings after exact applied judgments are accounted for.",
        ParsimonyViews.ParsimonySuppressed => "Lists captured keep and defer decisions with their current evidence state.",
        ParsimonyViews.ParsimonySuppressionHistory => "Lists every captured keep and defer revision and its Report binding.",
        ParsimonyViews.StatementUsage => "Lists each environment and natural class with its references and their parser effects.",
        ParsimonyViews.SlotContext => "Reads one slot with its inflectional users and template positions.",
        ParsimonyViews.TemplateOrder => "Reads declared template slots and their order.",
        ParsimonyViews.UnslottedAffixes => "Reads loaded unslotted affixes with Approved positions and direct order evidence.",
        ParsimonyViews.NullOptional => "Reads authored zero realizations, compiler provenance, slot optionality, and references.",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };
}
