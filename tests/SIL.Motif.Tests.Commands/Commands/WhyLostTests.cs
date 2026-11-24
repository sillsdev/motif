using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// The Baseline-against-Trial comparison on a real PanGloss 0.32.0 capture: a Tamil dative whose lexeme form is blocked
/// by a new alternate form. The two captures are excerpts of <c>pangloss parse --trace-rejections</c> on the Baseline
/// and on the Trial copy, with every GUID replaced and every record not needed here dropped.
/// </summary>
public sealed class WhyLostTests
{
    private const string Word = "எனக்கு";

    private static string Fixture(string name)
    {
        using var stream = typeof(WhyLostTests).Assembly.GetManifestResourceStream("WhyLost." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static RejectionTraceResponse Trace(string name, string tracedAgainst)
    {
        Assert.True(RejectionTraceDocument.TryRead(Fixture(name), out var document, out var error), error);
        return RejectionTraceProjection.Project(document!, Word, tracedAgainst, 64);
    }

    private static RejectionTraceResponse Baseline() => Trace("tamil-baseline.jsonl", "baseline");

    private static RejectionTraceResponse Trial() => Trace("tamil-trial.jsonl", "proposal p");

    private static string BlockerId(RejectionTraceResponse trial) =>
        trial.Paths.Select(path => path.EarliestRejection).First(stop => stop?.Reason == "allomorph_blocked")!
            .Related.Single(related => related.Role == "blocker").Object.Ids.Single(id => id.IsSubject && id.Kind == "form").Id;

    private static OperationEnvelope Create(string blocker, string entry) => new(CanonicalId.Mint("op/"),
        "lexical/lexEntry/createAlternateForms", entityId: CanonicalId.Parse(blocker), target: CanonicalId.Parse(entry));

    private static Proposal Proposal(params OperationEnvelope[] operations) =>
        new(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint("proposal/"), null, operations);

    private sealed class Forms : WhyLostProjection.IFormReader
    {
        public string? TextOf(string formId) => "text:" + formId;

        public string? EntryOf(string formId) => null;
    }

    private static WhyLostResponse Project(RejectionTraceResponse baseline, RejectionTraceResponse trial, Proposal proposal,
        bool described = true)
    {
        using var context = RegressionContext.Build(proposal, null, null);
        return WhyLostProjection.Project(Word, "p", "job-1", baseline, trial, described, described, proposal, context, new Forms());
    }

    [Fact]
    public void ALostReadingNamesTheBlockedFormTheBlockerAndTheOperationThatCreatesIt()
    {
        var baseline = Baseline();
        var trial = Trial();
        var blocker = BlockerId(trial);
        var entry = trial.Paths.Select(path => path.EarliestRejection).First(stop => stop?.Reason == "allomorph_blocked")!
            .Subject.Ids.Single(id => id.Kind == "entry").Id;
        var proposal = Proposal(Create(blocker, entry));

        var response = Project(baseline, trial, proposal);

        Assert.Empty(response.Kept);
        Assert.Empty(response.Gained);
        var lost = Assert.Single(response.Lost);
        var cause = Assert.Single(lost.Causes);
        Assert.Equal("recorded", cause.Attribution);
        Assert.Equal("allomorph_blocked", cause.Reason);
        Assert.Equal("morphology", cause.Stage);
        Assert.Equal(blocker, cause.Blocker!.Id);
        Assert.Equal(entry, cause.Blocked!.EntryId);
        var operation = Assert.Single(cause.Operations);
        Assert.Equal((1, "creates"), (operation.Position, operation.Role));
        Assert.Contains("Operation 1 (lexical/lexEntry/createAlternateForms) creates the blocking form.", cause.Sentence, StringComparison.Ordinal);
        Assert.Contains("text:" + blocker, cause.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockerNoOperationNamesSaysSoAndNamesNoOperation()
    {
        var response = Project(Baseline(), Trial(), Proposal(Create(CanonicalId.Mint("form/").Value, CanonicalId.Mint("entry/").Value)));

        var cause = Assert.Single(Assert.Single(response.Lost).Causes);
        Assert.Equal("recorded", cause.Attribution);
        Assert.Empty(cause.Operations);
        Assert.Contains("No Proposal operation names the blocking form.", cause.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ATrialWithoutTheStopSaysNotRecordedAndNamesNoOperation()
    {
        var trial = Trial();
        var noStop = trial with { Paths = [.. trial.Paths.Where(path => path.EarliestRejection?.Reason != "allomorph_blocked")] };

        var response = Project(Baseline(), noStop, Proposal(Create(BlockerId(trial), CanonicalId.Mint("entry/").Value)));

        var cause = Assert.Single(Assert.Single(response.Lost).Causes);
        Assert.Equal("not-recorded", cause.Attribution);
        Assert.Empty(cause.Operations);
        Assert.StartsWith("Not recorded:", cause.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("Operation", cause.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AParserThatDoesNotDescribeTheStopDegradesToNotRecordedEvenWhenTheCaptureHoldsOne()
    {
        var trial = Trial();

        var response = Project(Baseline(), trial, Proposal(Create(BlockerId(trial), CanonicalId.Mint("entry/").Value)), described: false);

        var cause = Assert.Single(Assert.Single(response.Lost).Causes);
        Assert.Equal("not-recorded", cause.Attribution);
        Assert.Contains("does not describe an allomorph_blocked stop", cause.Sentence, StringComparison.Ordinal);
        Assert.False(response.Trial.BlockedStopDescribed);
    }

    [Fact]
    public void AnUnrelatedRecordedStopIsNotAttributedToTheLostReading()
    {
        var trial = Trial();
        Assert.Contains(trial.Paths, path => path.EarliestRejection is { Reason: not "allomorph_blocked" });
        var onlyOther = trial with { Paths = [.. trial.Paths.Where(path => path.EarliestRejection?.Reason != "allomorph_blocked")] };

        var response = Project(Baseline(), onlyOther, Proposal());

        Assert.Equal("not-recorded", Assert.Single(Assert.Single(response.Lost).Causes).Attribution);
    }

    [Fact]
    public void ASurvivingReadingIsKeptAndANewOneIsGained()
    {
        var baseline = Baseline();
        var gainedTrial = Trial() with { Outputs = baseline.Outputs.Take(1).ToList() };

        var kept = Project(baseline, gainedTrial, Proposal());
        Assert.Empty(kept.Lost);
        Assert.Single(kept.Kept);
        Assert.Contains("No Reading the Baseline has is missing", string.Join(' ', kept.Notes), StringComparison.Ordinal);

        var gained = Project(baseline with { Outputs = [] }, gainedTrial, Proposal());
        Assert.Single(gained.Gained);
        Assert.Empty(gained.Lost);
    }

    [Fact]
    public void GrammarHashesWithDifferentSemanticsAreNotCompared()
    {
        var baseline = Baseline();
        var other = Trial() with { Producer = Trial().Producer with { GrammarHashSemantics = "another-semantics-v2" } };
        using var context = RegressionContext.Build(Proposal(), null, null);

        var outcome = WhyLostQuery.Shape(new WhyLostRequest("p.fwdata", Word, "p"), baseline, null, other, null, null, Proposal(),
            context, new Forms());

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.WhyLostIncomparable, outcome.Refusal!.Code);
    }

    [Theory]
    [InlineData("", "p")]
    [InlineData("w", "")]
    [InlineData("w", "baseline")]
    [InlineData("w", "not a proposal id")]
    public void ARequestWithoutAWordOrAProposalIsRefusedBeforeAnyProjectOpens(string word, string proposal)
    {
        var outcome = WhyLostQuery.Compare(new WhyLostRequest("never-opened.fwdata", word, proposal), parserPath: null);

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.WhyLostRequestInvalid, outcome.Refusal!.Code);
    }

    [Fact]
    public void ARejectionTraceSentenceExistsForAnAllomorphBlock()
    {
        var sentence = RejectionTraceSentences.For("allomorph_blocked", rejection: true);

        Assert.DoesNotContain("has no sentence", sentence, StringComparison.Ordinal);
        Assert.Contains("earlier allomorph", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRealDescribeListsTheBlockedStopAndAnOlderOneDoesNot()
    {
        var current = PanGlossSurface.ReadDescription(Fixture("describe-0.32.0.json"), out _)!;
        Assert.Contains("allomorph_blocked", current.DiagnosticRejectReasons!.Value);
        Assert.Contains("required_slot_unfilled", current.DiagnosticRejectReasons.Value);

        var older = PanGlossSurface.ReadDescription(SIL.Motif.Tests.PanGloss.WhyNotRecordedTests.Recorded("describe"), out _)!;
        Assert.Null(older.DiagnosticRejectReasons);
    }
}
