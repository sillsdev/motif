using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="StatsCommand"/> over a real, file-backed project store: resolving the current Baseline
/// Assessment by default and a Trial Assessment under <c>--proposal</c>, refusing an absent grammar, cache,
/// or Assessment with a distinct <c>stats.*</c> code each, the <c>--format</c>/<c>--json</c> conflict, that
/// forwarding to the invocation module preserves argument order exactly and appends <c>--format jsonl</c>
/// only for JSON rows, and that every non-completed outcome the invoker returns becomes its own refusal.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class StatsCommandTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.StatsCommandTests", Guid.NewGuid().ToString("N"));

    public StatsCommandTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void NoBaselineIsRefused()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var fake = new FakeInvoker();

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.no-baseline", outcome.Refusal!.Code);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void NoObjectTimingBaselineAssessmentIsRefused()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        var fake = new FakeInvoker();

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.no-assessment", outcome.Refusal!.Code);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void ABaselineAssessmentRecordedWithoutACacheIsRefusedWithItsOwnCode()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: null);
        var fake = new FakeInvoker();

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.no-cache", outcome.Refusal!.Code);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void TheCurrentBaselineAssessmentIsQueriedByDefaultAndForwardingIsExact()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var baseline = CaptureBaseline(fwDataPath);
        var assessmentId = RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = Completing("group    key    count" + Environment.NewLine);
        string[] forwarded = ["--Group", "word And spaces", "-x", "--group", "word"];

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, forwarded, fake);

        Assert.True(outcome.Succeeded);
        var response = outcome.Value!;
        Assert.Equal(assessmentId, response.AssessmentId);
        Assert.Equal(baseline.FwDataPath, response.GrammarPath);
        Assert.Equal("cache.sqlite", response.CachePath);
        Assert.Equal("group    key    count" + Environment.NewLine, response.Text);
        Assert.Null(response.Rows);
        var seen = SeenStats(fake);
        Assert.Equal(baseline.FwDataPath, seen.GrammarPath);
        Assert.Equal("cache.sqlite", seen.CachePath);
        Assert.Equal(forwarded, seen.ForwardedArguments);
    }

    [Fact]
    public void JsonRowsAppendsFormatJsonlAndParsesEachLineAsAClonedElement()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var jsonl = "{\"group\":\"word\",\"count\":3}" + Environment.NewLine +
            Environment.NewLine + // a blank line must never become a row
            "{\"group\":\"beta\",\"count\":1}" + Environment.NewLine;
        var fake = Completing(jsonl);

        var outcome = Run(fwDataPath, null, StatsOutputKind.JsonRows, ["--group", "word"], fake);

        Assert.True(outcome.Succeeded);
        var response = outcome.Value!;
        Assert.Null(response.Text);
        Assert.Equal(2, response.Rows!.Count);
        Assert.Equal("word", response.Rows[0].GetProperty("group").GetString());
        Assert.Equal(1, response.Rows[1].GetProperty("count").GetInt32());
        Assert.Equal(["--group", "word", "--format", "jsonl"], SeenStats(fake).ForwardedArguments);
    }

    [Fact]
    public void JsonOutputWithAForwardedFormatIsRefusedRatherThanOverridden()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker();

        var outcome = Run(fwDataPath, null, StatsOutputKind.JsonRows, ["--format", "text"], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.format-conflict", outcome.Refusal!.Code);
        Assert.Empty(fake.Requests);
    }

    // The joined spelling is the same flag: missing it would append a second, contradictory --format.
    [Fact]
    public void JsonOutputWithAForwardedFormatWrittenWithAnEqualsSignIsAlsoRefused()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker();

        var outcome = Run(fwDataPath, null, StatsOutputKind.JsonRows, ["--format=text"], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.format-conflict", outcome.Refusal!.Code);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void AnInvalidProposalIdIsRefused()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        var fake = new FakeInvoker();

        var outcome = Run(fwDataPath, "not-a-canonical-id", StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.invalid-proposal-id", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, outcome.Refusal.Reason);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void AProposalWithNoTrialAssessmentIsRefused()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        var proposalId = CanonicalId.Mint("proposal/");
        var fake = new FakeInvoker();

        var outcome = Run(fwDataPath, proposalId.Value, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.no-assessment", outcome.Refusal!.Code);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void AProposalIdSelectsItsTrialAssessmentInsteadOfTheBaselineOne()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "baseline-cache.sqlite");
        var proposalId = CanonicalId.Mint("proposal/");
        SeedProposal(fwDataPath, proposalId);
        var trialAssessmentId = RecordAssessment(fwDataPath, proposalId, cachePath: "trial-cache.sqlite");
        var fake = Completing("trial stats" + Environment.NewLine);

        var outcome = Run(fwDataPath, proposalId.Value, StatsOutputKind.Text, [], fake);

        Assert.True(outcome.Succeeded);
        Assert.Equal(trialAssessmentId, outcome.Value!.AssessmentId);
        Assert.Equal("trial-cache.sqlite", outcome.Value.CachePath);
        Assert.Equal("trial-cache.sqlite", SeenStats(fake).CachePath);
    }

    [Fact]
    public void AnUnavailableParserIsRefusedRatherThanEscapingAsAnException()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker { Respond = _ => new PanGlossOutcome.Unavailable("no pangloss here") };

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.parser-unavailable", outcome.Refusal!.Code);
        Assert.Equal("no pangloss here", outcome.Refusal.Message);
    }

    [Fact]
    public void CancellationDuringTheQueryIsRefusedAsCancelled()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker { Respond = _ => new PanGlossOutcome.Cancelled() };

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.cancelled", outcome.Refusal!.Code);
    }

    [Fact]
    public void ARefusedStatsRunBecomesAParserRefusedRefusal_CarryingTheExitCode()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Refused(2, "stale cache", string.Empty, "pangloss stats exited 2:\nstale cache"),
        };

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.parser-refused", outcome.Refusal!.Code);
        Assert.Equal("2", outcome.Refusal.Facts["exitCode"]);
        Assert.Contains("stale cache", outcome.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATimedOutStatsRunBecomesATimedOutRefusal()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.TimedOut(TimeSpan.FromMinutes(10),
                "pangloss stats did not finish within 10 minutes and was stopped."),
        };

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.Equal("stats.timed-out", outcome.Refusal!.Code);
        Assert.Equal("10", outcome.Refusal.Facts["capMinutes"]);
    }

    private static CommandOutcome<StatsCommandResponse> Run(string fwDataPath, string? proposalId,
        StatsOutputKind output, IReadOnlyList<string> forwarded, IPanGlossInvoker invoker,
        CancellationToken cancellationToken = default) =>
        StatsCommand.Run(new StatsRequest(fwDataPath, proposalId, output, forwarded), invoker, cancellationToken);

    // Answers every stats request with the same rows; what the command sent is read back from Requests.
    private static FakeInvoker Completing(string standardOutput) => new()
    {
        Respond = _ => new PanGlossOutcome.Completed(standardOutput, string.Empty, TimeSpan.Zero),
    };

    private static PanGlossRequest.Stats SeenStats(FakeInvoker fake) =>
        Assert.IsType<PanGlossRequest.Stats>(Assert.Single(fake.Requests).Request);

    private BaselineCaptureResponse CaptureBaseline(string fwDataPath)
    {
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(outcome.Succeeded);
        return outcome.Value!;
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // ProposalId is a real foreign key; a Trial Assessment needs a Proposal row to point at.
    private static void SeedProposal(string fwDataPath, CanonicalId proposalId) =>
        new ProposalRepository(OpenDatabase(fwDataPath)).SaveRevision(new ProposalRevisionRecord(
            proposalId, "sha256:" + new string('2', 64), "{}", "proposed", null, null, null));

    private static string RecordAssessment(string fwDataPath, CanonicalId? proposalId, string? cachePath)
    {
        var repository = new AssessmentRepository(OpenDatabase(fwDataPath));
        var assessmentId = CanonicalId.Mint("assessment/").Value;
        repository.Record(new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: proposalId,
            ProposalIntentDigest: null,
            Assessor: "fake-assessor",
            Kind: AssessmentKind.ObjectTiming.ToStoredKind(),
            ScopeJson: "{}",
            ScopeDigest: "sha256:" + new string('0', 64),
            TokeniserName: "none",
            TokeniserVersion: "1",
            BaselineToken: "{}",
            Selection: Selection.Create("test", Array.Empty<string>()),
            OutcomeDigest: "sha256:" + new string('0', 64),
            SemanticDigest: "sha256:" + new string('0', 64),
            GrammarSourceSha256: "sha256:" + new string('0', 64),
            ModelFingerprint: "fp",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Words: Array.Empty<AssessedWord>(),
            CachePath: cachePath,
            CacheDigest: cachePath is null ? null : "sha256:" + new string('1', 64)));
        return assessmentId;
    }

    private static MotifDatabase OpenDatabase(string fwDataPath)
    {
        var project = new ProjectLocator(Path.GetFullPath(fwDataPath), Path.GetFileNameWithoutExtension(fwDataPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        return MotifDatabase.OpenOwned(databasePath, project, MotifSchema.CurrentSchema, new Version(1, 0));
    }
}
