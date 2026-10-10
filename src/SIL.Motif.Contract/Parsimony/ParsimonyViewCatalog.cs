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
        Define("affix-context", "object-guid", "evidence", "msas", "entries", "allomorphs", "templates"),
        Define("adhoc-context", "object-guid", "adhoc_prohibitions", "load_accounting"),
        Define("allomorph-context", "object-guid", "evidence", "allomorphs", "environments"),
        Define("alternation-families", "none", "allomorphs", "entries", "msas", "features", "phonology",
            "environments", "patterns"),
        Define("approved-morph-sequences", "object-guid?", "evidence", "categories", "templates", "msas",
            "entries", "allomorphs"),
        Define("environment-excess", "object-guid?", "evidence", "compiled_mappings", "environments",
            "allomorphs", "entries", "features", "msas", "patterns", "phonology"),
        Define("join-quality", "scope", "evidence"),
        Define("natural-class-excess", "object-guid?", "allomorphs", "compiled_mappings", "entries",
            "environments", "evidence", "features", "msas", "patterns", "phonology"),
        Define("natural-class-context", "object-guid", "features", "patterns", "environments", "load_accounting"),
        Define("parser-cases", "case-key?", "parser-overlay", "stats"),
        Define("parsimony-active-findings", "report-id,measure-id?,subject-key?,search?", "human-judgments", "report-findings"),
        Define("parsimony-suppressed", "report-id?,measure-id?,subject-key?,disposition?,search?", "human-judgments"),
        Define("parsimony-suppression-history", "report-id?,measure-id?,subject-key?,disposition?,state?,search?", "human-judgments"),
        Define("statement-usage", "statement-kind?,object-guid?", "statement-reference", "adhoc_prohibitions",
            "affix_processes", "allomorphs", "compound_rules", "entries", "environments", "load_accounting", "msas",
            "patterns", "phonology", "templates"),
        Define("slot-context", "object-guid", "categories", "templates", "msas"),
        Define("template-order", "template-guid|category-guid", "categories", "templates"),
        Define("unslotted-affixes", "object-guid?", "affix-slot-membership", "approved-signatures",
            "effective-objects", "rooted-morph"),
        Define("null-optional", "object-guid?", "affix-slot-membership", "feature-contributions",
            "incoming-references", "loaded-zero-realizations"),
    ]);

    private static readonly IReadOnlyDictionary<string, ParsimonyViewDefinition> ByCode =
        Definitions.ToDictionary(definition => definition.Code, StringComparer.Ordinal);

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
        "affix-context" => "Reads one MSA with its authored categories, slots, and Approved witnesses.",
        "adhoc-context" => "Reads one full ordered prohibition and its final known loader state.",
        "allomorph-context" => "Reads one allomorph's forms, environment conditions, and Approved witnesses.",
        "alternation-families" => "Reads repeated phoneme-level changes across independent morphemes and their limits.",
        "approved-morph-sequences" => "Reads the Approved words and their ordered recorded morphemes.",
        "environment-excess" => "Reads compiler-ordered one-neighbor contexts beside uniquely aligned Approved triggers.",
        "join-quality" => "Reports distinct project and evidence-scope denominators.",
        "natural-class-excess" => "Reads a natural class usage site's effective members, observed triggers, and feature intersection.",
        "natural-class-context" => "Reads one natural class's listed and effective members and references.",
        "parser-cases" => "Reads Assessment origins, case completion, ordered morphs, supported counters, and reviewed-negative matches.",
        "parsimony-active-findings" => "Lists current Report findings after exact applied judgments are accounted for.",
        "parsimony-suppressed" => "Lists captured keep and defer decisions with their current evidence state.",
        "parsimony-suppression-history" => "Lists every captured keep and defer revision and its Report binding.",
        "statement-usage" => "Lists each environment and natural class with its references and their parser effects.",
        "slot-context" => "Reads one slot with its inflectional users and template positions.",
        "template-order" => "Reads declared template slots and their order.",
        "unslotted-affixes" => "Reads loaded unslotted affixes with Approved positions and direct order evidence.",
        "null-optional" => "Reads authored zero realizations, compiler provenance, slot optionality, and references.",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };
}
