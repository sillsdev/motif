using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Runs the Parsimony pipeline once over a seeded project to get a real facts file and evidence file, then opens
/// sessions over copies of the facts whose lexicon a test replaces. The lexicon checks read only the facts, so one
/// pipeline run serves every fixture of a test.
/// </summary>
internal sealed class LexiconBundleHarness : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "plx", Guid.NewGuid().ToString("N")[..8]);
    private readonly PristineProjectFixture _pristine;
    private readonly ProjectLocator _project;
    private readonly MotifDatabase _database;
    private readonly string _projectKey;
    private readonly BaselineToken _token;
    private ParsimonyBundleRecord? _bundle;
    private int _copies;

    public LexiconBundleHarness(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
        _project = new ProjectLocator(Path.Combine(_root, "live", NewLangProjFixture.ProjectName + ".fwdata"),
            "live-project-identity");
        Directory.CreateDirectory(Path.GetDirectoryName(_project.FullFwDataPath)!);
        File.WriteAllText(_project.FullFwDataPath, "live project bytes stay untouched");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "motif.db"), _project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _projectKey = ProjectWorkspaceKey.Compute(_project);
        _token = new BaselineToken("live-project-identity", "sha256:" + new string('1', 64), "1",
            "2026-10-05T00:00:00Z", "sha256:" + new string('2', 64));
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Opens a session over a copy of the facts whose lexicon is the one the seed names.</summary>
    public async Task<ParsimonyQuerySession> OpenAsync(Action<LexiconSeed> author)
    {
        var bundle = _bundle ??= await BuildBundleAsync();
        var seed = new LexiconSeed();
        author(seed);
        var facts = Path.Combine(_root, $"facts-{_copies++}.sqlite");
        File.Copy(bundle.GrammarFactsPath, facts);
        seed.WriteTo(facts);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null, bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(facts)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        return new ParsimonyQuerySession(facts, bundle.EvidencePath, inputs);
    }

    private async Task<ParsimonyBundleRecord> BuildBundleAsync()
    {
        var baselines = new BaselineRepository(_database);
        var template = PublishedBaselineTemplate<SeededText>.Get("seeded-text", _pristine,
            cache => SeededProject.SeedText(cache, _pristine.Seed));
        var publishedRoot = Path.Combine(_root, "published-baseline");
        var fwDataPath = template.CopyTo(publishedRoot);
        baselines.Record(_projectKey, new BaselinePublication(publishedRoot, fwDataPath, _token),
            DateTimeOffset.Parse("2026-10-05T00:00:00Z"), DateTimeOffset.Parse("2026-10-05T00:00:00Z"),
            template.Projection, template.Summary);
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser"));
        FakeParser.WriteFactsFixture(parserPath, new { prohibitions = Array.Empty<object>() });
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var jobs = new JobRepository(_database);
        var input = JsonSerializer.Serialize(new ParsimonyJobInput("P-msa-no-category",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null)),
            SIL.Motif.Contract.MotifJson.CreateOptions());
        var job = jobs.Create(Guid.NewGuid().ToString("N"), _projectKey, ParsimonyJobHandler.JobKind, input,
            "2026-10-05T00:00:00Z");
        var claimed = new JobClaims(_database).Claim(_projectKey, "lexicon-checks-test",
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5));
        Assert.NotNull(claimed);
        var handler = RetainedFacts.Handler(_database, baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(new ClaimedJob(jobs, claimed), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        return new ParsimonyBundleRepository(_database).Get(result.RootElement.GetProperty("bundleId").GetString()!)!;
    }

    private static string Sha256Of(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
