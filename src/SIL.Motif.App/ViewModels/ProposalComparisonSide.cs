using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Where a comparison stands.</summary>
public enum ProposalComparisonState
{
    /// <summary>A Proposal is still being read or measured.</summary>
    Reading,

    /// <summary>Both Proposals were measured against one body of evidence, so their effects can be set side by side.</summary>
    Comparable,

    /// <summary>The project or a Proposal changed after a measurement, so it no longer stands.</summary>
    Outdated,

    /// <summary>The evidence for a fair comparison does not exist or does not match.</summary>
    Unavailable,
}

/// <summary>Why a comparison is not offered.</summary>
public enum ProposalComparisonReason
{
    None,
    Reading,
    Unreadable,
    Outdated,
    NoEvidence,
    DifferentText,
    DifferentBaseline,
    DifferentWords,
    DecisionsNotRecorded,
    DifferentDecisions,
    DifferentAssessor,
    Incomplete,
}

/// <summary>What the evidence says about whether two Proposals can be compared.</summary>
/// <param name="State">Where the comparison stands.</param>
/// <param name="Reason">Why it is not offered, or <see cref="ProposalComparisonReason.None"/>.</param>
/// <param name="CanRemeasure">Whether measuring again could make the pair comparable.</param>
public sealed record ProposalComparability(ProposalComparisonState State, ProposalComparisonReason Reason, bool CanRemeasure)
{
    /// <summary>The window's words for <see cref="Reason"/>.</summary>
    public string ReasonText => Reason switch
    {
        ProposalComparisonReason.Unreadable => MotifAppStrings.ProposalCompareReasonUnreadable,
        ProposalComparisonReason.Outdated => MotifAppStrings.ProposalCompareReasonOutdated,
        ProposalComparisonReason.NoEvidence => MotifAppStrings.ProposalCompareReasonNoEvidence,
        ProposalComparisonReason.DifferentText => MotifAppStrings.ProposalCompareReasonText,
        ProposalComparisonReason.DifferentBaseline => MotifAppStrings.ProposalCompareReasonBaseline,
        ProposalComparisonReason.DifferentWords => MotifAppStrings.ProposalCompareReasonWords,
        ProposalComparisonReason.DecisionsNotRecorded => MotifAppStrings.ProposalCompareReasonDecisionsMissing,
        ProposalComparisonReason.DifferentDecisions => MotifAppStrings.ProposalCompareReasonDecisions,
        ProposalComparisonReason.DifferentAssessor => MotifAppStrings.ProposalCompareReasonAssessor,
        ProposalComparisonReason.Incomplete => MotifAppStrings.ProposalCompareReasonIncomplete,
        ProposalComparisonReason.Reading => MotifAppStrings.ProposalCompareReading,
        _ => string.Empty,
    };

    /// <summary>
    /// Decides from each Proposal's saved Difference alone. Two Proposals compare only when each one's evidence is current
    /// and complete and both name the same project, Text, Baseline, words, parser, tokeniser, assessor and recorded
    /// decisions; anything else is outdated or unavailable and never subtracted.
    /// </summary>
    public static ProposalComparability Evaluate(WorkspaceContext context, ProposalDocumentViewModel first,
        ProposalDocumentViewModel second)
    {
        var sides = new[] { first, second };
        if (sides.Any(side => side.ProjectPath != context.ProjectPath))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.Unreadable, false);
        if (sides.Any(Busy)) return new(ProposalComparisonState.Reading, ProposalComparisonReason.Reading, false);
        if (sides.Any(side => side.Detail is null || side.DetailRefusal is not null))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.Unreadable, true);
        if (sides.Any(side => side.IsOutdated) || context.Evidence.IsStale)
            return new(ProposalComparisonState.Outdated, ProposalComparisonReason.Outdated, true);
        if (sides.Any(side => !side.Difference.IsShown || side.Difference.Summary is null))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.NoEvidence, true);
        if (sides.Any(side => !side.Evidence.CanShowFacts))
            return new(ProposalComparisonState.Outdated, ProposalComparisonReason.Outdated, true);
        if (sides.Any(side => !side.Evidence.Complete))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.Incomplete, false);
        var a = first.Difference.Summary!;
        var b = second.Difference.Summary!;
        if (!string.Equals(a.TextId, b.TextId, StringComparison.Ordinal))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.DifferentText, true);
        if (!SameBaseline(a.BaselineToken, b.BaselineToken))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.DifferentBaseline, true);
        if (!SameReference(a.Before, b.Before))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.DifferentWords, true);
        if (a.Reference != b.Reference ||
            (a.DecisionsDigest is not null && b.DecisionsDigest is not null && a.DecisionsDigest != b.DecisionsDigest))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.DifferentDecisions, true);
        if (string.IsNullOrEmpty(a.DecisionsDigest) || string.IsNullOrEmpty(b.DecisionsDigest))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.DecisionsNotRecorded, true);
        if (a.Assessor != b.Assessor || !Equals(a.Policy, b.Policy))
            return new(ProposalComparisonState.Unavailable, ProposalComparisonReason.DifferentAssessor, true);
        return new(ProposalComparisonState.Comparable, ProposalComparisonReason.None, false);
    }

    private static bool Busy(ProposalDocumentViewModel side) =>
        side.IsLoading || side.Difference.IsLoading || side.Difference.Maker.IsActive;

    // Both Proposals' own Before sides are the common reference, so every pinned fact of it must be the same.
    private static bool SameReference(ProposalDifferenceSidePin x, ProposalDifferenceSidePin y) =>
        x.GrammarSourceSha256 == y.GrammarSourceSha256 && x.SelectionSha256 == y.SelectionSha256 &&
        x.ParserSha256 == y.ParserSha256 && x.ParserVersion == y.ParserVersion &&
        x.TokeniserName == y.TokeniserName && x.TokeniserVersion == y.TokeniserVersion &&
        x.GrammarExportFormat == y.GrammarExportFormat;

    private static bool SameBaseline(string x, string y)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var left = JsonSerializer.Deserialize<BaselineToken>(x, options);
            var right = JsonSerializer.Deserialize<BaselineToken>(y, options);
            return left is not null && left.HasSameSemanticIdentity(right);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException) { return false; }
    }
}

