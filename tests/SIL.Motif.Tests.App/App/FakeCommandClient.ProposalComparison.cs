using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

/// <summary>One word of a Proposal's saved Difference: how many readings it had before and with the Proposal.</summary>
internal sealed record ComparisonWord(string Word, int Before, int After, string? GainedForm = null);

/// <summary>
/// Saved Differences for several Proposals over one common reference, each editable to break one fact of that
/// reference. Installed on a fake it answers the Proposal, review and Difference reads of every Proposal added.
/// </summary>
internal sealed class ProposalComparisonScenario
{
    private readonly BaselineToken _baseline = WorkspaceContextTests.Assessment().Baseline.Token;
    private readonly Dictionary<string, (IReadOnlyList<ComparisonWord> Words, Func<ProposalDifferenceSummary, ProposalDifferenceSummary> Change)> _proposals = [];

    /// <summary>Proposals that have no saved Difference, so their read is refused.</summary>
    public HashSet<string> Missing { get; } = [];

    /// <summary>Awaited at the start of every Difference read, so a test can hold one open.</summary>
    public Func<string, CancellationToken, Task>? BeforeRead { get; set; }

    public static string Json(BaselineToken token) => System.Text.Json.JsonSerializer.Serialize(token);

    /// <summary>Adds a Proposal; <paramref name="change"/> alters the summary the common reference would give it.</summary>
    public ProposalComparisonScenario Add(string id, ComparisonWord[] words,
        Func<ProposalDifferenceSummary, ProposalDifferenceSummary>? change = null)
    {
        _proposals[id] = (words, change ?? (summary => summary));
        return this;
    }

    public void Replace(string id, Func<ProposalDifferenceSummary, ProposalDifferenceSummary> change) =>
        _proposals[id] = (_proposals[id].Words, change);

    public ProposalDifferenceSummary SummaryOf(string id)
    {
        var (words, change) = _proposals[id];
        var pin = new ProposalDifferenceSidePin("1.0.0", "sha256:p", "sha256:g", "selection", "sha256:s", "tokeniser", "1");
        var summary = new ProposalDifferenceFixture(0).Summary with
        {
            DifferenceId = "difference/" + id, ProposalId = id, TrialJobId = "job/" + id, BaselineToken = Json(_baseline),
            ProposalIntentDigest = "intent/" + id, Words = words.Count, WordsListed = words.Count, UnboundOccurrences = 0,
            Limitations = [], Warnings = [], Before = pin, After = pin, DecisionsDigest = "sha256:decisions",
            Assessor = "parsimony", TextId = "text/one", TextTitle = "Genesis 1",
        };
        return change(summary);
    }

    public void InstallOn(FakeCommandClient fake)
    {
        fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
            new(request.ProposalId, "proposed", request.ProposalId, null, "intent/" + request.ProposalId, [])));
        fake.ProposalReviewHandler = (request, _) =>
        {
            var id = request.ProposalId ?? string.Empty;
            return Task.FromResult(CommandOutcome<ProposalReviewResponse>.Success(
                new ProposalReviewResponse(id, "intent/" + id, "revision/one",
                    [new GrammarChangeEntry("entry", 0, "A change", ["op/one"], [], "content", false, "composer",
                        "A change", [], [], [])], Trial(id), new(true, false, false, null, [], false), [], 0)));
        };
        fake.OnProposalDifference(async (request, token) =>
        {
            var id = request.ProposalId ?? throw new InvalidOperationException("A Proposal is named.");
            if (BeforeRead is { } before) await before(id, token);
            if (Missing.Contains(id) || !_proposals.TryGetValue(id, out var entry))
                return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                    RefusalCodes.DifferenceNone, FailureReason.NotFound, "None"));
            var words = entry.Words.Select(Word).ToArray();
            return CommandOutcome<ProposalDifferenceResponse>.Success(
                new(SummaryOf(id), words, words.Length, null, true));
        });
    }

    public TrialRecord Trial(string id) => new("job/" + id, id, "intent/" + id, _baseline, null,
        new Dictionary<string, string>(), [], false, null, JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current);

    private static ProposalDifferenceWord Word(ComparisonWord word)
    {
        var gained = word.GainedForm is null ? [] : new[]
        {
            new HumanAgreementProducedIdentity([new HumanAgreementProducedMorph(word.GainedForm, "msa", null, null)]),
        };
        var facts = new ProposalDifferenceCaseFacts(new ProposalDifferencePolicy(), Side(word.Before), Side(word.After),
            true, [], [], [], gained, [], []);
        return new(word.Word, 1, "no-parse", "approved-unique", [], [], facts);
    }

    private static HumanAgreementFacts Side(int count) =>
        new(HumanAgreementEvidenceAvailability.Available, HumanAgreementSearchCompletion.Complete, [],
            count == 0 ? HumanAgreementApprovalState.NoParse : HumanAgreementApprovalState.ApprovedUnique,
            count, 0, 0, [], [], [], [], [], [], [], HumanAgreementSpellingAcceptance.NotApplicable, []);
}
