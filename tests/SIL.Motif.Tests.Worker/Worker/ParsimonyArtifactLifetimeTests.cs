using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class ParsimonyArtifactLifetimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyArtifactLifetimeTests),
        Guid.NewGuid().ToString("N"));
    private readonly ProjectLocator _project;
    private readonly string _projectKey;
    private readonly string _workerRoot;
    private readonly string _baselineRoot;
    private readonly MotifDatabase _database;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly EvidenceArtifactRepository _artifacts;
    private readonly EvidenceArtifactPublisher _publisher;
    private readonly BaselineToken _token;

    public ParsimonyArtifactLifetimeTests()
    {
        Directory.CreateDirectory(_root);
        _project = new ProjectLocator(Path.Combine(_root, "project", "Project.fwdata"), "project-identity");
        Directory.CreateDirectory(Path.GetDirectoryName(_project.FullFwDataPath)!);
        File.WriteAllText(_project.FullFwDataPath, "live project");
        _projectKey = ProjectWorkspaceKey.Compute(_project);
        _workerRoot = Path.Combine(_root, "worker");
        _baselineRoot = Path.Combine(_workerRoot, ProjectWorkspaceKey.StorageSegment(_projectKey), "baseline", "source");
        Directory.CreateDirectory(_baselineRoot);
        File.WriteAllText(Path.Combine(_baselineRoot, "Project.fwdata"), "saved Baseline");
        _token = new BaselineToken("project-identity", "sha256:" + new string('1', 64), "1",
            "2026-10-06T11:00:00Z", "sha256:" + new string('a', 64));
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "motif.db"), _project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        InsertBaseline();
        _artifacts = new EvidenceArtifactRepository(_database, _clock);
        _publisher = new EvidenceArtifactPublisher(_workerRoot, _projectKey);
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void CandidateIdentityDoesNotAddAnotherPathComponentToTheEvidenceBundle()
    {
        var materialKey = Hash("candidate-material");
        var candidateIdentity = "sha256:" + Hash("candidate-identity");
        var modelFingerprint = "sha256:" + Hash("model");
        var baselinePath = _publisher.StagingDirectory("bundle-path", _token, modelFingerprint, materialKey);
        var candidatePath = _publisher.StagingDirectory("bundle-path", _token, modelFingerprint, materialKey,
            "candidate", candidateIdentity);

        Assert.NotEqual(baselinePath, candidatePath);
        Assert.Equal(baselinePath.Length, candidatePath.Length);
    }

    [Fact]
    public void EvidenceBundlePathsStayBelowWindowsMaxPathFromADeepWorkerRoot()
    {
        var prefix = Path.Combine(Path.GetTempPath(), "p");
        const int workerRootLength = 140;
        var workerRoot = Path.Combine(prefix, new string('w', workerRootLength - prefix.Length - 1));
        Assert.Equal(workerRootLength, workerRoot.Length);
        var publisher = new EvidenceArtifactPublisher(workerRoot, _projectKey);
        var bundleId = "parsimony-bundle-" + Guid.NewGuid().ToString("N");
        var modelFingerprint = "sha256:" + Hash("path-budget-model");
        var materialKey = Hash("path-budget-material");
        var staging = publisher.StagingDirectory(bundleId, _token, modelFingerprint, materialKey);
        var final = publisher.FinalDirectory(bundleId, _token, modelFingerprint, materialKey);
        var paths = new[]
        {
            Path.Combine(staging, "grammar-facts.sqlite"),
            Path.Combine(staging, "evidence.sqlite"),
            Path.Combine(final, "grammar-facts.sqlite"),
            Path.Combine(final, "evidence.sqlite"),
        };

        // SQLite's rollback journal is the longest name a write creates beside each file.
        Assert.All(paths, path => Assert.True((path + "-journal").Length <= 259,
            $"The Parsimony artifact path is {path.Length} characters: {path}"));
    }

    [Fact]
    public void StagingAndRenameFailuresNeverCreateAVisibleBundle()
    {
        var beforeClose = new EvidenceArtifactPublisher(_workerRoot, _projectKey,
            new FailureHook(EvidencePublicationCheckpoint.BeforeFileClose));
        var closeStage = CreateStagingFiles(beforeClose, "close-failure", "material-close");
        Assert.Throws<IOException>(() => beforeClose.FlushAndClose(closeStage));
        beforeClose.Discard(closeStage);
        Assert.Null(_artifacts.Get("bundle-close-failure"));

        var beforeRename = new EvidenceArtifactPublisher(_workerRoot, _projectKey,
            new FailureHook(EvidencePublicationCheckpoint.BeforeRename));
        var renameStage = CreateStagingFiles(beforeRename, "rename-failure", "material-rename");
        beforeRename.FlushAndClose(renameStage);
        Assert.Throws<IOException>(() => beforeRename.Publish(renameStage, "bundle-rename-failure"));
        Assert.Null(_artifacts.Get("bundle-rename-failure"));
        beforeRename.Discard(renameStage);
    }

    [Fact]
    public void CancellationBeforeStoreCommitPublishesNothingAndCancellationAfterCommitKeepsTheReport()
    {
        var before = CreateFiles(_publisher, "cancel-before", "material-cancel-before");
        var cancelled = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => _artifacts.Publish(before.Bundle, before.Report,
            cancelled.Token, beforeCommit: cancelled.Cancel));
        Assert.Null(_artifacts.Get(before.Bundle.BundleId));
        Assert.Null(new ReportRepository(_database).Get(before.Report.ReportId));
        _publisher.Discard(before.FinalDirectory);

        var after = CreateFiles(_publisher, "cancel-after", "material-cancel-after");
        var cancelAfterCommit = new CancellationTokenSource();
        _artifacts.Publish(after.Bundle, after.Report, cancelAfterCommit.Token,
            afterCommit: cancelAfterCommit.Cancel);
        Assert.True(cancelAfterCommit.IsCancellationRequested);
        Assert.NotNull(_artifacts.Get(after.Bundle.BundleId));
        Assert.Equal("stored report", new ReportRepository(_database).Get(after.Report.ReportId)!.RenderedText);
    }

    [Fact]
    public void DuplicateBuildsUseFreshDirectoriesAndDoNotReplaceEitherPublishedPair()
    {
        var first = CreateFiles(_publisher, "duplicate-one", "same-material");
        var second = CreateFiles(_publisher, "duplicate-two", "same-material");
        _artifacts.Publish(first.Bundle, first.Report);
        _artifacts.Publish(second.Bundle, second.Report);

        Assert.NotEqual(first.Bundle.EvidenceDirectory, second.Bundle.EvidenceDirectory);
        Assert.Equal(first.Bundle.GrammarFactsSha256, HashFile(first.Bundle.GrammarFactsPath));
        Assert.Equal(second.Bundle.GrammarFactsSha256, HashFile(second.Bundle.GrammarFactsPath));
        Assert.NotNull(_artifacts.Get(first.Bundle.BundleId));
        Assert.NotNull(_artifacts.Get(second.Bundle.BundleId));
    }

    [Fact]
    public void ReportCheckRunJobAndReaderPinsAreVisibleAndAStoredReportSurvivesMissingFiles()
    {
        var build = Publish("pin-source");
        _artifacts.AddReference(build.Bundle.BundleId, "check-run", "check-run/1");
        _artifacts.AddReference(build.Bundle.BundleId, "job", "job/1");
        using var reader = _artifacts.AcquireReaderLease(build.Bundle.BundleId, "test-reader");

        Assert.Contains("report:" + build.Report.ReportId, _artifacts.GetPinReasons(build.Bundle.BundleId));
        Assert.Contains("check-run:check-run/1", _artifacts.GetPinReasons(build.Bundle.BundleId));
        Assert.Contains("job:job/1", _artifacts.GetPinReasons(build.Bundle.BundleId));
        Assert.False(_artifacts.TryMarkDeleting(build.Bundle.BundleId, _clock.UtcNow));

        File.Delete(build.Bundle.EvidencePath);
        _artifacts.MarkUnavailable(build.Bundle.BundleId, ParsimonyBundleFault.Missing, "the evidence file is missing");
        Assert.Throws<InvalidDataException>(() => _artifacts.AcquireReaderLease(build.Bundle.BundleId, "late-reader"));
        Assert.Equal("stored report", new ReportRepository(_database).Get(build.Report.ReportId)!.RenderedText);
    }

    [Fact]
    public void BusyFaultIsRefusedByTheRepositoryAndLeavesTheBundleAvailable()
    {
        var build = Publish("busy-refused");

        Assert.Throws<ArgumentException>(() => _artifacts.MarkUnavailable(build.Bundle.BundleId,
            ParsimonyBundleFault.Busy, "held open by another process"));
        Assert.Equal("available", _artifacts.Get(build.Bundle.BundleId)!.State);
    }

    [Fact]
    public void AnAbsentBundlePathIsMissingWhateverExceptionSurfaced()
    {
        var absent = new[] { Path.Combine(_root, "absent", "grammar-facts.sqlite") };

        Assert.Equal(ParsimonyBundleFault.Missing, ParsimonyBundleFaults.Classify(new IOException("sharing"), absent));
        Assert.Equal(ParsimonyBundleFault.Missing,
            ParsimonyBundleFaults.Classify(new InvalidDataException("digest"), absent));
        Assert.Equal(ParsimonyBundleFault.Missing,
            ParsimonyBundleFaults.Classify(new FileNotFoundException("gone"), absent));
    }

    [Fact]
    public void ADigestOrIdentityMismatchAndSqliteCorruptionAreCorrupt()
    {
        var present = PresentFile();

        Assert.Equal(ParsimonyBundleFault.Corrupt, ParsimonyBundleFaults.Classify(new InvalidDataException("digest"), present));
        Assert.Equal(ParsimonyBundleFault.Corrupt,
            ParsimonyBundleFaults.Classify(new SqliteException("corrupt", 11), present));
        Assert.Equal(ParsimonyBundleFault.Corrupt,
            ParsimonyBundleFaults.Classify(new SqliteException("not a database", 26), present));
    }

    [Fact]
    public void AnIOErrorOrASqliteLockOnAPresentFileIsBusy()
    {
        var present = PresentFile();

        Assert.Equal(ParsimonyBundleFault.Busy, ParsimonyBundleFaults.Classify(new IOException("sharing"), present));
        foreach (var code in new[] { 5, 6, 10, 14 })
        {
            Assert.Equal(ParsimonyBundleFault.Busy,
                ParsimonyBundleFaults.Classify(new SqliteException("locked", code), present));
        }
        Assert.Equal(ParsimonyBundleFault.Busy,
            ParsimonyBundleFaults.Classify(new UnauthorizedAccessException("delete pending"), present));
    }

    [Fact]
    public void AnUnexpectedExceptionTypeIsUnclassifiedAndMarksNothing()
    {
        var present = PresentFile();

        Assert.Null(ParsimonyBundleFaults.Classify(new NotSupportedException("unexpected"), present));
        Assert.Null(ParsimonyBundleFaults.Classify(new InvalidOperationException("unexpected"), present));
        Assert.Null(ParsimonyBundleFaults.Classify(new SqliteException("other", 1), present));
    }

    [Fact]
    public void Sha256HexHashesAStreamInBoundedReads()
    {
        var path = Path.Combine(_root, "large-facts.sqlite");
        var bytes = new byte[1024 * 1024];
        Random.Shared.NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        using var stream = new ReadRecordingStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ParsimonyQuerySession.Sha256Hex(stream));
        Assert.True(stream.LargestRead <= 64 * 1024, $"One read asked for {stream.LargestRead} bytes.");
    }

    [Fact]
    public void AChangedFactsFileIsRefusedAtOpenAndClassifiedCorrupt()
    {
        var build = Publish("changed-facts");
        var inputs = InputsFor(build.Bundle);
        File.WriteAllText(build.Bundle.GrammarFactsPath, "facts changed after publication");

        var exception = Assert.Throws<InvalidDataException>(() => new ParsimonyQuerySession(
            build.Bundle.GrammarFactsPath, build.Bundle.EvidencePath, inputs));
        Assert.Equal(ParsimonyBundleFault.Corrupt, ParsimonyBundleFaults.Classify(exception, BundlePaths(build.Bundle)));
    }

    [Fact]
    public void AFactsFileHeldExclusivelyByAnotherProcessOpensAsBusy()
    {
        var build = Publish("held-facts");
        var inputs = InputsFor(build.Bundle);
        using var exclusive = new FileStream(build.Bundle.GrammarFactsPath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.None);

        var exception = Assert.ThrowsAny<Exception>(() => new ParsimonyQuerySession(
            build.Bundle.GrammarFactsPath, build.Bundle.EvidencePath, inputs));
        Assert.Equal(ParsimonyBundleFault.Busy, ParsimonyBundleFaults.Classify(exception, BundlePaths(build.Bundle)));
        Assert.Equal("available", _artifacts.Get(build.Bundle.BundleId)!.State);
    }

    private ParsimonyReportInputs InputsFor(ParsimonyBundleRecord bundle) => new(bundle.BundleId, _token,
        "baseline", null, bundle.ModelFingerprint,
        new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, HashFile(bundle.GrammarFactsPath)),
        new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, HashFile(bundle.EvidencePath)),
        null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);

    private static string[] BundlePaths(ParsimonyBundleRecord bundle) =>
        [bundle.EvidenceDirectory, bundle.GrammarFactsPath, bundle.EvidencePath];

    private sealed class ReadRecordingStream(Stream inner) : Stream
    {
        public int LargestRead { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            LargestRead = Math.Max(LargestRead, count);
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            LargestRead = Math.Max(LargestRead, buffer.Length);
            return inner.Read(buffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private string[] PresentFile()
    {
        var path = Path.Combine(_root, "present.sqlite");
        File.WriteAllText(path, "present");
        return [path];
    }

    [Fact]
    public void ReaderLeaseRenewsWhileLiveAndCannotBeRenewedAfterItsExpiry()
    {
        var old = Publish("lease-old");
        var current = Publish("lease-current");
        Assert.True(_artifacts.DeleteReport(old.Report.ReportId));
        Assert.True(_artifacts.DeleteReport(current.Report.ReportId));
        using var lease = _artifacts.AcquireReaderLease(old.Bundle.BundleId, "renewing-reader");

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(lease.Renew());
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Contains(_artifacts.GetPinReasons(old.Bundle.BundleId), reason => reason.StartsWith("reader:",
            StringComparison.Ordinal));
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.False(lease.Renew());
        Assert.DoesNotContain(_artifacts.GetPinReasons(old.Bundle.BundleId), reason => reason.StartsWith("reader:",
            StringComparison.Ordinal));
        Assert.True(_artifacts.TryMarkDeleting(old.Bundle.BundleId, _clock.UtcNow));
    }

    [Fact]
    public void RetentionKeepsCurrentMaterialAndNewestTwentyUnpinnedBundles()
    {
        var builds = new List<(ParsimonyBundleRecord Bundle, ReportRecord Report)>();
        for (var index = 0; index < 22; index++)
        {
            builds.Add(Publish("count-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
        foreach (var build in builds) Assert.True(_artifacts.DeleteReport(build.Report.ReportId));

        var cleaner = new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(_workerRoot), _clock);
        var result = cleaner.Clean(_projectKey);

        Assert.Single(result.DeletedBundleIds);
        Assert.Equal(builds[0].Bundle.BundleId, result.DeletedBundleIds[0]);
        Assert.False(Directory.Exists(builds[0].Bundle.EvidenceDirectory));
        Assert.True(Directory.Exists(builds[^1].Bundle.EvidenceDirectory));
        Assert.True(result.RetainedPinnedBytes > 0);
        Assert.Equal(1, result.PinReasonCounts["current-baseline-material"]);
    }

    [Fact]
    public async Task CleanupWaitsForACompetingReaderLeaseThenRetriesDeletion()
    {
        var old = Publish("reader-old");
        var current = Publish("reader-current");
        Assert.True(_artifacts.DeleteReport(old.Report.ReportId));
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var readerTask = Task.Run(() =>
        {
            using var lease = _artifacts.AcquireReaderLease(old.Bundle.BundleId, "barrier-reader");
            using var facts = new FileStream(old.Bundle.GrammarFactsPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            entered.Set();
            release.Wait();
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var cleaner = new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(_workerRoot),
            _clock, retainedUnpinnedCount: 0);

        var whileOpen = cleaner.Clean(_projectKey);
        Assert.Empty(whileOpen.DeletedBundleIds);
        Assert.True(File.Exists(old.Bundle.GrammarFactsPath));
        release.Set();
        await readerTask;

        var afterClose = cleaner.Clean(_projectKey);
        Assert.Contains(old.Bundle.BundleId, afterClose.DeletedBundleIds);
        Assert.True(Directory.Exists(current.Bundle.EvidenceDirectory));
    }

    [Fact]
    public void FailedDeletionStaysRegisteredForRetryAndReparseEntriesAreRefused()
    {
        var old = Publish("delete-retry-old");
        var current = Publish("delete-retry-current");
        Assert.True(_artifacts.DeleteReport(old.Report.ReportId));
        var fileSystem = new FailFirstDeleteFileSystem();
        var cleaner = new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(_workerRoot),
            _clock, fileSystem, retainedUnpinnedCount: 0);

        var failed = cleaner.Clean(_projectKey);
        Assert.Single(failed.Failures);
        Assert.Equal("deleting", _artifacts.Get(old.Bundle.BundleId)!.State);
        var retried = cleaner.Clean(_projectKey);
        Assert.Contains(old.Bundle.BundleId, retried.DeletedBundleIds);

        var reparse = Publish("reparse-old");
        var newer = Publish("reparse-current");
        Assert.True(_artifacts.DeleteReport(reparse.Report.ReportId));
        var link = Path.Combine(reparse.Bundle.EvidenceDirectory, "outside-link");
        File.WriteAllText(link, "do not follow the reparse entry");
        var reparseCleaner = new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(_workerRoot),
            _clock, new ReparseFileSystem(link), retainedUnpinnedCount: 0);
        var refused = reparseCleaner.Clean(_projectKey);
        Assert.Contains(refused.Failures, failure => failure.Path == reparse.Bundle.EvidenceDirectory);
        Assert.Equal("do not follow the reparse entry", File.ReadAllText(link));
        Assert.True(Directory.Exists(newer.Bundle.EvidenceDirectory));
    }

    [Fact]
    public void ExpiredBuildClaimsRecoverOnlyAfterTheJobLeaseEndsAndRetainedBundlesPinTheirSource()
    {
        _ = new JobRepository(_database, _clock).Create("writer-job", _projectKey,
            ParsimonyJobHandler.JobKind, "{}", JobTimestamp.FormatUtc(_clock.UtcNow));
        var claimed = new JobClaims(_database, _clock).Claim(_projectKey, "writer", JobTimestamp.FormatUtc(_clock.UtcNow),
            TimeSpan.FromMinutes(1))!;
        Assert.Equal(JobStatus.Running, claimed.Status);
        var publisher = new EvidenceArtifactPublisher(_workerRoot, _projectKey);
        var working = publisher.CreateWorkingDirectory("orphan-work");
        var staging = publisher.CreateStagingDirectory("orphan", _token, "sha256:" + Hash("model"),
            Hash("orphan-material"));
        var final = publisher.FinalDirectory("orphan", _token, "sha256:" + Hash("model"),
            Hash("orphan-material"));
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(final);
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim("orphan", _projectKey, claimed.JobId,
            claimed.ClaimToken!, _baselineRoot, working, staging, final, JobTimestamp.FormatUtc(_clock.UtcNow)));
        _artifacts.SetBuildClaimPaths("orphan", staging, final);

        Assert.Empty(_artifacts.ListExpiredBuildClaims(_projectKey));
        Assert.True((_artifacts.GetPinSources(_baselineRoot) & BaselinePinSources.ParsimonyJob) != 0);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var recovery = new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(_workerRoot), _clock);
        var cleanup = recovery.RecoverExpiredBuilds(_projectKey, publisher);

        Assert.Empty(cleanup.Failures);
        Assert.False(Directory.Exists(working));
        Assert.False(Directory.Exists(staging));
        Assert.False(Directory.Exists(final));
        Assert.Empty(_artifacts.ListExpiredBuildClaims(_projectKey));
    }

    [Fact]
    public void AnExpiredClaimWhoseJobWasPurgedIsRecovered()
    {
        var purged = StartLiveJob("purged-writer");
        var working = Path.Combine(WorkRoot, "bundle-purged");
        Directory.CreateDirectory(working);
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim("bundle-purged", _projectKey, purged.JobId,
            purged.ClaimToken!, _baselineRoot, working));
        using (var connection = _database.OpenConnection())
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM Jobs WHERE JobId=$job;";
            delete.Parameters.AddWithValue("$job", purged.JobId);
            delete.ExecuteNonQuery();
        }

        Assert.DoesNotContain(Path.GetFullPath(working), _artifacts.ListLiveBuildClaimWorkingDirectories(_projectKey));
        Assert.Contains(_artifacts.ListExpiredBuildClaims(_projectKey), claim => claim.BundleId == "bundle-purged");
    }

    [Fact]
    public void StartupRemovesWorkLeftAfterACommittedPublication()
    {
        var jobs = new JobRepository(_database, _clock);
        var claims = new JobClaims(_database, _clock);
        var now = JobTimestamp.FormatUtc(_clock.UtcNow);
        _ = jobs.Create("live-writer", _projectKey, ParsimonyJobHandler.JobKind, "{}", now);
        var live = claims.Claim(_projectKey, "live-writer", now, TimeSpan.FromMinutes(10))!;
        var liveWork = _publisher.CreateWorkingDirectory("bundle-live-owned");
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim("bundle-live-owned", _projectKey, live.JobId,
            live.ClaimToken!, _baselineRoot, liveWork));

        _ = jobs.Create("publishing-writer", _projectKey, ParsimonyJobHandler.JobKind, "{}", now);
        var publishing = claims.Claim(_projectKey, "publishing-writer", now, TimeSpan.FromMinutes(10))!;
        var orphanWork = _publisher.CreateWorkingDirectory("bundle-left-behind");
        File.WriteAllText(Path.Combine(orphanWork, "evidence.sqlite"), "work copy");
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim("bundle-left-behind", _projectKey, publishing.JobId,
            publishing.ClaimToken!, _baselineRoot, orphanWork));
        var published = Publish("left-behind");
        Assert.True(Directory.Exists(orphanWork));

        var recovery = new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(_workerRoot), _clock);
        var cleanup = recovery.RecoverExpiredBuilds(_projectKey, _publisher);

        Assert.Empty(cleanup.Failures);
        Assert.False(Directory.Exists(orphanWork));
        Assert.True(Directory.Exists(liveWork));
        Assert.Equal("available", _artifacts.Get(published.Bundle.BundleId)!.State);
        Assert.Equal(published.Bundle.GrammarFactsSha256, HashFile(published.Bundle.GrammarFactsPath));
        Assert.Equal(published.Bundle.EvidenceSha256, HashFile(published.Bundle.EvidencePath));
        Assert.Equal("stored report", new ReportRepository(_database).Get(published.Report.ReportId)!.RenderedText);
    }

    [Fact]
    public void WorkDirectoryCreatedAfterTheListingIsNotSweptWithItsClaim()
    {
        var live = StartLiveJob("listing-writer");
        var late = Path.Combine(WorkRoot, "bundle-late");
        var fired = false;
        var hooked = new EvidenceArtifactPublisher(_workerRoot, _projectKey, new CheckpointHook(
            EvidencePublicationCheckpoint.AfterWorkListing, _ =>
            {
                fired = true;
                _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim("bundle-late", _projectKey, live.JobId,
                    live.ClaimToken!, _baselineRoot, late));
                Directory.CreateDirectory(late);
            }));
        Directory.CreateDirectory(WorkRoot);

        var cleanup = Recover(hooked);

        Assert.True(fired);
        Assert.Empty(cleanup.Failures);
        Assert.True(Directory.Exists(late));
    }

    [Fact]
    public void ClaimRegisteredAfterListingIsRecheckedBeforeDeletion()
    {
        var live = StartLiveJob("delete-writer");
        var raced = Path.Combine(WorkRoot, "bundle-raced");
        Directory.CreateDirectory(raced);
        File.WriteAllText(Path.Combine(raced, "evidence.sqlite"), "work copy");
        var fired = false;
        var hooked = new EvidenceArtifactPublisher(_workerRoot, _projectKey, new CheckpointHook(
            EvidencePublicationCheckpoint.BeforeWorkDelete, path =>
            {
                if (!StringComparer.Ordinal.Equals(path, raced)) return;
                fired = true;
                _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim("bundle-raced", _projectKey, live.JobId,
                    live.ClaimToken!, _baselineRoot, raced));
            }));

        var cleanup = Recover(hooked);

        Assert.True(fired);
        Assert.Empty(cleanup.Failures);
        Assert.True(File.Exists(Path.Combine(raced, "evidence.sqlite")));
    }

    [RequiresSymbolicLinkFact]
    public void WorkRootThatIsALinkIsReportedAndNothingBehindItIsDeleted()
    {
        var outside = CreateOutsideDirectory("outside-root");
        Directory.CreateSymbolicLink(WorkRoot, outside);

        var cleanup = Recover(_publisher);

        Assert.Contains(cleanup.Failures, failure => StringComparer.Ordinal.Equals(failure.Path, WorkRoot));
        Assert.Empty(cleanup.DeletedPaths);
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
        Assert.NotNull(new DirectoryInfo(WorkRoot).LinkTarget);
    }

    [RequiresSymbolicLinkFact]
    public void WorkEntryLinkedOutsideTheRootIsRefusedAndTheTargetSurvives()
    {
        var outside = CreateOutsideDirectory("outside-entry");
        Directory.CreateDirectory(WorkRoot);
        var link = Path.Combine(WorkRoot, "linked");
        Directory.CreateSymbolicLink(link, outside);

        var cleanup = Recover(_publisher);

        Assert.Contains(cleanup.Failures, failure => StringComparer.Ordinal.Equals(failure.Path, link)
            && failure.Message.Contains("link", StringComparison.Ordinal));
        Assert.Empty(cleanup.DeletedPaths);
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    [RequiresSymbolicLinkFact]
    public void DanglingWorkEntryLinkIsRefusedAndReportedAsALink()
    {
        Directory.CreateDirectory(WorkRoot);
        var link = Path.Combine(WorkRoot, "dangling");
        Directory.CreateSymbolicLink(link, Path.Combine(_root, "never-created"));

        var cleanup = Recover(_publisher);

        Assert.Contains(cleanup.Failures, failure => StringComparer.Ordinal.Equals(failure.Path, link)
            && failure.Message.Contains("link", StringComparison.Ordinal));
        Assert.Empty(cleanup.DeletedPaths);
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    [Fact]
    public void RegularFileUnderTheWorkRootIsNotADirectoryAndIsLeftInPlace()
    {
        Directory.CreateDirectory(WorkRoot);
        var stray = Path.Combine(WorkRoot, "stray.txt");
        File.WriteAllText(stray, "not a working directory");

        var cleanup = Recover(_publisher);

        var failure = Assert.Single(cleanup.Failures);
        Assert.Equal(stray, failure.Path);
        Assert.Contains("is not a working directory", failure.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(stray));
    }

    [Fact]
    public void LiveClaimProtectsEntryByNameWhenItsStoredPathDiffersOnlyInCase()
    {
        var live = StartLiveJob("case-writer");
        var listed = Path.Combine(WorkRoot, "bundle-cased");
        Directory.CreateDirectory(listed);
        File.WriteAllText(Path.Combine(listed, "evidence.sqlite"), "work copy");
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim("bundle-cased", _projectKey, live.JobId,
            live.ClaimToken!, _baselineRoot, Path.Combine(WorkRoot.ToUpperInvariant(), "BUNDLE-CASED")));

        var cleanup = Recover(_publisher);

        Assert.Empty(cleanup.Failures);
        Assert.True(File.Exists(Path.Combine(listed, "evidence.sqlite")));
    }

    [Fact]
    public void LiveClaimOutsideTheWorkRootIsReportedAndTheEntryIsLeftInPlace()
    {
        var live = StartLiveJob("elsewhere-writer");
        var listed = Path.Combine(WorkRoot, "bundle-elsewhere");
        Directory.CreateDirectory(listed);
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim("bundle-elsewhere", _projectKey, live.JobId,
            live.ClaimToken!, _baselineRoot, Path.Combine(_root, "elsewhere", "bundle-elsewhere")));

        var cleanup = Recover(_publisher);

        Assert.Contains(cleanup.Failures, failure => StringComparer.Ordinal.Equals(failure.Path, listed));
        Assert.Empty(cleanup.DeletedPaths);
        Assert.True(Directory.Exists(listed));
    }

    [Fact]
    public void ExpiredClaimOnADanglingWorkLinkIsRefusedAndKeepsItsClaim()
    {
        var expired = StartLiveJob("expired-link-writer");
        Directory.CreateDirectory(WorkRoot);
        var link = Path.Combine(WorkRoot, "bundle-expired-link");
        Directory.CreateSymbolicLink(link, Path.Combine(_root, "never-created"));
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim("bundle-expired-link", _projectKey, expired.JobId,
            expired.ClaimToken!, _baselineRoot, link));
        _clock.Advance(TimeSpan.FromMinutes(11));

        var cleanup = Recover(_publisher);

        Assert.Contains(cleanup.Failures, failure => StringComparer.Ordinal.Equals(failure.Path, link)
            && failure.Message.Contains("link", StringComparison.Ordinal));
        Assert.DoesNotContain(link, cleanup.DeletedPaths);
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Contains(_artifacts.ListExpiredBuildClaims(_projectKey), claim => claim.BundleId == "bundle-expired-link");
    }

    [Fact]
    public void CompletingAJobReleasesItsBundlePin()
    {
        var job = StartLiveJob("completing-writer");
        var build = PublishUnderClaim("completing", job);
        Assert.Contains("job:" + job.JobId, _artifacts.GetPinReasons(build.Bundle.BundleId));

        _ = new JobRepository(_database, _clock).Transition(job.JobId, JobStatus.Completed);

        Assert.DoesNotContain("job:" + job.JobId, _artifacts.GetPinReasons(build.Bundle.BundleId));
    }

    [Fact]
    public void PurgingAJobReleasesItsBundlePin()
    {
        var job = StartLiveJob("purging-writer");
        var build = PublishUnderClaim("purging", job);
        // Written directly so the pin survives into the purge: only the purge itself may release it.
        using (var connection = _database.OpenConnection())
        using (var end = connection.CreateCommand())
        {
            end.CommandText = "UPDATE Jobs SET Status='completed', FailureCategory='none', ArchivedUtc=UpdatedUtc " +
                "WHERE JobId=$job;";
            end.Parameters.AddWithValue("$job", job.JobId);
            Assert.Equal(1, end.ExecuteNonQuery());
        }
        Assert.Contains("job:" + job.JobId, _artifacts.GetPinReasons(build.Bundle.BundleId));
        var jobs = new JobRepository(_database, _clock);

        Assert.Equal(1, jobs.PurgeArchived(new ArchivePolicy(0)));

        Assert.DoesNotContain("job:" + job.JobId, _artifacts.GetPinReasons(build.Bundle.BundleId));
    }

    [Fact]
    public void ABundleWithOnlyADeadJobPinCanBeDeleted()
    {
        var job = StartLiveJob("dead-writer");
        var build = PublishUnderClaim("dead", job);
        Assert.True(_artifacts.DeleteReport(build.Report.ReportId));
        var jobs = new JobRepository(_database, _clock);
        _ = jobs.Transition(job.JobId, JobStatus.Completed);
        Assert.Equal(1, jobs.PurgeArchived(new ArchivePolicy(0)));

        Assert.True(_artifacts.TryMarkDeleting(build.Bundle.BundleId, _clock.UtcNow));
    }

    [Fact]
    public void StartupRemovesJobPinsWhoseJobIsAlreadyTerminal()
    {
        var published = Publish("startup-pin");
        var jobs = new JobRepository(_database, _clock);
        _ = jobs.Create("ended-job", _projectKey, ParsimonyJobHandler.JobKind, "{}",
            JobTimestamp.FormatUtc(_clock.UtcNow));
        _ = jobs.Transition("ended-job", JobStatus.Cancelled);
        // A pin left behind by a crash or by a job that ended before terminal transitions released it.
        _artifacts.AddReference(published.Bundle.BundleId, "job", "ended-job");
        Assert.Contains("job:ended-job", _artifacts.GetPinReasons(published.Bundle.BundleId));

        _ = new WorkerRecovery(jobs, _clock).Recover();

        Assert.DoesNotContain("job:ended-job", _artifacts.GetPinReasons(published.Bundle.BundleId));
    }

    private (ParsimonyBundleRecord Bundle, ReportRecord Report) PublishUnderClaim(string identity, JobRecord job)
    {
        var bundleId = "bundle-" + identity;
        var working = Path.Combine(WorkRoot, bundleId);
        Directory.CreateDirectory(working);
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim(bundleId, _projectKey, job.JobId, job.ClaimToken!,
            _baselineRoot, working));
        var build = CreateFiles(_publisher, identity, "material-" + identity);
        _artifacts.Publish(build.Bundle, build.Report, setCurrentBaseline: false);
        return (build.Bundle, build.Report);
    }

    // The publisher owns the layout; the tests follow it rather than spelling the path out.
    private string WorkRoot => _publisher.WorkRoot;

    private WorkspaceCleanupResult Recover(EvidenceArtifactPublisher publisher) =>
        new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(_workerRoot), _clock)
            .RecoverExpiredBuilds(_projectKey, publisher);

    private JobRecord StartLiveJob(string name)
    {
        var now = JobTimestamp.FormatUtc(_clock.UtcNow);
        _ = new JobRepository(_database, _clock).Create(name, _projectKey, ParsimonyJobHandler.JobKind, "{}", now);
        return new JobClaims(_database, _clock).Claim(_projectKey, name, now, TimeSpan.FromMinutes(10))!;
    }

    private string CreateOutsideDirectory(string name)
    {
        var outside = Path.Combine(_root, name);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "outside the work root");
        return outside;
    }

    private (ParsimonyBundleRecord Bundle, ReportRecord Report) Publish(string identity)
    {
        var build = CreateFiles(_publisher, identity, "material-" + identity);
        _artifacts.Publish(build.Bundle, build.Report);
        return (build.Bundle, build.Report);
    }

    private (ParsimonyBundleRecord Bundle, ReportRecord Report, string StagingDirectory, string FinalDirectory)
        CreateFiles(EvidenceArtifactPublisher publisher, string identity, string material)
    {
        var staging = CreateStagingFiles(publisher, identity, material);
        publisher.FlushAndClose(staging);
        return PublishFiles(publisher, identity, material, staging);
    }

    private string CreateStagingFiles(EvidenceArtifactPublisher publisher, string identity, string material)
    {
        var bundleId = "bundle-" + identity;
        var modelFingerprint = "sha256:" + Hash("model-fingerprint");
        var materialKey = Hash(material);
        var staging = publisher.CreateStagingDirectory(bundleId, _token, modelFingerprint, materialKey);
        File.WriteAllText(Path.Combine(staging, "grammar-facts.sqlite"), "facts:" + identity);
        File.WriteAllText(Path.Combine(staging, "evidence.sqlite"), "evidence:" + identity);
        return staging;
    }

    private (ParsimonyBundleRecord Bundle, ReportRecord Report, string StagingDirectory, string FinalDirectory)
        PublishFiles(EvidenceArtifactPublisher publisher, string identity, string material, string staging)
    {
        var bundleId = "bundle-" + identity;
        var modelFingerprint = "sha256:" + Hash("model-fingerprint");
        var materialKey = Hash(material);
        var final = publisher.Publish(staging, bundleId);
        var facts = Path.Combine(final, "grammar-facts.sqlite");
        var evidence = Path.Combine(final, "evidence.sqlite");
        var bundle = new ParsimonyBundleRecord(bundleId, TokenJson(), final, facts, evidence, modelFingerprint,
            1, HashFile(facts), 1, HashFile(evidence), JobTimestamp.FormatUtc(_clock.UtcNow), _projectKey,
            _token.BundleDigest[7..], materialKey, "available", null, _baselineRoot);
        var reportId = "report-" + identity;
        var reportJson = JsonSerializer.Serialize(new { inputs = new { bundleId } });
        var report = new ReportRecord(reportId, null, null, reportJson, "{}", "parsimony", "stored report",
            JobTimestamp.FormatUtc(_clock.UtcNow));
        return (bundle, report, staging, final);
    }

    private void InsertBaseline()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Baselines
                (ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, CapturedUtc,
                 BundleDigest, RootDirectory, FwDataPath, PublishedUtc, SourceLastWriteUtc)
            VALUES ($project, $identity, $semantic, '1', $captured, $bundle, $root, $fwdata, $published, $source);
            """;
        command.Parameters.AddWithValue("$project", _projectKey);
        command.Parameters.AddWithValue("$identity", _project.FieldWorksProjectIdentity);
        command.Parameters.AddWithValue("$semantic", _token.SemanticSnapshotDigest);
        command.Parameters.AddWithValue("$captured", _token.CapturedUtc);
        command.Parameters.AddWithValue("$bundle", _token.BundleDigest);
        command.Parameters.AddWithValue("$root", _baselineRoot);
        command.Parameters.AddWithValue("$fwdata", Path.Combine(_baselineRoot, "Project.fwdata"));
        command.Parameters.AddWithValue("$published", _token.CapturedUtc);
        command.Parameters.AddWithValue("$source", _token.CapturedUtc);
        command.ExecuteNonQuery();
    }

    private string TokenJson() => JsonSerializer.Serialize(_token, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
        .ToLowerInvariant();

    private sealed class TestClock(DateTimeOffset now) : IJobClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    private sealed class CheckpointHook(EvidencePublicationCheckpoint at, Action<string> action)
        : IEvidencePublicationHooks
    {
        public void Reach(EvidencePublicationCheckpoint checkpoint, string path)
        {
            if (checkpoint == at) action(path);
        }
    }

    private sealed class FailureHook(EvidencePublicationCheckpoint failureAt) : IEvidencePublicationHooks
    {
        public void Reach(EvidencePublicationCheckpoint checkpoint, string path)
        {
            if (checkpoint == failureAt) throw new IOException("Injected publication failure at " + checkpoint + ".");
        }
    }

    private sealed class FailFirstDeleteFileSystem : IWorkspaceFileSystem
    {
        private bool _failed;
        public bool Exists(string path) => Directory.Exists(path) || File.Exists(path);
        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
        public IReadOnlyList<string> EnumerateFileSystemEntries(string path) => Directory.GetFileSystemEntries(path);
        public void DeleteFile(string path)
        {
            if (!_failed)
            {
                _failed = true;
                throw new IOException("Injected file deletion failure.");
            }
            File.Delete(path);
        }
        public void DeleteDirectory(string path) => Directory.Delete(path, recursive: false);
    }

    private sealed class ReparseFileSystem(string reparsePath) : IWorkspaceFileSystem
    {
        public bool Exists(string path) => Directory.Exists(path) || File.Exists(path);
        public FileAttributes GetAttributes(string path) => StringComparer.Ordinal.Equals(path, reparsePath)
            ? File.GetAttributes(path) | FileAttributes.ReparsePoint : File.GetAttributes(path);
        public IReadOnlyList<string> EnumerateFileSystemEntries(string path) => Directory.GetFileSystemEntries(path);
        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path) => Directory.Delete(path, recursive: false);
    }
}
