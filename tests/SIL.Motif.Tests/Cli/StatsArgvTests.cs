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
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Drives the real <c>motif.exe</c> for <c>stats</c> against the real <c>FakePanGloss</c> executable: the
/// exact argv it records beside the grammar proves the whole path — <c>ParseArgs</c>'s new standalone
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
    public void NoBaselineRefusesWithItsOwnCode()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var result = Run($"stats \"{fwDataPath}\" --json");

        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal("stats.no-baseline", envelope.Code);
    }

    [Fact]
    public void ForwardingAfterTheDelimiterPreservesBoundariesOrderDuplicatesCasingAndLeadingDashes()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var (baseline, _) = CaptureBaselineAndSeedAssessment(fwDataPath, proposalId: null, "cache.sqlite");

        var result = Run(
            $"stats \"{fwDataPath}\" -- --Group \"word And spaces\" --group word -x --group word " +
            "-onedash-value");

        Assert.Equal(0, result.ExitCode);
        var argv = ReadArgv(baseline.FwDataPath);
        Assert.Equal(
            new[]
            {
                "stats", baseline.FwDataPath, "--cache", "cache.sqlite", "--Group", "word And spaces",
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
        CaptureBaselineAndSeedAssessment(fwDataPath, proposalId: null, "cache.sqlite");

        var result = Run($"stats \"{fwDataPath}\" --json");

        Assert.Equal(0, result.ExitCode);
        var response = ProjectionJson.Deserialize<StatsCommandResponse>(result.Output)!;
        Assert.Null(response.Text);
        Assert.NotNull(response.Rows);
        Assert.True(response.Rows!.Count > 0);
        Assert.Equal("word", response.Rows[0].GetProperty("group").GetString());
    }

    [Fact]
    public void AProposalSelectsTheTrialAssessmentsCacheRatherThanTheBaselines()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var (baseline, _) = CaptureBaselineAndSeedAssessment(fwDataPath, proposalId: null, "baseline-cache.sqlite");
        var proposalId = CanonicalId.Mint("proposal/");
        SeedProposal(fwDataPath, proposalId);
        SeedAssessment(fwDataPath, proposalId, "trial-cache.sqlite");

        var result = Run($"stats \"{fwDataPath}\" --proposal {proposalId.Value}");

        Assert.Equal(0, result.ExitCode);
        var argv = ReadArgv(baseline.FwDataPath);
        Assert.Equal(["stats", baseline.FwDataPath, "--cache", "trial-cache.sqlite"], argv);
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
        var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(project), project, MotifSchema.CurrentSchema, new Version(1, 0));
        new ProposalRepository(database).SaveRevision(new ProposalRevisionRecord(
            proposalId, "sha256:" + new string('2', 64), "{}", "proposed", null, null, null));
    }

    private static string SeedAssessment(string fwDataPath, CanonicalId? proposalId, string cachePath)
    {
        var project = new ProjectLocator(Path.GetFullPath(fwDataPath), Path.GetFileNameWithoutExtension(fwDataPath));
        var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(project), project, MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new AssessmentRepository(database);
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
            CacheDigest: "sha256:" + new string('1', 64)));
        return assessmentId;
    }

    private static string[] ReadArgv(string grammarPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(grammarPath))!;
        var argvPath = Path.Combine(directory, ArgvFileName);
        return JsonSerializer.Deserialize<string[]>(File.ReadAllText(argvPath))!;
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
