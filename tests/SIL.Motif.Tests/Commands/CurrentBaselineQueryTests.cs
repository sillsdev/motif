using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="CurrentBaselineQuery"/> over a real, file-backed seeded project: the absent-Baseline
/// state before any capture, the recorded token and source last-save time after one, the live lock-file
/// observation either way, and that reading never captures, publishes, or otherwise changes anything.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
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

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
