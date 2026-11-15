using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class WordTraceSessionViewModelTests
{
    [Fact]
    public async Task TypedWordLoadsEveryStoredOpinionWithoutAnAssessment()
    {
        var fake = new FakeCommandClient();
        var approved = new ParserReading([new("dog", "dog", "n", null, false, null)])
            { StoredAnalysisId = "approved", StoredAnalysisOpinion = "approved" };
        var rejected = approved with { StoredAnalysisId = "rejected", StoredAnalysisOpinion = "disapproved" };
        var candidate = approved with { StoredAnalysisId = "candidate", StoredAnalysisOpinion = "candidate" };
        fake.WordContextHandler = (request, _) => Task.FromResult(CommandOutcome<WordContextResponse>.Success(
            new WordContextResponse(request.Word, true)
            { IsInFieldWorks = true, Analyses = [approved, rejected, candidate], ExpectedAnalysis = approved }));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        var session = context.WordTraceSession;

        await session.RefreshExpectedAsync("dogs");

        Assert.Equal("dog", Assert.Single(session.Trace.ExpectedMorphs).Form);
        Assert.Equal(["approved", "disapproved", "candidate"],
            session.WordContext!.Analyses.Select(analysis => analysis.StoredAnalysisOpinion));
        Assert.Null(context.Evidence.Assessment);
        Assert.Empty(fake.TraceWordRequests);
        await session.StopAsync();
    }

    [Fact]
    public async Task PastedWordIsNormalizedBeforeReadingFieldWorks()
    {
        var fake = new FakeCommandClient();
        fake.WordContextHandler = (request, _) => Task.FromResult(
            CommandOutcome<WordContextResponse>.Success(new WordContextResponse(request.Word, true)));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        var session = context.WordTraceSession;

        await session.RefreshExpectedAsync("\u00e9");

        Assert.Equal("e\u0301", Assert.Single(fake.WordContextRequests).Word);
        await session.StopAsync();
    }

    [Theory]
    [InlineData(false, null, "FieldWorks word context unavailable")]
    [InlineData(true, null, "FieldWorks membership not recorded")]
    [InlineData(true, false, "Not in FieldWorks")]
    [InlineData(true, true, "No stored analyses in the captured FieldWorks Baseline")]
    public async Task ResultContextDistinguishesUnavailableAbsentAndEmpty(bool baseline, bool? member, string expected)
    {
        var fake = new FakeCommandClient();
        fake.WordContextHandler = (request, _) => Task.FromResult(CommandOutcome<WordContextResponse>.Success(
            new WordContextResponse(request.Word, baseline) { IsInFieldWorks = member }));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        var session = context.WordTraceSession;
        var reading = TraceReadingBuilder.Build("dogs", new TraceStep("WordAnalysis", null, null, null, null, []), [], []);
        session.Trace.Result = new WordTraceResponse("dogs", false, true, null, 0, null, 0, reading);

        await session.RefreshResultAsync();

        Assert.Equal(expected, session.FieldWorksContextStatus);
        await session.StopAsync();
    }

    [Fact]
    public void IdenticalFieldWorksRenderingsKeepAllOpinionCountsWithoutClaimingIdentityEquality()
    {
        var readings = new string?[] { "approved", "disapproved", "candidate", null }.Select(opinion =>
            new ParserReading([new("matin", "stem", "n", null, false, null), new("lu", "suffix", "", null, false, null)])
            { StoredAnalysisOpinion = opinion }).ToArray();
        var row = new TryWordStoredAnalysis(readings, null);
        Assert.Equal("matin + lu", row.Text);
        Assert.Equal("Approved 1 · Disapproved 1 · Unknown 2", row.OpinionLabel);
        Assert.Equal(["stem", "suffix"], row.Morphs.Select(morph => morph.GlossOrPlaceholder));
        Assert.All(row.Pieces, piece => Assert.Empty(piece.StatusParts));
        Assert.True(row.HasUnrecordedStep);
    }

    [Fact]
    public void PieceMarksRequireExactAuthoredIdentityRatherThanEqualLabels()
    {
        const string msa = "00000000-0000-0000-0000-000000000777";
        var step = new TraceStep("MorphologicalRuleSynthesis", "ja-", null, null, "RequiredSyntacticFeatureStruct", [])
        { SourceIdentityKind = "morphRule", SourceIdentityId = msa, SourceIdentityQuality = "authored" };
        var reading = TraceReadingBuilder.Build("word", step, [], []);
        var sameLabel = new ParserReadingMorph("ja-", "NEG.PERF", "v", null, false, null);
        Assert.Empty(new TryWordTracePiece(sameLabel, reading).StatusParts);
        var refused = new TryWordTracePiece(sameLabel with { GrammaticalInfoId = msa }, reading);
        Assert.Equal("refused", refused.Status);
        Assert.Equal(Mark.Of(TraceStepMark.Refused), Assert.Single(refused.StatusParts).Mark);
        var grouped = new TryWordStoredAnalysis([
            new ParserReading([sameLabel]),
            new ParserReading([sameLabel with { GrammaticalInfoId = msa }])], reading);
        Assert.Equal("Trace marks differ", Assert.Single(grouped.Pieces).Status);
        Assert.Empty(new TryWordTracePiece(sameLabel with { GrammaticalInfoId = msa },
            TraceReadingBuilder.Build("word", step with { SourceIdentityQuality = "grammar-local" }, [], [])).StatusParts);
    }

    [Fact]
    public async Task ChangingTheTraceWordReadsItsStoredContextWithoutOpeningAPage()
    {
        var fake = new FakeCommandClient();
        fake.WordContextHandler = (request, _) => Task.FromResult(
            CommandOutcome<WordContextResponse>.Success(new WordContextResponse(request.Word, false)));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        var session = context.WordTraceSession;
        var initialPage = context.CurrentPage;

        session.Trace.WordToTry = "dogs";

        Assert.Equal("dogs", session.WordContext?.Word);
        Assert.Equal("dogs", Assert.Single(fake.WordContextRequests).Word);
        Assert.Equal(initialPage, context.CurrentPage);
        Assert.Empty(fake.TraceWordRequests);
        await session.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResultComparisonRequiresMatchingRecordedIdentities(bool same)
    {
        const string form = "00000000-0000-0000-0000-000000000771";
        const string other = "00000000-0000-0000-0000-000000000772";
        const string msa = "00000000-0000-0000-0000-000000000773";
        var fake = new FakeCommandClient();
        var stored = new ParserReading([new("dog", "dog", "n", null, false, null)])
        {
            Identity = new ApprovedMorphology([new(form, msa, null, ["dog"])]),
            StoredAnalysisId = "stored",
            StoredAnalysisOpinion = ReadingGrade.Candidate,
        };
        fake.WordContextHandler = (request, _) => Task.FromResult(CommandOutcome<WordContextResponse>.Success(
            new WordContextResponse(request.Word, true) { IsInFieldWorks = true, Analyses = [stored] }));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        var session = context.WordTraceSession;
        var morph = new TraceMorph(null, "dog", null, "dog", "n", null, null, null, null, null)
        { FormId = same ? form : other, MsaId = msa, IdentityQuality = "authored" };
        var analysis = new TraceAnalysis("one", 0, "dog", "recorded", [morph]) { ProjectionStatus = "available" };
        var root = new TraceStep("WordAnalysis", null, null, null, null, []);
        var reading = TraceReadingBuilder.Build("dogs", root, [], [analysis]);

        session.Trace.Result = new WordTraceResponse("dogs", true, true, null, 1, null, 1, reading);

        Assert.Equal(same ? "Same" : "Different", session.ResultOutcomeText);
        Assert.Equal(same ? Mark.Same : Mark.Different, session.ResultOutcomeMark);
        await session.StopAsync();
    }

    [Fact]
    public async Task WorkspaceOwnsAndStopsTheSharedTraceSession()
    {
        var fake = new FakeCommandClient();
        var pending = new TaskCompletionSource<CommandOutcome<WordContextResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.WordContextHandler = (_, token) =>
        {
            token.Register(() => pending.TrySetCanceled(token));
            return pending.Task;
        };
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var session = context.WordTraceSession;
        Assert.Same(session, context.WordTraceSession);
        var read = session.RefreshExpectedAsync("word");

        await context.StopProjectWorkAsync();

        Assert.True(read.IsCompleted);
        Assert.Null(session.WordContext);
    }

    [Fact]
    public async Task LateWordContextCannotReplaceTheNewWord()
    {
        var fake = new FakeCommandClient();
        var old = new TaskCompletionSource<CommandOutcome<WordContextResponse>>();
        fake.WordContextHandler = (request, _) => request.Word == "old" ? old.Task : Task.FromResult(
            CommandOutcome<WordContextResponse>.Success(new WordContextResponse(request.Word, false)));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        var session = new WordTraceSessionViewModel(context);

        var first = session.RefreshExpectedAsync("old");
        await session.RefreshExpectedAsync("new");
        old.SetResult(CommandOutcome<WordContextResponse>.Success(new WordContextResponse("old", true)));
        await first;

        Assert.Equal("new", session.WordContext?.Word);
        await session.StopAsync();
        Assert.Null(session.WordContext);
    }

    [Fact]
    public async Task StaleProjectCannotPublishAnEarlierWordContext()
    {
        var fake = new FakeCommandClient();
        var pending = new TaskCompletionSource<CommandOutcome<WordContextResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.WordContextHandler = (_, _) => pending.Task;
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var session = context.WordTraceSession;
        var read = session.RefreshExpectedAsync("word");

        context.RecordApplied();
        pending.SetResult(CommandOutcome<WordContextResponse>.Success(new WordContextResponse("word", true)));
        await read;

        Assert.Null(session.WordContext);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task ContextFromADifferentBaselineIsNotShown()
    {
        static BaselineToken Token(char digit) => new("project", "sha256:" + new string(digit, 64), "1",
            "2026-09-05T11:02:00Z", "sha256:" + new string(digit, 64));
        var expected = Token('a');
        var fake = new FakeCommandClient();
        fake.WordContextHandler = (request, _) => Task.FromResult(
            CommandOutcome<WordContextResponse>.Success(new WordContextResponse(request.Word, true) { Baseline = Token('b') }));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null) { Token = expected };
        var session = context.WordTraceSession;

        await session.RefreshExpectedAsync("word");

        Assert.Equal(expected, Assert.Single(fake.WordContextRequests).ExpectedBaseline);
        Assert.Null(session.WordContext);
        await session.StopAsync();
    }

    [Fact]
    public async Task ATraceFromAnEarlierBaselineDoesNotBorrowCurrentWordContext()
    {
        static BaselineToken Token(char digit) => new("project", "sha256:" + new string(digit, 64), "1",
            "2026-09-05T11:02:00Z", "sha256:" + new string(digit, 64));
        var current = Token('a');
        var traced = Token('b');
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null) { Token = current };
        var session = context.WordTraceSession;
        var reading = TraceReadingBuilder.Build("word", new TraceStep("WordAnalysis", null, null, null, null, []), [], []);
        session.Trace.Result = new WordTraceResponse("word", false, true, null, 0, null, 0, reading)
        {
            HostCapture = new TraceHostCapture(null, null, null, traced.BundleDigest, null, null, [])
            {
                Baseline = new TraceBaselineSource(traced, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "capture"),
            },
        };

        await session.RefreshResultAsync();

        Assert.Null(session.ResultWordContext);
        Assert.Empty(fake.WordContextRequests);
        await session.StopAsync();
    }

    [Fact]
    public async Task ANewFieldWorksSaveMarksOnlyATraceOfTheSameBaselineStale()
    {
        var assessment = WorkspaceContextTests.Assessment();
        var baseline = assessment.Baseline.Token;
        var fake = new FakeCommandClient();
        fake.WordContextHandler = (request, _) => Task.FromResult(CommandOutcome<WordContextResponse>.Success(
            new WordContextResponse(request.Word, true) { Baseline = baseline, IsStale = true }));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null) { Token = baseline };
        context.PublishEvidence(new WorkspaceEvidence(assessment, DateTimeOffset.UtcNow, false));
        await context.EvidencePublication;
        var session = context.WordTraceSession;
        var reading = TraceReadingBuilder.Build("dogs", new TraceStep("WordAnalysis", null, null, null, null, []), [], []);
        session.Trace.Result = new WordTraceResponse("dogs", false, true, null, 0, null, 0, reading)
        {
            HostCapture = new TraceHostCapture(null, null, null, baseline.BundleDigest, null, null, [])
            {
                Baseline = new TraceBaselineSource(baseline, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "capture"),
            },
        };

        await session.RefreshExpectedAsync("cats");

        Assert.True(context.Evidence.IsStale);
        Assert.True(session.IsTraceBaselineStale);
        Assert.Equal("dogs", session.Trace.Result!.Word);
        await session.StopAsync();
    }

    [Fact]
    public async Task StopCancelsAndJoinsPendingContextReads()
    {
        var fake = new FakeCommandClient();
        var pending = new TaskCompletionSource<CommandOutcome<WordContextResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.WordContextHandler = (_, token) =>
        {
            token.Register(() => pending.TrySetCanceled(token));
            return pending.Task;
        };
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        var session = new WordTraceSessionViewModel(context);
        var read = session.RefreshExpectedAsync("word");

        await session.StopAsync();

        Assert.True(read.IsCompleted);
        Assert.Null(session.WordContext);
    }
}
