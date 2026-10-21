using System.Linq;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Covers a job being pinned to the parser its enqueuer expects, and a Worker claiming only the jobs whose
/// pin its own parser matches.
/// </summary>
public sealed class JobParserPinTests : IDisposable
{
    private const string Project = "project-key";
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-pin-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly string _oldParser;
    private readonly string _newParser;

    public JobParserPinTests()
    {
        Directory.CreateDirectory(_root);
        var locator = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _oldParser = FakeParser.CopyReportingVersion(Path.Combine(_root, "old"), "0.8.6");
        _newParser = FakeParser.CopyReportingVersion(Path.Combine(_root, "new"), "0.8.8");
        ParserExecutableIdentity.ReadVersion(_oldParser);
        ParserExecutableIdentity.ReadVersion(_newParser);
    }

    [Fact]
    public void AParserThatCannotReportItsVersionStillPinsByItsBytes()
    {
        var broken = Path.Combine(_root, "not-a-parser");
        File.WriteAllText(broken, "not an executable");

        var identity = ParserIdentity.Read(broken)!;

        Assert.Equal("unknown", identity.Version);
        Assert.Equal(identity.Sha256, identity.Key);
        Assert.Equal(identity.Key, JobParserPin.KeyOf(JobParserPin.Stamp("{}", broken)));
    }

    [Fact]
    public void TwoParsersOfDifferentVersionsHaveDifferentIdentities()
    {
        var old = ParserIdentity.Read(_oldParser)!;
        var current = ParserIdentity.Read(_newParser)!;

        Assert.Equal("0.8.6", old.Version);
        Assert.Equal("0.8.8", current.Version);
        Assert.NotEqual(old.Key, current.Key);
        Assert.Equal(_newParser, current.Path);
    }

    [Fact]
    public void TwoBuildsReportingOneVersionAreTwoParsersWhenTheirBytesDiffer()
    {
        var rebuilt = FakeParser.CopyReportingVersion(Path.Combine(_root, "rebuilt"), "0.8.8");
        File.AppendAllText(rebuilt, " ");
        ParserExecutableIdentity.ReadVersion(rebuilt);

        var original = ParserIdentity.Read(_newParser)!;
        var changed = ParserIdentity.Read(rebuilt)!;

        Assert.Equal(original.Version, changed.Version);
        Assert.NotEqual(original.Sha256, changed.Sha256);
        Assert.NotEqual(original.Key, changed.Key);
    }

    [Fact]
    public void AWorkerIgnoresAJobPinnedToAnotherParserAndLeavesItQueued()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-b", Project, "trial", JobParserPin.Stamp("{}", _newParser), Now());
        var workerA = new JobClaims(_database, parserKey: ParserIdentity.KeyOf(_oldParser));
        var workerB = new JobClaims(_database, parserKey: ParserIdentity.KeyOf(_newParser));

        Assert.Null(workerA.Claim(Project, "runner-a", Now(), Lease()));
        Assert.Null(workerA.PeekHead(Project, Now()));
        Assert.Equal(JobStatus.Queued, jobs.Get("job-b")!.Status);

        var claimed = workerB.Claim(Project, "runner-b", Now(), Lease());
        Assert.Equal("job-b", claimed!.JobId);
        Assert.Equal("runner-b", claimed.OwnerId);
    }

    [Fact]
    public void AJobPinnedToAnotherParserDoesNotBlockTheJobsBehindIt()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-b", Project, "trial", JobParserPin.Stamp("{}", _newParser), Now());
        jobs.Create("job-a", Project, "trial", JobParserPin.Stamp("{}", _oldParser), Now());

        var claimed = new JobClaims(_database, parserKey: ParserIdentity.KeyOf(_oldParser))
            .Claim(Project, "runner-a", Now(), Lease());

        Assert.Equal("job-a", claimed!.JobId);
    }

    [Fact]
    public void ALegacyJobWithNoParserIdentityIsClaimedByAnyWorker()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("legacy-1", Project, "trial", "{\"proposalJson\":\"{}\"}", Now());
        jobs.Create("legacy-2", Project, "trial", "{}", Now());

        var withOld = new JobClaims(_database, parserKey: ParserIdentity.KeyOf(_oldParser))
            .Claim(Project, "runner-a", Now(), Lease());
        var withNone = new JobClaims(_database, parserKey: null).Claim(Project, "runner-b", Now(), Lease());

        Assert.Equal("legacy-1", withOld!.JobId);
        Assert.Equal("legacy-2", withNone!.JobId);
    }

    [Fact]
    public void AWorkerWithNoParserClaimsNoPinnedJob()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("pinned", Project, "trial", JobParserPin.Stamp("{}", _newParser), Now());

        Assert.Null(new JobClaims(_database).Claim(Project, "runner", Now(), Lease()));
    }

    [Fact]
    public void AnEnqueuerWithNoParserRecordsNoPin()
    {
        Assert.Equal("{}", JobParserPin.Stamp("{}", null));
        Assert.Equal("{}", JobParserPin.Stamp("{}", Path.Combine(_root, "missing")));
        Assert.Null(JobParserPin.KeyOf(JobParserPin.Stamp("{}", null)));
        Assert.Equal(ParserIdentity.KeyOf(_newParser), JobParserPin.KeyOf(JobParserPin.Stamp("{}", _newParser)));
    }

    private static string Now() => JobTimestamp.FormatUtc(DateTimeOffset.UtcNow);

    private static TimeSpan Lease() => TimeSpan.FromMinutes(5);

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }
}