/// <summary>One Proposal in a comparison: its identity and the evidence its open document already holds.</summary>
public sealed class ProposalComparisonSide(ProposalDocumentViewModel document, string heading) : ObservableObject
{
    /// <summary>The open document the side reads and the Apply path acts on.</summary>
    public ProposalDocumentViewModel Document { get; } = document;

    /// <summary>"First Proposal" or "Second Proposal".</summary>
    public string Heading { get; } = heading;

    /// <summary>The Proposal's label.</summary>
    public string Label => Document.Label;

    /// <summary>The Proposal's label under its heading, for the accessible name.</summary>
    public string Name => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalCompareSideNameFormat, Heading, Label);

    /// <summary>Draft, Ready and the other states.</summary>
    public string StatusText => Document.StatusText;

    /// <summary>Who built the Proposal.</summary>
    public string BuiltByText => Document.Card?.BuiltByText ?? string.Empty;

    /// <summary>The model that built it.</summary>
    public string ModelText => Document.Card?.ModelText ?? string.Empty;

    /// <summary>The count of changes to the grammar, when the Proposal's detail has been read.</summary>
    public string OperationsText => Document.Detail is { } detail
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalCompareOperationsFormat, detail.Operations.Count)
        : string.Empty;

    /// <summary>The Proposal's changes to the grammar in words.</summary>
    public IReadOnlyList<string> OperationDescriptions => Document.OperationDescriptions;

    /// <summary>How many Comments the Proposal has.</summary>
    public string CommentsText => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalCompareCommentsFormat,
        Document.Detail?.Comments?.Count ?? 0);

    /// <summary>The words the Proposal will fill in, over the compared Text.</summary>
    public IReadOnlyList<ProposalEvidenceWord> WillFillIn => Document.Evidence.WillFillIn;

    /// <summary>The words whose reading or spelling the Proposal will change.</summary>
    public IReadOnlyList<ProposalEvidenceWord> WillChange => Document.Evidence.WillChange;

    /// <summary>The number of words it fills in.</summary>
    public string FillInCount => Document.Evidence.FillInCount;

    /// <summary>The number of words it changes.</summary>
    public string ChangeCount => Document.Evidence.ChangeCount;

    /// <summary>Whether the effects show; only a proven comparison lists them.</summary>
    public bool ShowsEffects { get; private set; }

    /// <summary>Tells the side its document changed.</summary>
    /// <param name="showsEffects">Whether the comparison is proven, so the side's effects may show.</param>
    public void Refresh(bool showsEffects)
    {
        ShowsEffects = showsEffects;
        OnPropertyChanged(nameof(ShowsEffects));
        foreach (var name in new[] { nameof(Label), nameof(Name), nameof(StatusText), nameof(BuiltByText), nameof(ModelText),
                     nameof(OperationsText), nameof(OperationDescriptions), nameof(CommentsText), nameof(WillFillIn),
                     nameof(WillChange), nameof(FillInCount), nameof(ChangeCount) })
            OnPropertyChanged(name);
    }
}
