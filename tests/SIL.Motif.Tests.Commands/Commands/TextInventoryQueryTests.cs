using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="TextInventoryQuery"/> over a real, file-backed seeded project: the empty inventory
/// before any Baseline exists, the seeded Text's identity and title once one has been captured, and that
/// a later change to the live project never leaks into an already-captured Baseline's inventory.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class TextInventoryQueryTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.TextInventoryQueryTests", Guid.NewGuid().ToString("N"));

    public TextInventoryQueryTests(PristineProjectFixture pristine)
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
    public void BeforeAnyCaptureTheInventoryIsEmpty()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var outcome = TextInventoryQuery.Query(new TextInventoryRequest(fwDataPath));

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.Texts);
    }

    [Fact]
    public void AfterACaptureListsTheSeededTextByGuidAndTitle()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var seededText = WriteTextOnto(fwDataPath);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded);

        var outcome = TextInventoryQuery.Query(new TextInventoryRequest(fwDataPath));

        Assert.True(outcome.Succeeded);
        var choice = Assert.Single(outcome.Value!.Texts);
        Assert.Equal(seededText.TextId, choice.Id);
        Assert.Equal(SeededProject.TextTitle, choice.Title);
    }

    [Fact]
    public void ATextAddedToTheLiveProjectAfterCaptureDoesNotAppearInTheInventory()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded);

        // Written to the live project only after the Baseline above was already captured from it.
        WriteTextOnto(fwDataPath);

        var outcome = TextInventoryQuery.Query(new TextInventoryRequest(fwDataPath));

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.Texts);
    }

    private SeededText WriteTextOnto(string fwDataPath)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(fwDataPath);
        var seededText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        return seededText;
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
