using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore.HandAnalyses;

/// <summary>Host lifetime: what a Stamp claims, readiness and phases, refresh, verify and rebuild.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class HandAnalysisHostTests : IDisposable
{
    private const string ChildStore = "MOTIF_HOST_CHILD_STORE";
    private const string ChildExpired = "MOTIF_HOST_CHILD_EXPIRED_ID";
    private const string ChildResult = "MOTIF_HOST_CHILD_RESULT";
    private static readonly int Homograph = LexEntryTags.kflidHomographNumber;

    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-hand-host-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "records.sqlite");
    private readonly ExternalBasis basis = new("source", "parser", "settings");
    private sealed class Crash : Exception;

    private sealed class PhaseLog(Func<HostReadiness> readiness, Action<OpenPhase>? onPhase = null) : IProgress<OpenPhase>
    {
        public List<(OpenPhase Phase, HostReadiness Readiness)> Seen { get; } = [];
        public void Report(OpenPhase value)
        {
            Seen.Add((value, readiness()));
            onPhase?.Invoke(value);
        }
    }

    private async Task<Guid> Import()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        var seed = SeededProject.Seed(cache);
        var text = SeededProject.SeedText(cache, seed);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
            var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId).MorphBundlesOS.Add(bundle);
            bundle.MorphRA = entry.LexemeFormOA;
            bundle.SenseRA = entry.SensesOS[0];
            bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
        });
        new FwDataProjectLoader().Save(cache);
        await LcmRecordBridge.ImportAsync(cache, NewLangProjFixture.FwDataPath(root), Database);
        return seed.FirstEntryId;
    }

    private async Task<string> Bump(string path, Guid entry, int by = 1)
    {
        await using var store = new Store(path);
        var revision = (await store.ReferencesAsync()).Revision;
        var old = (await store.ReadAsync()).Single(r => r.Id == entry).Values[Homograph.ToString()].GetInt32();
        return await store.SetFieldAsync(entry, Homograph, JsonSerializer.SerializeToElement(old), JsonSerializer.SerializeToElement(old + by),
            revision, Guid.NewGuid());
    }

    private void Run(string sql, string? path = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path ?? Database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string Copy(string name)
    {
        var target = Path.Combine(root, name + ".sqlite");
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        source.Open();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
        return target;
    }

    [Fact]
    public async Task AStampSaysCertifiedForAGoodCertificateAndFullAfterVerifyOrTheFullPathAndNeverUncertified()
    {
        await Import();
        await using (var host = await HandAnalysisHost.OpenAsync(Database))
        {
            await using var first = await host.PinAsync(basis, null);
            Assert.Equal(SnapshotVerification.Certified, first.Stamp.Verification);
            Assert.True((await host.VerifyAsync()).Passed);
            await using var after = await host.PinAsync(basis, null);
            Assert.Equal(first.Stamp.SnapshotId, after.Stamp.SnapshotId);
            Assert.Equal(SnapshotVerification.Full, after.Stamp.Verification);
            // A pin made before the verification keeps the claim it was made with.
            Assert.Equal(SnapshotVerification.Certified, first.Stamp.Verification);
            await using var otherBasis = await host.PinAsync(basis with { ParserRevision = "x" }, null);
            Assert.Equal(SnapshotVerification.Full, otherBasis.Stamp.Verification);
        }
        // No trusted certificate: the read-only host rescans history itself, serves Full and rewrites nothing.
        Run("UPDATE RecordStoreCertificate SET ReplayDigest = 'damaged'");
        await using var damaged = await HandAnalysisHost.OpenAsync(Database);
        await using var full = await damaged.PinAsync(basis, null);
        Assert.Equal(SnapshotVerification.Full, full.Stamp.Verification);
        using var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        raw.Open();
        using var command = raw.CreateCommand();
        command.CommandText = "SELECT ReplayDigest FROM RecordStoreCertificate";
        Assert.Equal("damaged", command.ExecuteScalar());
    }

    [Fact]
    public async Task AFullPathThatFindsDamagedHistoryRefusesCorruptAndNamesTheTable()
    {
        await Import();
        Run("UPDATE RecordStoreCertificate SET WriteInProgress = 1");
        Run("UPDATE ChangeEntities SET EntityId = randomblob(16) WHERE rowid = (SELECT min(rowid) FROM ChangeEntities)");
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        var refusal = await ((IHandAnalysisIndex)host).PinAsync(Guid.NewGuid(), null, default);
        Assert.Equal(RefusalCodes.HandAnalysisIndexUnavailable, refusal.Refusal?.Code);
        Assert.Equal(HandAnalysisRefusalFacts.Corrupt, refusal.Refusal!.Facts[HandAnalysisRefusalFacts.Cause]);
        Assert.Equal(HostReadiness.Unavailable, host.Readiness);
        Assert.Contains("ChangeEntities", refusal.Refusal.Message);
    }

    [Fact]
    public async Task ATamperedReferenceIndexRefusesAndRebuildingItRestoresTheHost()
    {
        await Import();
        Run("DELETE FROM IncomingReferences WHERE Target = (SELECT min(Target) FROM IncomingReferences)");
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        var refusal = await ((IHandAnalysisIndex)host).PinAsync(Guid.NewGuid(), null, default);
        Assert.Equal(HandAnalysisRefusalFacts.Corrupt, refusal.Refusal!.Facts[HandAnalysisRefusalFacts.Cause]);
        var status = await host.StatusAsync();
        Assert.Equal(ReferenceIndexState.Unavailable, status.ReferenceIndex);
        Assert.NotNull(status.Unavailable);
        var stamp = await host.RebuildIndexAsync();
        Assert.Equal(HostReadiness.Ready, host.Readiness);
        await using var pin = await host.PinAsync(basis, null);
        Assert.Equal(stamp.HomePoint, pin.Stamp.HomePoint);
    }

    [Fact]
    public async Task OneHundredPinCyclesWithRefreshesKeepTheRingCappedNoPinsHeldAndMemoryBounded()
    {
        var entry = await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        long baseline = 0;
        for (var cycle = 0; cycle < 100; cycle++)
        {
            if (cycle is 25 or 50 or 75)
            {
                await Bump(Database, entry);
                await host.RefreshAsync();
            }
            await using (await host.PinAsync(basis with { SourceRevision = (cycle % 7).ToString() }, null)) { }
            if (cycle == 10)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                baseline = GC.GetTotalMemory(true);
            }
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var growth = GC.GetTotalMemory(true) - baseline;
        var status = await host.StatusAsync();
        Assert.True(status.RetainedPoints <= HandAnalysisHost.RetainedPoints);
        Assert.Equal(0, status.HeldPins);
        Assert.True(growth < 16 * 1048576, $"managed memory grew by {growth / 1048576.0:F1} MiB over 90 pin cycles");
    }

    [Fact]
    public async Task ARefreshSwapsTheRingHeadInOneStepAndACrashAtTheSwapLeavesAConsistentHostAndStore()
    {
        var entry = await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis, loadPoint: true);
        var before = (await host.StatusAsync()).Current!;
        var moved = await Bump(Database, entry);
        host.SwapReached = () => throw new Crash();
        await Assert.ThrowsAsync<Crash>(() => host.RefreshAsync());
        var crashed = await host.StatusAsync();
        // The injection stopped the host before it changed anything: the old point, whole, and still serving.
        Assert.Equal(before, crashed.Current);
        Assert.Equal(HostReadiness.Ready, crashed.Readiness);
        await using (var old = await host.PinAsync(basis, before.SnapshotId)) Assert.Equal(before.HomePoint, old.Stamp.HomePoint);
        host.SwapReached = null;
        var refreshed = await host.RefreshAsync();
        Assert.Equal(moved, refreshed.HomePoint);
        Assert.Equal(refreshed, (await host.StatusAsync()).Current);
        // A host opened after the crash sees only the new point, and the old exact id is not retained there.
        await using var fresh = await HandAnalysisHost.OpenAsync(Database, basis);
        await using var pin = await fresh.PinAsync(basis, null);
        Assert.Equal(refreshed, pin.Stamp);
        Assert.True(pin.IsCurrent);
        await Assert.ThrowsAsync<SnapshotExpiredException>(() => fresh.PinAsync(basis, before.SnapshotId));
    }

    [Fact]
    public async Task ReadinessAndPhaseTimingsAreMonotoneThroughALoad()
    {
        await Import();
        HandAnalysisHost? host = null;
        var log = new PhaseLog(() => host?.Readiness ?? HostReadiness.Opening);
        host = await HandAnalysisHost.OpenAsync(Database, basis, progress: log);
        await using var _ = host;
        Assert.Equal(HostReadiness.Opened, host.Readiness);
        await using var pin = await host.PinAsync(basis, null);
        Assert.Equal(HostReadiness.Ready, host.Readiness);
        Assert.Equal([OpenPhase.Open, OpenPhase.State, OpenPhase.Load, OpenPhase.Project, OpenPhase.Pin], log.Seen.Select(s => s.Phase));
        Assert.Equal(HostReadiness.Opening, log.Seen[0].Readiness);
        Assert.All(log.Seen.Skip(1), s => Assert.Equal(HostReadiness.Verifying, s.Readiness));
        var phases = host.Phases;
        Assert.Equal(log.Seen.Select(s => s.Phase), phases.Select(p => p.Phase));
        for (var i = 2; i < phases.Count; i++)
            Assert.True(phases[i].Start >= phases[i - 1].Start + phases[i - 1].Duration, "phases overlap or run out of order");
        Assert.All(phases, p => Assert.True(p.Duration >= TimeSpan.Zero));
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("State")]
    [InlineData("Load")]
    [InlineData("Project")]
    [InlineData("Pin")]
    public async Task CancellingInAnyPhaseLeavesTheHostReusable(string phaseName)
    {
        var cancelAt = Enum.Parse<OpenPhase>(phaseName);
        await Import();
        using var cancel = new CancellationTokenSource();
        var armed = true;
        var log = new PhaseLog(() => HostReadiness.Opening, phase => { if (armed && phase == cancelAt) cancel.Cancel(); });
        if (cancelAt == OpenPhase.Open)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HandAnalysisHost.OpenAsync(Database, basis, cancel.Token, progress: log));
            return;
        }
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis, progress: log);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.PinAsync(basis, null, cancel.Token));
        Assert.Equal(HostReadiness.Opened, host.Readiness);
        var status = await host.StatusAsync();
        Assert.Equal(ReferenceIndexState.NotBuilt, status.ReferenceIndex);
        Assert.Null(status.Current);
        armed = false;
        await using var pin = await host.PinAsync(basis, null);
        Assert.Equal(HostReadiness.Ready, host.Readiness);
    }

    [Fact]
    public async Task ACancelledPinAfterARefusalLeavesStatusUnavailableNotBuilding()
    {
        await Import();
        Run("DELETE FROM IncomingReferences WHERE Target = (SELECT min(Target) FROM IncomingReferences)");
        using var cancel = new CancellationTokenSource();
        var armed = false;
        var log = new PhaseLog(() => HostReadiness.Opening, phase => { if (armed && phase == OpenPhase.State) cancel.Cancel(); });
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis, progress: log);
        Assert.NotNull((await ((IHandAnalysisIndex)host).PinAsync(Guid.NewGuid(), null, default)).Refusal);
        armed = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.PinAsync(basis, null, cancel.Token));
        var status = await host.StatusAsync();
        Assert.Equal(HostReadiness.Unavailable, status.Readiness);
        Assert.Equal(ReferenceIndexState.Unavailable, status.ReferenceIndex);
        Assert.NotNull(status.Unavailable);
    }

    [Fact]
    public async Task AQueryMadeWhileThePointLoadsWaitsAndGetsARealAnswerNeverAnEmptyOne()
    {
        var entry = await Import();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var log = new PhaseLog(() => HostReadiness.Opening, phase =>
        {
            if (phase != OpenPhase.Project) return;
            reached.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(60)));
        });
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis, progress: log);
        var first = Task.Run(async () => await host.PinAsync(basis, null));
        Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(60))));
        var status = await host.StatusAsync();
        Assert.Equal(HostReadiness.Verifying, status.Readiness);
        Assert.Equal(ReferenceIndexState.Building, status.ReferenceIndex);
        Assert.Null(status.Current);
        var second = host.PinAsync(basis, null);
        await Task.Delay(300);
        Assert.False(second.IsCompleted, "a pin must wait for the index it needs, not answer without it");
        release.Set();
        await using var a = await first;
        await using var b = await second;
        Assert.Equal(a.Stamp, b.Stamp);
        var closure = await a.References(new ReferenceQuery.DeleteClosure(new(DeletionKind.Entry, entry)), new PageRequest());
        Assert.NotEmpty(closure.Rows.Items);
        Assert.Equal(ReferenceIndexState.Ready, (await host.StatusAsync()).ReferenceIndex);
    }

    [Fact]
    public async Task StatusNamesEveryTableStateAndTheServedPoint()
    {
        await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis);
        var idle = await host.StatusAsync();
        Assert.Equal(HostReadiness.Opened, idle.Readiness);
        Assert.Null(idle.Current);
        await using var pin = await host.PinAsync(basis, null);
        var status = await host.StatusAsync();
        var tables = status.Tables.ToDictionary(t => t.Table, t => t.State);
        Assert.Equal("published", tables["RecordStoreState"]);
        Assert.Equal("current", tables["Commits"]);
        Assert.Equal("current", tables["RecordStoreCertificate"]);
        Assert.Equal("present", tables["RecordContentIdentity"]);
        Assert.Equal("trusted", tables["IncomingReferences"]);
        Assert.Equal("present", tables["RecordHeaders"]);
        Assert.Equal("present", tables["RecordChunks"]);
        Assert.Equal("current", tables["RecordPublicationBinding"]);
        foreach (var later in new[] { "HandProjectionState", "HandFacts" })
            Assert.Equal("absent", tables[later]);
        Assert.Equal(pin.Stamp, status.Current);
        Assert.Equal(HostReadiness.Ready, status.Readiness);
        Assert.Equal(1, status.HeldPins);
        Assert.Equal(5, status.Phases.Count);
        Assert.Contains("provenance", status.LastFullVerificationBy);

        foreach (var (sql, table, state) in new[]
        {
            ("UPDATE RecordStoreCertificate SET ReplayDigest = 'damaged'", "RecordStoreCertificate", "mismatch"),
            ("UPDATE RecordStoreCertificate SET WriteInProgress = 1", "RecordStoreCertificate", "write-in-progress"),
            ("UPDATE RecordStoreCertificate SET StoreId = 'elsewhere'", "RecordStoreCertificate", "other-store"),
            ("UPDATE RecordStoreState SET Revision = 'moved'", "Commits", "revision-mismatch"),
            ("UPDATE RecordStoreState SET Published = 0", "RecordStoreState", "unpublished"),
            ("DROP TABLE RecordStoreCertificate", "RecordStoreCertificate", "absent"),
            ("DROP TABLE RecordContentIdentity", "RecordContentIdentity", "absent"),
            ("UPDATE RecordPublicationBinding SET Revision = 'moved'", "RecordPublicationBinding", "mismatch"),
            ("DROP TABLE RecordHeaders", "RecordHeaders", "absent")
        })
        {
            var copy = Copy("damaged-" + Guid.NewGuid().ToString("N"));
            Run(sql, copy);
            Assert.Equal(state, RecordStoreTables(copy)[table]);
        }
    }

    private static Dictionary<string, string> RecordStoreTables(string path) =>
        Store.InspectTables(path).ToDictionary(t => t.Table, t => t.State);

    [Fact]
    public async Task VerifyNamesTheDamagedHistoryTableAndAGoodStoreIsRecertifiedFull()
    {
        await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis);
        Run("UPDATE ChangeEntities SET EntityId = randomblob(16) WHERE rowid = (SELECT min(rowid) FROM ChangeEntities)");
        var damaged = await host.VerifyAsync();
        Assert.False(damaged.Passed);
        Assert.Contains(damaged.Findings, f => f.Contains("ChangeEntities", StringComparison.Ordinal));
        Assert.Equal(0, (await host.StatusAsync()).Tables.Count(t => t.Table == "RecordStoreCertificate" && t.State == "write-in-progress"));
    }

    [Fact]
    public async Task VerifyDoesNotHoldTheWriterLockWhileItScansSoAnotherHandleCanWrite()
    {
        var entry = await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis);
        using var scanning = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        host.ShortLivedStoreOpened = store => store.VerifyScanBatchStarted = (_, _) =>
        {
            scanning.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(60)));
        };
        var verify = host.VerifyAsync();
        Assert.True(await Task.Run(() => scanning.Wait(TimeSpan.FromSeconds(60))));
        var moved = await Bump(Database, entry);
        release.Set();
        // The write ran during the scan, so the verification must refuse to certify.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => verify);
        Assert.Contains("changed while it was being verified", failure.Message);
        host.ShortLivedStoreOpened = null;
        Assert.True((await host.VerifyAsync()).Passed);
        await using var pin = await host.PinAsync(basis, null);
        Assert.Equal(moved, pin.Stamp.HomePoint);
        Assert.Equal(SnapshotVerification.Full, pin.Stamp.Verification);
    }

    [Fact]
    public async Task AWriteThatAddsNoContentRightAfterVerifyIsNeverLabelledFull()
    {
        var entry = await Import();
        await Bump(Database, entry);
        await Bump(Database, entry);
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis);
        string? moved = null;
        host.ShortLivedStoreOpened = store => store.AfterVerifyCertified = async () => moved = await Bump(Database, entry, -1);
        Assert.True((await host.VerifyAsync()).Passed);
        host.ShortLivedStoreOpened = null;
        Assert.NotNull(moved);
        await using var pin = await host.PinAsync(basis, null);
        Assert.Equal(moved, pin.Stamp.HomePoint);
        Assert.Equal(SnapshotVerification.Certified, pin.Stamp.Verification);
        Assert.Equal(SnapshotVerification.Certified, (await host.StatusAsync()).Current!.Verification);
    }

    [Fact]
    public async Task AWrongProjectPinRefusesOnlyThatPinAndKeepsTheHostReady()
    {
        await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis);
        await using (await host.PinAsync(basis, null)) { }
        var refused = await host.PinAsync(Guid.NewGuid(), null, CancellationToken.None);
        Assert.NotNull(refused.Refusal);
        var status = await host.StatusAsync();
        Assert.Equal(HostReadiness.Ready, status.Readiness);
        Assert.Equal(ReferenceIndexState.Ready, status.ReferenceIndex);
    }

    [Fact]
    public async Task ACancelledRebuildLeavesTheStoreAndHostUsable()
    {
        await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.RebuildIndexAsync(cancelled.Token));
        await using var pin = await host.PinAsync(basis, null);
        Assert.True((await host.VerifyAsync()).Passed);
    }

    [Fact]
    public async Task TwoOperatingSystemProcessesSeeTheSamePointAndTheOldExactIdExpiresInTheOther()
    {
        if (Environment.GetEnvironmentVariable(ChildStore) is { Length: > 0 } childStore)
        {
            await RunChildAsync(childStore, Environment.GetEnvironmentVariable(ChildExpired)!, Environment.GetEnvironmentVariable(ChildResult)!);
            return;
        }
        var entry = await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database, basis);
        await using var first = await host.PinAsync(basis, null);
        var result = Path.Combine(root, "child-result.txt");
        var start = DotnetTestProcess.CreateTestStartInfo(Path.Combine(FindRepositoryRoot(), "tests", "SIL.Motif.Tests.RecordStore",
            "SIL.Motif.Tests.RecordStore.csproj"),
            "FullyQualifiedName=SIL.Motif.Tests.RecordStore.HandAnalyses.HandAnalysisHostTests.TwoOperatingSystemProcessesSeeTheSamePointAndTheOldExactIdExpiresInTheOther");
        start.Environment[ChildStore] = Database;
        start.Environment[ChildExpired] = first.Stamp.SnapshotId;
        start.Environment[ChildResult] = result;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the child test process.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5)); }
        catch (TimeoutException)
        {
            child.Kill(entireProcessTree: true);
            throw;
        }
        Assert.True(child.ExitCode == 0, $"Child failed. stdout: {await output} stderr: {await error}");
        var lines = await File.ReadAllLinesAsync(result);
        // The other process committed, and this process's next pin is exactly the point that process served.
        await using var second = await host.PinAsync(basis, null);
        Assert.NotEqual(first.Stamp.SnapshotId, second.Stamp.SnapshotId);
        Assert.Equal(lines[0], second.Stamp.SnapshotId);
        Assert.Equal(lines[1], second.Stamp.HomePoint);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
    }

    private async Task RunChildAsync(string store, string expiredId, string result)
    {
        await using var writer = new Store(store);
        var entry = (await writer.ReadAsync()).First(r => r.Kind == "lcm" && r.ClassId == LexEntryTags.kClassId);
        var number = entry.Values[Homograph.ToString()].GetInt32();
        await writer.SetFieldAsync(entry.Id, Homograph, JsonSerializer.SerializeToElement(number), JsonSerializer.SerializeToElement(number + 1),
            (await writer.ReferencesAsync()).Revision, Guid.NewGuid());
        await using var host = await HandAnalysisHost.OpenAsync(store, basis);
        await using var pin = await host.PinAsync(basis, null);
        var expired = await ((IHandAnalysisIndex)host).PinAsync(pin.Stamp.ProjectId, expiredId, default);
        Assert.Equal(RefusalCodes.HandAnalysisSnapshotExpired, expired.Refusal?.Code);
        await File.WriteAllLinesAsync(result, [pin.Stamp.SnapshotId, pin.Stamp.HomePoint]);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find Motif.sln above the test output directory.");
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
