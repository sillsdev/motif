using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>One reader session reuses a Baseline copy and replaces or releases it safely.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class BaselineReadCacheTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(), "SIL.Motif.Tests.BaselineCache", Guid.NewGuid().ToString("N"));
    private readonly BaselineReadSession _readers;

    public BaselineReadCacheTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        var environment = SystemEnvironment(_temporaryRoot);
        _readers = new BaselineReadSession(path => BaselineReadCache.Open(path, environment));
    }

    [Fact]
    public void ReadsOfTheSameBaselineShareOneOpenCopy()
    {
        using var project = new WalkthroughProject(_pristine);
        Capture(project);

        var first = Probe(project.FwDataPath);
        var second = Probe(project.FwDataPath);
        var root = CopyRoot(first);

        Assert.Same(first, second);
        Assert.False(first.IsDisposed);
        Assert.Equal(Path.Combine(_temporaryRoot, BaselineReadCache.CopiesFolder), Path.GetDirectoryName(root));
        Assert.True(File.Exists(root + ".owner"));
    }

    [Fact]
    public void AReplacementBaselineDisposesTheOldCopyBeforeOpeningTheNewOne()
    {
        using var project = new WalkthroughProject(_pristine,
            sourceLastWriteUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Capture(project);
        var before = Probe(project.FwDataPath);
        var oldRoot = CopyRoot(before);
        var beforeEntryCount = EntryCount(before);

        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(project.FwDataPath))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(project.Seed.SecondEntryId).Delete());
            loader.Save(cache);
        }
        File.SetLastWriteTimeUtc(project.FwDataPath, new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc));
        Capture(project);

        var after = Probe(project.FwDataPath);

        Assert.NotSame(before, after);
        Assert.Equal(beforeEntryCount - 1, EntryCount(after));
        Assert.True(before.IsDisposed);
        Assert.False(Directory.Exists(oldRoot));
        Assert.False(File.Exists(oldRoot + ".owner"));
    }

    [Fact]
    public void SwitchingProjectsDisposesThePreviousProjectCopy()
    {
        using var left = new WalkthroughProject(_pristine);
        using var right = new WalkthroughProject(_pristine);
        Capture(left);
        Capture(right);

        var leftCache = Probe(left.FwDataPath);
        var leftRoot = CopyRoot(leftCache);
        var rightCache = Probe(right.FwDataPath);
        var rightRoot = CopyRoot(rightCache);

        Assert.NotSame(leftCache, rightCache);
        Assert.True(leftCache.IsDisposed);
        Assert.False(rightCache.IsDisposed);
        Assert.False(Directory.Exists(leftRoot));
        Assert.True(Directory.Exists(rightRoot));
    }

    [Fact]
    public void ExplicitReleaseDisposesTheCopyAndTheNextReadOpensAFreshOne()
    {
        using var project = new WalkthroughProject(_pristine);
        Capture(project);
        var first = Probe(project.FwDataPath);
        var root = CopyRoot(first);

        _readers.Release();

        Assert.True(first.IsDisposed);
        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(root + ".owner"));

        var second = Probe(project.FwDataPath);

        Assert.NotSame(first, second);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void FailedOpenDoesNotLeaveADisposedReaderAvailableAndTheSessionRecovers()
    {
        var source = _pristine.CopyProjectFile();
        var first = _readers.Read("project-key", Token('a', 'b'), source, cache => cache);
        var missing = Path.Combine(Path.GetDirectoryName(source)!, "missing-project.fwdata");

        Assert.ThrowsAny<Exception>(() => _readers.Read(
            "project-key", Token('c', 'd'), missing, cache => cache));
        Assert.True(first.IsDisposed);

        var recovered = _readers.Read("project-key", Token('a', 'b'), source, cache => cache);

        Assert.NotSame(first, recovered);
        Assert.False(recovered.IsDisposed);
        Assert.Single(Directory.GetDirectories(CopiesRoot));
        Assert.Single(Directory.GetFiles(CopiesRoot, "*.owner"));
    }

    private LcmCache Probe(string projectPath)
    {
        LcmCache? seen = null;
        var outcome = BaselineReads.Read(projectPath, cache =>
        {
            seen = cache;
            return new JsonObject();
        }, _readers);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return seen!;
    }

    private static void Capture(WalkthroughProject project)
    {
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
    }

    private static BaselineToken Token(char semanticDigest, char bundleDigest) =>
        new("project-identity", "sha256:" + new string(semanticDigest, 64), "projection-1",
            "2026-01-01T00:00:00Z", "sha256:" + new string(bundleDigest, 64));

    private static int EntryCount(LcmCache cache) =>
        cache.ServiceLocator.GetInstance<ILexEntryRepository>().Count;

    private static string CopyRoot(LcmCache cache) =>
        Path.GetDirectoryName(Path.GetDirectoryName(cache.ProjectId.Path))!;

    private string CopiesRoot => Path.Combine(_temporaryRoot, BaselineReadCache.CopiesFolder);

    private static BaselineReadCacheEnvironment SystemEnvironment(string root) =>
        new(root, static () => DateTimeOffset.UtcNow, BaselineReadCache.InspectSystemProcess);

    public void Dispose()
    {
        _readers.Dispose();
        try { Directory.Delete(_temporaryRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A sweep deletes only copies whose sidecar proves their exact owner is dead past grace.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class BaselineReadCacheSweepTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(), "SIL.Motif.Tests.BaselineSweep", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<(int ProcessId, long StartTicks), BaselineReadOwnerStatus> _owners = new();
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, string> MalformedNumericOwnerFields
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var ownerField in new[] { "version", "processId", "processStartUtcTicks", "createdUtcTicks" })
                foreach (var wrongKind in new[] { "\"1\"", "null", "{}", "[]", "true" })
                    data.Add(ownerField, wrongKind);
            return data;
        }
    }

    [Fact]
    public void AnOpenCopyPublishesItsExactOwnerAndSurvivesASweepWhileThatProcessLives()
    {
        var environment = SystemEnvironment(_temporaryRoot);
        using var reader = BaselineReadCache.Open(pristine.CopyProjectFile(), environment);
        var root = CopyRoot(reader);
        var owner = JsonNode.Parse(File.ReadAllText(root + ".owner"))!.AsObject();
        using var process = Process.GetCurrentProcess();

        Assert.Equal(Path.GetFileName(root), owner["copyId"]!.GetValue<string>());
        Assert.Equal(process.Id, owner["processId"]!.GetValue<int>());
        Assert.Equal(process.StartTime.ToUniversalTime().Ticks, owner["processStartUtcTicks"]!.GetValue<long>());
        Assert.Equal(0, BaselineReadCache.SweepStale(environment));
        Assert.True(Directory.Exists(root));

        reader.Dispose();

        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(root + ".owner"));
    }

    [Fact]
    public void ADeadOwnerPastGraceIsReclaimedWithItsMatchingRecord()
    {
        var root = AddOwnedCopy(_now - TimeSpan.FromMinutes(11), BaselineReadOwnerStatus.Dead);

        Assert.Equal(1, BaselineReadCache.SweepStale(Environment()));

        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(root + ".owner"));
    }

    [Fact]
    public void AReusedPidWithADifferentStartIdentityIsDead()
    {
        using var process = Process.GetCurrentProcess();
        var reusedPid = process.Id;
        var oldStartTicks = process.StartTime.ToUniversalTime().Ticks + TimeSpan.TicksPerDay;
        var root = AddOwnedCopy(_now - TimeSpan.FromMinutes(11), null, reusedPid, oldStartTicks);
        var environment = SystemEnvironment(_temporaryRoot);

        Assert.Equal(1, BaselineReadCache.SweepStale(environment));

        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(root + ".owner"));
    }

    [Fact]
    public void ALiveOwnerIsKeptEvenWhenItsRecordIsOlderThanGrace()
    {
        var root = AddOwnedCopy(_now - TimeSpan.FromDays(1), BaselineReadOwnerStatus.Alive);

        Assert.Equal(0, BaselineReadCache.SweepStale(Environment()));

        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void AnUnknownProcessStatusKeepsTheCopy()
    {
        var root = AddOwnedCopy(_now - TimeSpan.FromDays(1), BaselineReadOwnerStatus.Unknown);

        Assert.Equal(0, BaselineReadCache.SweepStale(Environment()));

        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void ADeadOwnerIsKeptBeforeGraceAndRemovedAtTheBoundary()
    {
        var created = _now - BaselineReadCache.AbandonedOwnerGracePeriod + TimeSpan.FromTicks(1);
        var root = AddOwnedCopy(created, BaselineReadOwnerStatus.Dead);
        var environment = Environment();

        Assert.Equal(0, BaselineReadCache.SweepStale(environment));
        Assert.True(Directory.Exists(root));

        _now = created + BaselineReadCache.AbandonedOwnerGracePeriod;

        Assert.Equal(1, BaselineReadCache.SweepStale(environment));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void AClockRollbackKeepsADeadOwnerWithAFutureCreationTime()
    {
        var root = AddOwnedCopy(_now + TimeSpan.FromMinutes(1), BaselineReadOwnerStatus.Dead);

        Assert.Equal(0, BaselineReadCache.SweepStale(Environment()));

        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void MissingMalformedMismatchedOrOrphanRecordsAndAgeAloneDoNotAuthorizeRemoval()
    {
        var missing = AddDirectoryWithoutOwner();
        Directory.SetLastWriteTimeUtc(missing, _now.UtcDateTime - TimeSpan.FromDays(2));
        var malformed = AddOwnedCopy(_now - TimeSpan.FromDays(2), null, ownerText: "{");
        var mismatched = AddOwnedCopy(_now - TimeSpan.FromDays(2), null, recordedCopyId: Guid.NewGuid().ToString("N"));
        var orphanId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(CopiesRoot);
        File.WriteAllText(Path.Combine(CopiesRoot, orphanId + ".owner"), OwnerJson(orphanId, 40001, 12, _now - TimeSpan.FromDays(2)));

        Assert.Equal(0, BaselineReadCache.SweepStale(Environment()));

        Assert.True(Directory.Exists(missing));
        Assert.True(Directory.Exists(malformed));
        Assert.True(Directory.Exists(mismatched));
        Assert.True(File.Exists(Path.Combine(CopiesRoot, orphanId + ".owner")));
    }

    [Theory]
    [MemberData(nameof(MalformedNumericOwnerFields))]
    public void MalformedNumericOwnerFieldIsRetainedAndDoesNotBlockAnotherReaderOpening(
        string field, string wrongKind)
    {
        var malformed = AddOwnedCopy(_now - TimeSpan.FromDays(2), null);
        var ownerPath = malformed + ".owner";
        var owner = JsonNode.Parse(File.ReadAllText(ownerPath))!.AsObject();
        owner[field] = JsonNode.Parse(wrongKind);
        File.WriteAllText(ownerPath, owner.ToJsonString());
        var retainedRecord = File.ReadAllText(ownerPath);
        BaselineReadCache? reader = null;

        try
        {
            var failure = Record.Exception(() =>
                reader = BaselineReadCache.Open(pristine.CopyProjectFile(), SystemEnvironment(_temporaryRoot)));

            Assert.Null(failure);
            Assert.NotNull(reader);
            Assert.True(Directory.Exists(malformed));
            Assert.Equal(retainedRecord, File.ReadAllText(ownerPath));
            Assert.NotEqual(malformed, CopyRoot(reader));
        }
        finally
        {
            reader?.Dispose();
        }
    }

    [Fact]
    public void UnknownCopyPathInspectionFailsWithoutRetryingOrDeletingAndThenRecovers()
    {
        Directory.CreateDirectory(CopiesRoot);
        var unrelated = Path.Combine(CopiesRoot, "notes");
        Directory.CreateDirectory(unrelated);
        var unrelatedFile = Path.Combine(unrelated, "keep.txt");
        File.WriteAllText(unrelatedFile, "keep");

        var blockFirstCandidate = true;
        string? inaccessibleCopy = null;
        var environment = new BaselineReadCacheEnvironment(
            _temporaryRoot,
            () => _now,
            (_, _) => BaselineReadOwnerStatus.Unknown,
            readEntryAttributes: path =>
            {
                var name = Path.GetFileName(path);
                if (blockFirstCandidate &&
                    string.Equals(Path.GetDirectoryName(path), CopiesRoot, StringComparison.Ordinal) &&
                    Guid.TryParseExact(name, "N", out _))
                {
                    blockFirstCandidate = false;
                    inaccessibleCopy = path;
                    Directory.CreateDirectory(path);
                    File.WriteAllText(Path.Combine(path, "keep.txt"), "unknown");
                    throw new UnauthorizedAccessException("The candidate's attributes are inaccessible.");
                }

                return File.GetAttributes(path);
            });

        BaselineReadCache? failedOpenResult = null;
        try
        {
            var failure = Record.Exception(() =>
                failedOpenResult = BaselineReadCache.Open(pristine.CopyProjectFile(), environment));

            Assert.IsType<IOException>(failure);
            Assert.NotNull(inaccessibleCopy);
            Assert.True(File.Exists(Path.Combine(inaccessibleCopy, "keep.txt")));
            Assert.False(File.Exists(inaccessibleCopy + ".owner"));
            Assert.True(File.Exists(unrelatedFile));

            using var recovered = BaselineReadCache.Open(pristine.CopyProjectFile(), environment);

            Assert.True(Directory.Exists(CopyRoot(recovered)));
            Assert.True(File.Exists(Path.Combine(inaccessibleCopy, "keep.txt")));
            Assert.True(File.Exists(unrelatedFile));
        }
        finally
        {
            failedOpenResult?.Dispose();
        }
    }

    [Fact]
    public void InaccessibleOwnerRecordsAndInterruptedPublicationAreRetained()
    {
        var inaccessible = AddOwnedCopy(_now - TimeSpan.FromDays(2), BaselineReadOwnerStatus.Dead);
        var inaccessibleOwner = inaccessible + ".owner";
        var interrupted = AddDirectoryWithoutOwner();
        var pending = interrupted + ".owner." + Guid.NewGuid().ToString("N") + ".pending";
        File.WriteAllText(pending, OwnerJson(Path.GetFileName(interrupted), 40002, 13, _now - TimeSpan.FromDays(2)));
        var environment = Environment(path =>
        {
            if (string.Equals(path, inaccessibleOwner, StringComparison.Ordinal))
                throw new UnauthorizedAccessException();
            return File.ReadAllText(path);
        });

        Assert.Equal(0, BaselineReadCache.SweepStale(environment));

        Assert.True(Directory.Exists(inaccessible));
        Assert.True(Directory.Exists(interrupted));
        Assert.True(File.Exists(pending));
    }

    [Fact]
    public void UnrelatedDirectoriesAndReparsePointCopiesAreUntouched()
    {
        Directory.CreateDirectory(CopiesRoot);
        var unrelated = Path.Combine(CopiesRoot, "notes");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "keep");
        var outside = Path.Combine(_temporaryRoot, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "outside");
        var linkId = Guid.NewGuid().ToString("N");
        var link = Path.Combine(CopiesRoot, linkId);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, outside);
            File.WriteAllText(link + ".owner", OwnerJson(linkId, 40003, 14, _now - TimeSpan.FromDays(2)));
        }

        Assert.Equal(0, BaselineReadCache.SweepStale(Environment()));

        Assert.True(Directory.Exists(unrelated));
        Assert.True(File.Exists(Path.Combine(outside, "sentinel.txt")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(Directory.Exists(link));
            Assert.Equal(outside, new DirectoryInfo(link).LinkTarget is { } target
                ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link)!, target))
                : string.Empty);
            Directory.Delete(link);
        }
    }

    private string AddOwnedCopy(DateTimeOffset created, BaselineReadOwnerStatus? status,
        int processId = 40000, long startTicks = 10, string? ownerText = null, string? recordedCopyId = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var root = Path.Combine(CopiesRoot, id);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "project.fwdata"), "fixture");
        if (status is { } ownerStatus) _owners[(processId, startTicks)] = ownerStatus;
        File.WriteAllText(root + ".owner", ownerText ?? OwnerJson(recordedCopyId ?? id, processId, startTicks, created));
        return root;
    }

    private string AddDirectoryWithoutOwner()
    {
        var root = Path.Combine(CopiesRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "partial.fwdata"), "fixture");
        return root;
    }

    private static string OwnerJson(string copyId, int processId, long startTicks, DateTimeOffset created,
        int version = 1) => JsonSerializer.Serialize(new
        {
            version,
            copyId,
            processId,
            processStartUtcTicks = startTicks,
            createdUtcTicks = created.UtcDateTime.Ticks
        });

    private BaselineReadCacheEnvironment Environment(Func<string, string>? readOwner = null) =>
        new(_temporaryRoot, () => _now,
            (pid, start) => _owners.TryGetValue((pid, start), out var status)
                ? status
                : BaselineReadOwnerStatus.Unknown,
            readOwner);

    private static BaselineReadCacheEnvironment SystemEnvironment(string root) =>
        new(root, static () => DateTimeOffset.UtcNow, BaselineReadCache.InspectSystemProcess);

    private string CopiesRoot => Path.Combine(_temporaryRoot, BaselineReadCache.CopiesFolder);

    private static string CopyRoot(BaselineReadCache reader) =>
        Path.GetDirectoryName(Path.GetDirectoryName(reader.Cache.ProjectId.Path))!;

    public void Dispose()
    {
        try { Directory.Delete(_temporaryRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
