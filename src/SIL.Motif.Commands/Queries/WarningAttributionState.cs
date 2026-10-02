using SIL.Motif.Contract.Responses;
using ContractAttributionState = SIL.Motif.Contract.Responses.WarningAttributionState;

namespace SIL.Motif.Commands.Queries;

/// <summary>How a grammar finding's named item relates to words in the current Selection.</summary>
public enum WarningDisplayState
{
    /// <summary>Stored analyses or rule calls match a named item by identity.</summary>
    ExactUses,

    /// <summary>Words use members of a named resource without confirming selection of that resource.</summary>
    MembershipCandidates,

    /// <summary>Word spellings match the named letters, without confirming phoneme use.</summary>
    SpellingCandidates,

    /// <summary>A supported route finds no matching word in the current Selection.</summary>
    NoneInSelection,

    /// <summary>No followed route matched and another named connection could not be followed.</summary>
    NoFollowedRouteMatch,

    /// <summary>PanGloss supplied no subject for the finding.</summary>
    NoSubject,

    /// <summary>A named object has an identity, but Motif has no word route for its kind.</summary>
    NamedUnsupportedRoute,

    /// <summary>A project resource has no word attribution.</summary>
    ProjectWide,

    /// <summary>The named item is absent from the project checked by PanGloss.</summary>
    MissingObject,

    /// <summary>A subject is present, but its identity or word reach cannot be established.</summary>
    UnresolvedIdentity,

    /// <summary>A supported route exists, but current word evidence is unavailable.</summary>
    EvidenceUnavailable,
}

/// <summary>Projects PanGloss's canonical attribution evidence to the Warnings page's display states.</summary>
public static class WarningAttribution
{
    /// <summary>Whether any named subject or owner explicitly lacks a word route.</summary>
    public static bool HasUnfollowedConnections(GrammarWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return warning.AttributionLimits.Count > 0;
    }

    /// <summary>Returns the strongest word-attribution state supported by the finding's response fields.</summary>
    public static WarningDisplayState From(GrammarWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        var limits = HasUnfollowedConnections(warning);
        var state = warning.AttributionState;

        if (state == ContractAttributionState.UnresolvedIdentity &&
            warning.AttributionReason == WarningAttributionReason.NoSubject &&
            !warning.Subject.Any(IsObjectSubject))
            return WarningDisplayState.NoSubject;

        if (state == ContractAttributionState.UnresolvedIdentity &&
            warning.AttributionReason == WarningAttributionReason.UnsupportedKind)
            return warning.Subject.Any(IsObjectSubject) && warning.Subject.Where(IsObjectSubject).Any(HasIdentity)
                ? WarningDisplayState.NamedUnsupportedRoute
                : WarningDisplayState.UnresolvedIdentity;

        return state switch
        {
            ContractAttributionState.ExactUses => WarningDisplayState.ExactUses,
            ContractAttributionState.MembershipCandidates => WarningDisplayState.MembershipCandidates,
            ContractAttributionState.SpellingCandidates => WarningDisplayState.SpellingCandidates,
            ContractAttributionState.NoneInSelection => limits
                ? WarningDisplayState.NoFollowedRouteMatch
                : WarningDisplayState.NoneInSelection,
            ContractAttributionState.ProjectWide => WarningDisplayState.ProjectWide,
            ContractAttributionState.MissingObject => WarningDisplayState.MissingObject,
            ContractAttributionState.UnresolvedIdentity => WarningDisplayState.UnresolvedIdentity,
            _ => WarningDisplayState.EvidenceUnavailable,
        };
    }

    private static bool IsObjectSubject(GrammarWarningPart part) =>
        part.Role is GrammarWarningPartRole.Object or GrammarWarningPartRole.Missing;

    private static bool HasIdentity(GrammarWarningPart part) =>
        new[] { part.SubjectGuid, part.FieldWorksGuid, part.ObjectId }
            .Any(value => Guid.TryParse(value, out _));
}
