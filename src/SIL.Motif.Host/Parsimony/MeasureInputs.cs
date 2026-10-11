using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// What a measure's decision needs besides its facts: the bundle that evidence references name, the inputs of the
/// evidence digest, and a lookup for readable object names. Nothing here reaches back into the session.
/// </summary>
/// <param name="BundleId">The bundle every evidence reference of the measure's findings names.</param>
/// <param name="Digest">The report-level inputs folded into each finding's evidence digest.</param>
/// <param name="Describe">Returns a readable name for a grammar object, or null when none is known.</param>
internal sealed record MeasureContext(string BundleId, MeasureDigestContext Digest, Func<string, string?> Describe)
{
    /// <summary>A context whose names come from a fixed map, for deciding without a session.</summary>
    public static MeasureContext For(string bundleId, MeasureDigestContext digest,
        IReadOnlyDictionary<string, string>? names = null) =>
        new(bundleId, digest, guid => names is not null && names.TryGetValue(guid, out var name) ? name : null);
}

/// <summary>The report-level values a finding's evidence digest folds in beside the measure's own evidence.</summary>
/// <param name="FactsSchemaVersion">The schema version of the grammar facts artifact.</param>
/// <param name="EvidenceSchemaVersion">The schema version of the evidence artifact.</param>
/// <param name="Scope">The evidence scope the report was requested for.</param>
/// <param name="SelectionSha256">The digest of the default selection, when the scope is one.</param>
/// <param name="ExpectationRevisionSha256">The revision digest of the reviewed-negative expectations, when bound.</param>
internal sealed record MeasureDigestContext(int FactsSchemaVersion, int EvidenceSchemaVersion,
    ParsimonyEvidenceScopeKind Scope, string? SelectionSha256, string? ExpectationRevisionSha256)
{
    /// <summary>Takes the digest inputs from a report's frozen inputs.</summary>
    public static MeasureDigestContext From(ParsimonyReportInputs inputs) => new(inputs.GrammarFacts.SchemaVersion,
        inputs.Evidence.SchemaVersion, inputs.EvidenceScope, inputs.SelectionSha256,
        inputs.ExpectationRevisionSha256);
}

/// <summary>The enabled and loaded ad hoc prohibitions the duplicate measure compares, with the facts' provenance.</summary>
/// <param name="Prohibitions">Every ad hoc prohibition row, enabled or not.</param>
/// <param name="GroupedFactsAvailable">Whether the facts carry grouped ad hoc provenance.</param>
internal sealed record DuplicateProhibitionInput(IReadOnlyList<AdhocProhibitionFact> Prohibitions,
    bool GroupedFactsAvailable);

/// <summary>The unslotted inflectional affixes and the denominator of loaded inflectional affixes.</summary>
/// <param name="Rows">The unslotted affixes with their rooted Approved evidence.</param>
/// <param name="LoadedInflectionalAffixes">How many inflectional affixes the parser loaded.</param>
internal sealed record UnslottedAffixInput(IReadOnlyList<ParsimonyUnslottedAffixViewRow> Rows,
    long LoadedInflectionalAffixes);

/// <summary>The environment and natural-class statements, with the referrers of each one that is unused.</summary>
/// <param name="Statements">Every statement with its usage classification.</param>
/// <param name="Referrers">The referrers of each unused statement, keyed by statement kind and GUID.</param>
internal sealed record StatementUsageInput(IReadOnlyList<ParsimonyStatementUsageViewRow> Statements,
    IReadOnlyDictionary<(string StatementKind, string StatementGuid),
        IReadOnlyList<(string ReferrerKind, string ReferrerGuid, string ParserEffect)>> Referrers);

/// <summary>The ad hoc prohibitions, the template facts, and the compiler facts the slot-order measure checks.</summary>
/// <param name="Rules">Every ad hoc prohibition row.</param>
/// <param name="Facts">Templates, categories, MSA kinds, and Approved analyses.</param>
/// <param name="CompiledMorphRuleMsas">The MSAs the compiler mapped to a morphological rule.</param>
/// <param name="LoaderReasons">Why the loader excluded a prohibition, keyed by prohibition GUID.</param>
/// <param name="GroupedFactsAvailable">Whether the facts carry grouped ad hoc provenance.</param>
internal sealed record AdhocSlotOrderInput(IReadOnlyList<AdhocProhibitionFact> Rules,
    TemplateSequenceMeasureFacts Facts, IReadOnlySet<string> CompiledMorphRuleMsas,
    IReadOnlyDictionary<string, string> LoaderReasons, bool GroupedFactsAvailable);
