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

    /// <summary>How many places each word has in the Text when only <see cref="PlacesPerWord"/> of them are listed.</summary>
    public int? PlacesCounted { get; init; }

    /// <summary>The decisions the Difference was read against; by default a person's.</summary>
    public string Reference { get; set; } = ProposalDifferenceReferences.Human;

    /// <summary>Why the first word's decisions were staged, when a named program staged them.</summary>
    public IReadOnlyList<ProposalDifferenceWordEvidence>? FirstWordEvidence { get; init; }

    /// <summary>Makes every read match no word, as a filter that matches nothing does.</summary>
    public bool NoWordsMatch { get; set; }

    /// <summary>Applied Proposals the read could not parse; like the product, only read with words on the page.</summary>
    public IReadOnlyList<ProposalDifferenceUnreadableEvidence>? Unreadable { get; init; }

    /// <summary>The Text the saved Difference compares; a test sets it to the Text it made or saved.</summary>
    public string TextId { get; set; } = "text/one";

    /// <summary>The title of that Text.</summary>
    public string TextTitle { get; set; } = "Genesis 1";

    /// <summary>Other Differences the Proposal has saved, listed beside the one this fixture answers with.</summary>
    public List<ProposalDifferenceSaved> SavedOthers { get; } = [];

    /// <summary>How many more saved Differences the bounded list leaves out.</summary>
    public int SavedNotListed { get; set; }

    /// <summary>How many saved Differences the read could not open and so left out of the list.</summary>
    public int SavedUnreadable { get; set; }

    /// <summary>The Differences the read could not open, named by Text and reference; counted in <see cref="SavedUnreadable"/>.</summary>
    public List<ProposalDifferenceDamaged> SavedDamaged { get; } = [];

    public List<ProposalDifferenceShowRequest> Requests { get; } = [];

    /// <summary>More regression words the Difference counts but does not list, as when a category holds more than the bound.</summary>
    public int Cut { get; init; }

    /// <summary>Words in the unchanged approved cell, counted and not listed.</summary>
    public int Unchanged { get; init; }

    /// <summary>Whether the summary reads but the word rows are damaged.</summary>
    public bool WordRowsDamaged { get; set; }

    /// <summary>Every saved Difference is damaged and named here, so a read without an id is refused.</summary>
    public List<ProposalDifferenceDamaged>? EveryDifferenceDamaged { get; set; }

    private bool Bounded => Cut > 0 || Unchanged > 0;

    public ProposalDifferenceSummary Summary => new("difference/one", "proposal/one", "job/one", "assessment/b",
        "assessment/a", TextId, TextTitle, "baseline/one", new ProposalDifferencePolicy(), _words.Count + Cut + Unchanged,
        _words.Sum(word => word.Occurrences), 2, 12, CheckWords + Cut, ImprovedWords,
        [
            new(ProposalDifferenceBucket.Regression, 10 + Cut, 20, Bounded ? 10 : null),
            new(ProposalDifferenceBucket.AnalysisChanged, 5, 9, Bounded ? 5 : null),
            new(ProposalDifferenceBucket.ImprovedUnique, 4, 8, Bounded ? 4 : null),
        ],
        [
            new("approved-unique", "no-approved-match", 10 + Cut, 20, Bounded ? 10 : null),
            new("no-parse", "approved-unique", 4, 8, Bounded ? 4 : null),
            .. Unchanged > 0 ? new[] { new ProposalDifferenceMatrixCell("approved-unique", "approved-unique", Unchanged, Unchanged, 0) } : [],
        ],
        "parsimony", null, Pin(), Pin(), Warnings, 3,
        [new(HumanAgreementLimitation.Capped, 2, 3, Bounded ? 2 : null)], "2026-10-31T00:00:00Z", Reference,
        WordsListed: Bounded ? _words.Count : null);

    public int CheckWords => 15;

    public int ImprovedWords => 4;

    public async Task<CommandOutcome<ProposalDifferenceResponse>> Read(ProposalDifferenceShowRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (BeforeRead is { } before) await before(request, cancellationToken);
        if (EveryDifferenceDamaged is { } damaged && request.DifferenceId is null)
            return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(RefusalCodes.DifferenceUnreadable,
                FailureReason.StoreInconsistent, "None of the Proposal's saved Differences can be read.",
                ProposalDifferenceDamagedFacts.ToFacts("proposal/one", damaged)));
        if (WordRowsDamaged)
            return CommandOutcome<ProposalDifferenceResponse>.Success(new ProposalDifferenceResponse(Summary, [], 0, null,
                false, null, null, [new ProposalDifferenceSaved("difference/one", TextId, TextTitle, Reference,
                    "2026-10-31T00:00:00Z", false)], 1, 0, null, 0, true));
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
            places = new ProposalDifferenceOccurrencePage(word, placePage, PlacesCounted ?? all.Count,
                (request.OccurrenceOffset ?? 0) + placePage.Count < all.Count
                    ? (request.OccurrenceOffset ?? 0) + placePage.Count : null,
                PlacesCounted is null ? null : all.Count);
        }
        return CommandOutcome<ProposalDifferenceResponse>.Success(new ProposalDifferenceResponse(
            Summary, page, matching.Count, next, WordDetailKept, places, page.Count > 0 ? Unreadable : null,
            SavedOthers.Prepend(new ProposalDifferenceSaved("difference/one", TextId, TextTitle, Reference,
                "2026-10-31T00:00:00Z", WordDetailKept)).ToList(), SavedOthers.Count + 1 + SavedNotListed + SavedUnreadable + SavedDamaged.Count,
                SavedUnreadable + SavedDamaged.Count, SavedDamaged.Count == 0 ? null : SavedDamaged,
                WordDetailKept ? _words.Count : 0));
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
