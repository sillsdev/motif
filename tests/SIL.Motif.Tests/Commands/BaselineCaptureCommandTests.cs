using System;
using System.IO;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="BaselineCaptureCommand"/> over a real, file-backed seeded project: a first capture,
/// idempotent recapture of unchanged bytes, a genuinely new capture after a save, the lock-file
/// observation, and both renderings of the typed response.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class BaselineCaptureCommandTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.BaselineCaptureCommandTests", Guid.NewGuid().ToString("N"));

    public BaselineCaptureCommandTests(PristineProjectFixture pristine)
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
    public void FirstCaptureRecordsANewBaselineFromTheSavedFile()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());

        Assert.True(outcome.Succeeded);
        var response = outcome.Value!;
        Assert.False(response.ReusedExistingBytes);
        Assert.False(response.FieldWorksHeldProject);
        Assert.True(File.Exists(response.FwDataPath));
        Assert.NotEqual(fwDataPath, response.FwDataPath);
        Assert.Equal(File.GetLastWriteTimeUtc(fwDataPath), response.SourceLastWriteUtc.UtcDateTime);
    }

    // The window lists Known projects from the same store the CLI fills, so capture itself must fill it.
    [Fact]
    public void ASuccessfulCaptureRecordsTheProjectAsKnownUnderTheManagedRoot()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();
        Assert.Empty(KnownProjectsQuery.List(managedRoot));

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);

        Assert.True(outcome.Succeeded);
        Assert.Equal(Path.GetFullPath(fwDataPath), Assert.Single(KnownProjectsQuery.List(managedRoot)).FullFwDataPath);
    }

    [Fact]
    public void RecapturingTheSameSavedBytesReusesTheExistingPublication()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();

        var first = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(first.Succeeded);

        var second = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);

        Assert.True(second.Succeeded);
        Assert.True(second.Value!.ReusedExistingBytes);
        Assert.Equal(first.Value!.Token.BundleDigest, second.Value.Token.BundleDigest);
        Assert.Equal(first.Value.Token.SemanticSnapshotDigest, second.Value.Token.SemanticSnapshotDigest);
    }

    [Fact]
    public void RecapturingAfterTheProjectIsSavedAgainProducesANewCapture()
    {
        using var cache = _pristine.NewScratch();
        var fwDataPath = cache.ProjectId.Path;
        var managedRoot = NewManagedRoot();

        var first = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(first.Succeeded);

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
            () => cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create());
        new FwDataProjectLoader().Save(cache);

        var second = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);

        Assert.True(second.Succeeded);
        Assert.False(second.Value!.ReusedExistingBytes);
        Assert.NotEqual(first.Value!.Token.BundleDigest, second.Value.Token.BundleDigest);
        Assert.NotEqual(first.Value.Token.SemanticSnapshotDigest, second.Value.Token.SemanticSnapshotDigest);
    }

    [Fact]
    public void ReportsFieldWorksHoldsTheProjectWhenALockFileIsPresent()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        using var lockHandle = new FileStream(
            fwDataPath + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.Value!.FieldWorksHeldProject);
    }

    [Fact]
    public void ReportsFieldWorksDoesNotHoldTheProjectWhenNoLockFileExists()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Value!.FieldWorksHeldProject);
    }

    [Fact]
    public void HumanRenderingNamesTheFreshnessAsOfFieldWorksLastSave()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(outcome.Succeeded);

        var rendered = CommandTextRenderer.Render(outcome, asJson: false);

        Assert.Equal(0, rendered.ExitCode);
        Assert.Contains("as of FieldWorks' last save", rendered.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonRendersAndBindsBackToTheTypedResponse()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(outcome.Succeeded);

        var rendered = CommandTextRenderer.Render(outcome, asJson: true);

        Assert.Equal(0, rendered.ExitCode);
        var bound = ProjectionJson.Deserialize<BaselineCaptureResponse>(rendered.Output);
        Assert.NotNull(bound);
        Assert.Equal(outcome.Value!.Token.BundleDigest, bound!.Token.BundleDigest);
        Assert.Equal(outcome.Value.FwDataPath, bound.FwDataPath);
        Assert.Equal(outcome.Value.ReusedExistingBytes, bound.ReusedExistingBytes);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
