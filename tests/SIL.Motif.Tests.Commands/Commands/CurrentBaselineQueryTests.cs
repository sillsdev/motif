using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="CurrentBaselineQuery"/> over a real, file-backed seeded project: the absent-Baseline
/// state before any capture, the recorded token and source last-save time after one, the project file's own
/// last-write time beside it, the live lock-file observation either way, and that reading never captures,
/// publishes, or otherwise changes anything.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class CurrentBaselineQueryTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.CurrentBaselineQueryTests", Guid.NewGuid().ToString("N"));

    public CurrentBaselineQueryTests(PristineProjectFixture pristine)
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
    public void BeforeAnyCaptureTheBaselineIsAbsentButTheLockObservationStillReports()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var outcome = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath));

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Value!.Token);
        Assert.Null(outcome.Value.SourceLastWriteUtc);
        Assert.False(outcome.Value.FieldWorksHeldProject);
    }

    [Fact]
    public void ReportsFieldWorksHoldsTheProjectWhenALockFileIsPresentEvenBeforeAnyCapture()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        using var lockHandle = new FileStream(
            fwDataPath + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var outcome = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath));

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.Value!.FieldWorksHeldProject);
    }

    [Fact]
    public void AfterACaptureReportsTheSameTokenAndSourceLastWriteTimeItRecorded()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded);

        var outcome = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath));

        Assert.True(outcome.Succeeded);
        Assert.Equal(captured.Value!.Token.BundleDigest, outcome.Value!.Token!.BundleDigest);
        Assert.Equal(captured.Value.SourceLastWriteUtc, outcome.Value.SourceLastWriteUtc);
    }

    [Fact]
    public void TheProjectFilesWriteTimeMatchesTheBaselineUntilTheFileIsSavedAgain()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded);

        var before = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath)).Value!;
        File.SetLastWriteTimeUtc(fwDataPath, before.SourceLastWriteUtc!.Value.UtcDateTime.AddMinutes(5));
        var after = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath)).Value!;

        Assert.Equal(before.SourceLastWriteUtc, before.ProjectLastWriteUtc);
        Assert.Equal(before.SourceLastWriteUtc!.Value.AddMinutes(5), after.ProjectLastWriteUtc);
        Assert.Equal(before.SourceLastWriteUtc, after.SourceLastWriteUtc);
    }

    [Fact]
    public void ReadingRepeatedlyNeverChangesTheRecordedBaseline()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded);

        var first = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath));
        var second = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath));
        var third = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath));

        Assert.Equal(first.Value!.Token, second.Value!.Token);
        Assert.Equal(first.Value.Token, third.Value!.Token);
        Assert.Equal(captured.Value!.Token.BundleDigest, third.Value.Token!.BundleDigest);
    }

    [Fact]
    public void AMovedAsideManagedRootLeavesNoBaselineUntilTheNextCaptureAndTheStoreSitsBesideTheProject()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var root = NewManagedRoot();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), root).Succeeded);
        Assert.True(File.Exists(Path.ChangeExtension(fwDataPath, ".motif.db")));
        Assert.NotNull(CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath)).Value!.Token);

        Directory.Delete(Path.Combine(root, "baselines"), recursive: true);
        var gone = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath));

        Assert.True(gone.Succeeded);
        Assert.Null(gone.Value!.Token);
        Assert.Null(gone.Value.SourceLastWriteUtc);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), root).Succeeded);
        Assert.NotNull(CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath)).Value!.Token);
    }

    [Fact]
    public void SavingTheDefaultSelectionAfterTheBaselineCopyWasMovedAsideDoesNotOpenTheMissingFile()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var root = NewManagedRoot();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), root).Succeeded);
        Directory.Delete(Path.Combine(root, "baselines"), recursive: true);

        var saved = SIL.Motif.Commands.SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            fwDataPath, "moved-aside", [Guid.NewGuid()], []));

        Assert.True(saved.Succeeded, saved.Refusal?.Code + ": " + saved.Refusal?.Message);
    }

    [Fact]
    public void StorageThatIsUnavailableWhenTheStoreIsOpenedRefusesAsStoreIoNeverAsAnInconsistentStore()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot()).Succeeded);
        var storePath = Path.GetFullPath(Path.ChangeExtension(fwDataPath, ".motif.db"));
        Environment.SetEnvironmentVariable(StorageFaultVariable, storePath);
        try
        {
            var outcome = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath));

            Assert.False(outcome.Succeeded);
            Assert.Equal("project.store-io", outcome.Refusal!.Code);
            Assert.Equal(FailureReason.Busy, outcome.Refusal.Reason);
            Assert.DoesNotContain("corrupt", outcome.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(StorageFaultVariable, null);
        }
        Assert.True(CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath)).Succeeded);
    }

    [Fact]
    public void AFitReadWhoseBaselineFileIsGoneReadsTheSavedProjectInsteadOfOpeningTheMissingPath()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(
            fwDataPath, Path.GetFileNameWithoutExtension(fwDataPath));
        var liveWrite = File.GetLastWriteTimeUtc(fwDataPath);
        var gone = new SIL.Motif.Worker.Baselines.BaselineRecord("key",
            new SIL.Motif.Contract.Baselines.BaselineToken("id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-08-23T00:00:00Z", "sha256:" + new string('2', 64)),
            Path.Combine(_managedRootsParent, "gone"), Path.Combine(_managedRootsParent, "gone", "x.fwdata"),
            new DateTimeOffset(liveWrite, TimeSpan.Zero), DateTimeOffset.UtcNow);

        var read = SIL.Motif.Commands.ProjectReadCache.ReadCurrent(project, gone, liveWrite.Ticks, (cache, ticks) => (cache is not null, ticks));

        Assert.True(read.Item1);
        Assert.Equal(liveWrite.Ticks, read.Item2);
    }

    private const string StorageFaultVariable = "MOTIF_TEST_STORAGE_UNAVAILABLE_FOR";

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
