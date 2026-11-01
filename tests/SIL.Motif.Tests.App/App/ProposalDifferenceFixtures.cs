using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A saved Difference held in memory, and a stand-in for the shared bounded query that answers from it the way the
/// store does: filters, word pages and one word's Text places.
/// </summary>
internal sealed class ProposalDifferenceFixture
{
    private readonly List<ProposalDifferenceWord> _words;

    public ProposalDifferenceFixture(int wordCount = 30, bool wordDetailKept = true, string? longestWord = null)
    {
        WordDetailKept = wordDetailKept;
        _words = [.. Enumerable.Range(0, wordCount).Select(index => Word(index, index == 0 ? longestWord : null))];
    }

    /// <summary>Whether word detail is kept; a test sets it false to decide the Proposal from outside the window.</summary>
    public bool WordDetailKept { get; set; }

    /// <summary>Awaited at the start of every read, so a test can hold a read open, fail it, or see its token.</summary>
    public Func<ProposalDifferenceShowRequest, CancellationToken, Task>? BeforeRead { get; set; }

    /// <summary>The warnings the summary carries; by default one the product really writes.</summary>
    public IReadOnlyList<string> Warnings { get; init; } =
    [
        ProposalDifferenceWarnings.ParserDiffers + "1.0.0 (0123456789ab) and the after side by 1.1.0 (fedcba987654). " +
            "A word that differs may differ because the parser changed, not the grammar.",
    ];

    public int PlacesPerWord { get; init; } = 3;

    /// <summary>The decisions the Difference was read against; by default a person's.</summary>
    public string Reference { get; init; } = ProposalDifferenceReferences.Human;

    /// <summary>Why the first word's decisions were staged, when a named program staged them.</summary>
    public IReadOnlyList<ProposalDifferenceWordEvidence>? FirstWordEvidence { get; init; }

    /// <summary>Makes every read match no word, as a filter that matches nothing does.</summary>
    public bool NoWordsMatch { get; set; }

    /// <summary>Applied Proposals the read could not parse; like the product, only read with words on the page.</summary>
    public IReadOnlyList<ProposalDifferenceUnreadableEvidence>? Unreadable { get; init; }

    public List<ProposalDifferenceShowRequest> Requests { get; } = [];

    public ProposalDifferenceSummary Summary => new("difference/one", "proposal/one", "job/one", "assessment/b",
        "assessment/a", "text/one", "Genesis 1", "baseline/one", new ProposalDifferencePolicy(), _words.Count,
        _words.Sum(word => word.Occurrences), 2, 12, CheckWords, ImprovedWords,
        [
            new(ProposalDifferenceBucket.Regression, 10, 20), new(ProposalDifferenceBucket.AnalysisChanged, 5, 9),
            new(ProposalDifferenceBucket.ImprovedUnique, 4, 8),
        ],
        [new("approved-unique", "no-approved-match", 10, 20), new("no-parse", "approved-unique", 4, 8)],
        "parsimony", null, Pin(), Pin(), Warnings, 3,
        [new(HumanAgreementLimitation.Capped, 2, 3)], "2026-10-31T00:00:00Z", Reference);

    public int CheckWords => 15;

    public int ImprovedWords => 4;

    public async Task<CommandOutcome<ProposalDifferenceResponse>> Read(ProposalDifferenceShowRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (BeforeRead is { } before) await before(request, cancellationToken);
        var matching = WordDetailKept && !NoWordsMatch ? _words.Where(word => Matches(request, word)).ToList() : [];
        var page = WordDetailKept ? matching.Skip(request.Offset).Take(request.Limit).ToList() : [];
        if (FirstWordEvidence is not null)
            page = [.. page.Select(word => word.Word == "word00" ? word with { Evidence = FirstWordEvidence } : word)];
        int? next = WordDetailKept && request.Offset + page.Count < matching.Count ? request.Offset + page.Count : null;
        ProposalDifferenceOccurrencePage? places = null;
        if (WordDetailKept && request.Word is { } word)
        {
            var all = Enumerable.Range(0, PlacesPerWord)
                .Select(index => new ProposalDifferenceOccurrencePlace("text/one", $"segment/{index}", index, index + 1))
                .ToList();
            var placePage = all.Skip(request.OccurrenceOffset ?? 0).Take(request.OccurrenceLimit ?? 25).ToList();
            places = new ProposalDifferenceOccurrencePage(word, placePage, all.Count,
                (request.OccurrenceOffset ?? 0) + placePage.Count < all.Count
                    ? (request.OccurrenceOffset ?? 0) + placePage.Count : null);
        }
        return CommandOutcome<ProposalDifferenceResponse>.Success(new ProposalDifferenceResponse(
            Summary, page, matching.Count, next, WordDetailKept, places, page.Count > 0 ? Unreadable : null));
    }

    private static bool Matches(ProposalDifferenceShowRequest request, ProposalDifferenceWord word) =>
        (request.Word is null || word.Word == request.Word) &&
        (!request.CouldBeBad || word.Buckets.Any(IsCheck)) &&
        (request.Bucket is null || word.Buckets.Any(bucket => Code(bucket) == request.Bucket)) &&
        (request.BeforeState is null || word.BeforeState == request.BeforeState) &&
        (request.AfterState is null || word.AfterState == request.AfterState) &&
        (request.Limitation is null || word.Facts.Before.Limitations.Concat(word.Facts.After.Limitations)
            .Any(limit => Code(limit) == request.Limitation));

    private static bool IsCheck(ProposalDifferenceBucket bucket) =>
        bucket is not (ProposalDifferenceBucket.ImprovedApprovedMatch or ProposalDifferenceBucket.ImprovedUnique or
            ProposalDifferenceBucket.ImprovedNegativeRejection);

    private static string Code<T>(T value) where T : struct, Enum =>
        SIL.Motif.App.ViewModels.ProposalDifferenceWords.Code(value);

    // Words 0-9 lost their Approved analysis and are capped, 10-14 changed, the rest improved.
    private static ProposalDifferenceWord Word(int index, string? name = null)
    {
        var lost = index < 10;
        var changed = index is >= 10 and < 15;
        var before = lost ? "approved-unique" : changed ? "approved-unique" : "no-parse";
        var after = lost ? "no-approved-match" : changed ? "approved-unique" : "approved-unique";
        var bucket = lost ? ProposalDifferenceBucket.Regression
            : changed ? ProposalDifferenceBucket.AnalysisChanged : ProposalDifferenceBucket.ImprovedUnique;
        var limits = index < 2 ? new[] { HumanAgreementLimitation.Capped } : [];
        return new ProposalDifferenceWord(name ?? $"word{index:00}", 2, before, after, [], [bucket],
            new ProposalDifferenceCaseFacts(new ProposalDifferencePolicy(), Facts(limits), Facts(limits), true, [], [],
                [], [], [], []));
    }

    private static HumanAgreementFacts Facts(IReadOnlyList<HumanAgreementLimitation> limits) =>
        new(HumanAgreementEvidenceAvailability.Available, HumanAgreementSearchCompletion.Complete, limits,
            HumanAgreementApprovalState.ApprovedUnique, 1, 1, 1, [], [], [], [], [], [], [],
            HumanAgreementSpellingAcceptance.NotApplicable, []);

    private static ProposalDifferenceSidePin Pin() =>
        new("1.0.0", "sha256:p", "sha256:g", "selection", "sha256:s", "tokeniser", "1");
}
