using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Generator;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Drives the real <c>motif.exe</c> for <c>stats</c> against the real <c>FakePanGloss</c> executable: the
/// exact argv recorded at a child-only destination proves the whole path — <c>ParseArgs</c>'s standalone
/// <c>--</c> delimiter, <see cref="SIL.Motif.Commands.Assess.StatsCommand"/>'s Baseline and Trial
/// resolution, and <see cref="SIL.Motif.Host.PanGloss.PanGlossInvoker"/>'s own forwarding — preserves
/// argument boundaries, order, duplicates, casing, and values that themselves begin with a dash.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class StatsArgvTests : IDisposable
{
    // Kept in step with FakePanGloss's own Program.ArgvFileName; the fake is launched, never referenced.
    private const string ArgvFileName = "_pangloss-argv.json";

    private readonly PristineProjectFixture _pristine;
    private readonly string _workerRoot =
        Path.Combine(Path.GetTempPath(), "motif-stats-argv-" + Guid.NewGuid().ToString("N"));

    public StatsArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_workerRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workerRoot, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void OmittingTheProjectIsAUsageFailureNamingTheVerb()
    {
        var result = Run("stats");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif stats <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonexistentProjectRefusesTheWayEveryOtherVerbDoes()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");

        var result = Run($"stats \"{missing}\" --json");

        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal("project.not-found", envelope.Code);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void NoAssessmentRefusesWithoutRequiringABaseline()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var result = Run($"stats \"{fwDataPath}\" --json");

        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal("stats.no-assessment", envelope.Code);
    }

    [Fact]
    public void ForwardingAfterTheDelimiterPreservesBoundariesOrderDuplicatesCasingAndLeadingDashes()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var (_, assessmentId) = CaptureBaselineAndSeedAssessment(fwDataPath, proposalId: null, "cache.sqlite");

        var result = Run(
            $"stats \"{fwDataPath}\" -- --Group \"word And spaces\" --group word -x --group word " +
            "-onedash-value");

        Assert.Equal(0, result.ExitCode);
        var argv = ReadArgv();
        AssertVerifiedReplayPaths(fwDataPath, assessmentId, argv);
        Assert.Equal(
            new[]
            {
                "stats", argv[1], "--cache", argv[3], "--Group", "word And spaces",
                "--group", "word", "-x", "--group", "word", "-onedash-value",
            },
            argv);
        // Duplicates and casing survive exactly; nothing here is deduplicated or normalized (design decision 5).
        Assert.Equal(3, argv.Count(a => a is "--group" or "--Group"));
        Assert.Equal(2, argv.Count(a => a == "word"));
    }

    [Fact]
    public void JsonModeAppendsFormatJsonlAndReturnsPanGlossRowsAsMotifRows()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var (_, assessmentId) = CaptureBaselineAndSeedAssessment(fwDataPath, proposalId: null, "cache.sqlite");

        var result = Run($"stats \"{fwDataPath}\" --json");

        Assert.Equal(0, result.ExitCode);
        var response = ProjectionJson.Deserialize<StatsCommandResponse>(result.Output)!;
        Assert.Null(response.Text);
        Assert.NotNull(response.Rows);
        Assert.True(response.Rows!.Count > 0);
        Assert.Equal("word", response.Rows[0].GetProperty("group").GetString());
        var argv = ReadArgv();
        AssertVerifiedReplayPaths(fwDataPath, assessmentId, argv);
        Assert.Equal(["--format", "jsonl"], argv[4..]);
        var record = ReadAssessment(fwDataPath, assessmentId);
        Assert.Equal(record.Invocation!.SourcePath, response.GrammarPath);
        Assert.Equal(record.CachePath, response.CachePath);
    }

    [Fact]
    public void AProposalSelectsTheTrialAssessmentsCacheRatherThanTheBaselines()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaselineAndSeedAssessment(fwDataPath, proposalId: null, "baseline-cache.sqlite");
        var proposalId = CanonicalId.Mint("proposal/");
        SeedProposal(fwDataPath, proposalId);
        var assessmentId = SeedAssessment(fwDataPath, proposalId, "trial-cache.sqlite");

        var result = Run($"stats \"{fwDataPath}\" --proposal {proposalId.Value}");

        Assert.Equal(0, result.ExitCode);
        var argv = ReadArgv();
        AssertVerifiedReplayPaths(fwDataPath, assessmentId, argv);
        Assert.Equal(["stats", argv[1], "--cache", argv[3]], argv);
        Assert.Equal("trial-cache.sqlite", Path.GetFileName(ReadAssessment(fwDataPath, assessmentId).CachePath));
    }

    private (BaselineCaptureResponse Baseline, string AssessmentId) CaptureBaselineAndSeedAssessment(
        string fwDataPath, CanonicalId? proposalId, string cachePath)
    {
        var managedRoot = Path.Combine(_workerRoot, "captures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(managedRoot);
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(outcome.Succeeded);
        var baseline = outcome.Value!;
        var assessmentId = SeedAssessment(fwDataPath, proposalId, cachePath);
        return (baseline, assessmentId);
    }

    // ProposalId is a real foreign key; a Trial Assessment needs a Proposal row to point at.
    private static void SeedProposal(string fwDataPath, CanonicalId proposalId)
    {
        var project = new ProjectLocator(Path.GetFullPath(fwDataPath), Path.GetFileNameWithoutExtension(fwDataPath));
        using var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(project), project, MotifSchema.CurrentSchema, new Version(1, 0));
        new ProposalRepository(database).SaveRevision(new ProposalRevisionRecord(
            proposalId, "sha256:" + new string('2', 64), "{}", "proposed", null, null, null));
    }

    private static string SeedAssessment(string fwDataPath, CanonicalId? proposalId, string cachePath)
    {
        var project = new ProjectLocator(Path.GetFullPath(fwDataPath), Path.GetFileNameWithoutExtension(fwDataPath));
        using var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(project), project, MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new AssessmentRepository(database);
        var assessmentId = CanonicalId.Mint("assessment/").Value;
        var run = Path.Combine(Path.GetDirectoryName(fwDataPath)!, "stats-evidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var source = Path.Combine(run, "source.fwdata");
        File.WriteAllText(source, "retained source " + assessmentId);
        cachePath = Path.Combine(run, cachePath);
        File.WriteAllText(cachePath, "retained cache " + assessmentId);
        var evidence = new BatchInvocationEvidence(Guid.NewGuid().ToString("N"), source,
            BatchInvocationEvidence.DigestFile(source), "sha256:executable", "words", "sha256:words",
            "rows", "sha256:rows", "stderr", "sha256:stderr", 1000, 200000, 1, true);
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
            CacheDigest: BatchInvocationEvidence.DigestFile(cachePath)) { Invocation = evidence });
        return assessmentId;
    }

    private string[] ReadArgv() =>
        JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(_workerRoot, ArgvFileName)))!;

    private static AssessmentRecord ReadAssessment(string fwDataPath, string assessmentId)
    {
        var project = new ProjectLocator(Path.GetFullPath(fwDataPath), Path.GetFileNameWithoutExtension(fwDataPath));
        using var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(project), project, MotifSchema.CurrentSchema, new Version(1, 0));
        return new AssessmentRepository(database).Get(assessmentId);
    }

    private static void AssertVerifiedReplayPaths(string fwDataPath, string assessmentId, string[] argv)
    {
        var record = ReadAssessment(fwDataPath, assessmentId);
        Assert.NotEqual(record.Invocation!.SourcePath, argv[1]);
        Assert.NotEqual(record.CachePath, argv[3]);
        Assert.False(File.Exists(argv[1]));
        Assert.False(File.Exists(argv[3]));
        Assert.Equal("retained source " + assessmentId, File.ReadAllText(record.Invocation.SourcePath));
        Assert.Equal("retained cache " + assessmentId, File.ReadAllText(record.CachePath!));
        Assert.Equal(record.Invocation.SourceBytesSha256, BatchInvocationEvidence.DigestFile(record.Invocation.SourcePath));
        Assert.Equal(record.CacheDigest, BatchInvocationEvidence.DigestFile(record.CachePath!));
    }
    private CliRun Run(string arguments)
    {
        var executable = Path.Combine(
            RepoPaths.FindRepoRoot(), "src", "SIL.Motif.Cli", "bin", "Debug", "net10.0", "motif.exe");
        var start = new ProcessStartInfo(executable)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        start.Environment["FAKE_PANGLOSS_ARGV_PATH"] = Path.Combine(_workerRoot, ArgvFileName);
        start.Environment[PanGlossExecutable.PathVariable] = FakeParser.ExecutablePath;
        using var process = Process.Start(start)!;
        // Both pipes drain concurrently: a sequential read deadlocks past the pipe buffer.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.WaitForExit(60000), "The CLI did not exit within its bound.");
        return new CliRun(process.ExitCode, output, error);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
