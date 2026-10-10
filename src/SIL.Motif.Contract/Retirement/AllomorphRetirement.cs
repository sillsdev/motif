using System.Collections.Generic;

namespace SIL.Motif.Contract.Retirement;

/// <summary>
/// Authored sound-rule replacement in one Proposal. Model assertions describe the expected Baseline;
/// they require an independent complete reference census and live validation before any write.
/// </summary>
public sealed record ReplaceListedAllomorphsWithRuleIntent(
    RetirementNaturalClass NaturalClass,
    RetirementRule Rule,
    IReadOnlyList<RetireAllomorphIntent> Retirements,
    IReadOnlyList<RetirementOperationBinding> Operations,
    RetirementDisplay? Display = null)
{
    public string Format { get; init; } = "motif-allomorph-retirement";
    public int Version { get; init; } = 2;
}

/// <summary>
/// A same-entry retirement component. Explicit bundle destinations are not a claim that the incoming
/// reference census is complete; unsupported references or a changed census block retirement.
/// </summary>
public sealed record RetireAllomorphIntent(
    string Entry,
    AllomorphRetirementScope Scope,
    IReadOnlyList<AllomorphIdentity> RetiredForms,
    IReadOnlyList<AllomorphRoleReplacement> RoleReplacements,
    IReadOnlyList<RetirementBundle> Bundles,
    IReadOnlyList<AdhocOccurrenceReplacement> AdhocReplacements);

/// <summary>A standalone duplicate-form retirement request, without rule authoring.</summary>
public sealed record RetireAllomorphIntentDocument(
    RetireAllomorphIntent Retirement,
    IReadOnlyList<AllomorphIdentity> DuplicateSurvivors)
{
    public string Format { get; init; } = "motif-retire-allomorph";
    public int Version { get; init; } = 1;
}

/// <summary>
/// Expected semantic identity, never a cache-local handle. StemName is explicit even when null;
/// environment and effective selection-gate meaning belongs in the Baseline semantic assertion.
/// </summary>
public sealed record AllomorphIdentity(
    string Id, string Entry, string Class, string Location, string Position, string SemanticDigest,
    string? StemName);

/// <summary>Authoring scope is separate from permission to execute the retirement.</summary>
public enum AllomorphRetirementScope
{
    Affix,
    Stem
}

/// <summary>
/// Exact role-qualified identity translation. InflType must be present even when null; whole is the
/// ordinary expansion role. Neither MSA, spelling nor entry alone selects a replacement.
/// </summary>
public sealed record AllomorphRoleReplacement(
    string RetiredForm, string Msa, string? InflType, string ExpansionRole, AllomorphIdentity Replacement);

/// <summary>An exact linked bundle occurrence; count/order and the owning analysis identity remain intact.</summary>
public sealed record RetirementBundle(
    string Bundle, string Analysis, string Wordform, string RetiredForm, string Msa, string? InflType,
    string ExpansionRole);

/// <summary>
/// One authored ad hoc destination. FirstAllomorph has a null ordinal; RestOfAllos and Allomorphs have
/// zero-based occurrence ordinals. A sequence has one final operation for all its changed occurrences.
/// </summary>
public sealed record AdhocOccurrenceReplacement(
    string Rule, string Field, int? Ordinal, string RetiredForm, AllomorphIdentity Replacement);

/// <summary>
/// An exact segment class to create or reuse. Reuse includes the expected semantic digest and exact
/// members; it never changes a shared class or infers its identity from an environment token.
/// </summary>
public sealed record RetirementNaturalClass(
    string Mode, string Id, string Name, string Abbreviation, IReadOnlyList<string> Members, string? SemanticDigest);

/// <summary>
/// Complete bounded segment rewrite and placement. Input/output contain authored phoneme identities;
/// context atoms are segment, natural-class or boundary identities, without alpha or process expansion.
/// </summary>
public sealed record RetirementRule(
    string Id, string Name, IReadOnlyList<string> Input, IReadOnlyList<string> Output,
    IReadOnlyList<RetirementContextAtom> Left, IReadOnlyList<RetirementContextAtom> Right,
    RetirementRulePlacement Placement, bool Enabled);

/// <summary>A typed context atom; sequence position here is phonological order, not mutation order.</summary>
public sealed record RetirementContextAtom(string Kind, string Id);

/// <summary>Explicit first/last placement, or before/after one exact existing rule anchor.</summary>
public sealed record RetirementRulePlacement(string Kind, string? Anchor);

/// <summary>
/// Binding to a concrete semantic operation id and closed slot. A binding is review/dependency evidence,
/// never an executable property script or Mutation Plan. Member is required only for alternate deletion.
/// </summary>
public sealed record RetirementOperationBinding(
    string OperationId, string Slot, string Target, string? Member, IReadOnlyList<string> DependsOn);

/// <summary>Optional review copy excluded from the authored retirement digest.</summary>
public sealed record RetirementDisplay(string Title, string Description);

/// <summary>Contract capability metadata; model mutation requires the complete operation-family gates.</summary>
public static class AllomorphRetirementCapabilities
{
    public const string Contract = "allomorph-retirement/v2";
    public const string FrozenExpectationContract = "frozen-expectations/v1";
    public const bool CanApply = false;
    public static IReadOnlyList<AllomorphRetirementScope> AuthoringScopes { get; } =
        System.Array.AsReadOnly(new[] { AllomorphRetirementScope.Affix, AllomorphRetirementScope.Stem });
    public static IReadOnlyList<string> RequiredCapabilities { get; } = System.Array.AsReadOnly(new[]
    {
        "complete-incoming-reference-census", "ordinary-bundle-form-effects", "exact-adhoc-denotation",
        "frozen-expectations", "proposal-scoped-role-translation", "atomic-owned-alternate-deletion",
        "complete-affected-reading-verification", "rule-attribution", "rebuilt-finding-resolution"
    });

    /// <summary>Required proofs, not satisfied capabilities or an execution allowlist.</summary>
    public static IReadOnlyList<string> RequiredCapabilitiesFor(AllomorphRetirementScope scope) => scope switch
    {
        AllomorphRetirementScope.Affix => RequiredCapabilities,
        AllomorphRetirementScope.Stem => StemRequiredCapabilities,
        _ => throw new System.ArgumentOutOfRangeException(nameof(scope))
    };

    private static IReadOnlyList<string> StemRequiredCapabilities { get; } = System.Array.AsReadOnly<string>(
    [
        .. RequiredCapabilities,
        "stem-selection-gate-equivalence", "stem-environment-rule-correspondence",
        "stem-expansion-context-census", "stem-bundle-form-effects", "frozen-stem-reading-verification"
    ]);
}
