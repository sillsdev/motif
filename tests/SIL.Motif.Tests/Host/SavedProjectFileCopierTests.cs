using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Host;

[Collection(LcmCacheTestCollection.Name)]
public sealed class SavedProjectFileCopierTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.SavedProjectFileCopierTests", Guid.NewGuid().ToString("N"));
    private readonly string _fwDataPath;

    public SavedProjectFileCopierTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_root);
        _fwDataPath = pristine.CopyProjectFile();
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task CopyAsync_SucceedsWhileFwDataLockFileIsHeldExclusively()
    {
        var fwDataPath = _fwDataPath;
        using var lockHandle = new FileStream(
            fwDataPath + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var destination = Path.Combine(_root, "captured");
        var copy = await new SavedProjectFileCopier().CopyAsync(fwDataPath, destination, CancellationToken.None);

        Assert.True(File.Exists(copy.FwDataPath));
        Assert.Equal(File.ReadAllBytes(fwDataPath), File.ReadAllBytes(copy.FwDataPath));
        Assert.NotEmpty(copy.WritingSystemPaths);
        Assert.All(copy.WritingSystemPaths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task CopyAsync_ReadsTheCompleteOldFileWhileFieldWorksRenamesConcurrently()
    {
        var projectFolder = Path.GetDirectoryName(_fwDataPath)!;
        var liveFwDataPath = _fwDataPath;
        var originalBytes = File.ReadAllBytes(liveFwDataPath);
        var originalLastWriteUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(liveFwDataPath, originalLastWriteUtc);

        var replacementPath = Path.Combine(_root, "replacement.fwdata");
        File.WriteAllText(replacementPath, "<languageproject><replacement/></languageproject>");
        var replacementLastWriteUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(replacementPath, replacementLastWriteUtc);

        var bakPath = Path.Combine(projectFolder, NewLangProjFixture.ProjectName + ".bak");
        var pausedAfterOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeCopy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var copier = new SavedProjectFileCopier
        {
            AfterFwDataHandleOpened = async () =>
            {
                pausedAfterOpen.SetResult();
                await resumeCopy.Task.WaitAsync(TimeSpan.FromSeconds(10));
            },
        };

        var destination = Path.Combine(_root, "captured");
        var copyTask = copier.CopyAsync(liveFwDataPath, destination, CancellationToken.None);
        await pausedAfterOpen.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Stands in for FieldWorks' own save: rename the currently open file away, then the replacement into place.
        File.Move(liveFwDataPath, bakPath, overwrite: true);
        File.Move(replacementPath, liveFwDataPath, overwrite: true);
        resumeCopy.SetResult();

        var copy = await copyTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(originalBytes, File.ReadAllBytes(copy.FwDataPath));
        Assert.Equal(originalLastWriteUtc, copy.SourceLastWriteUtc.UtcDateTime);
        Assert.NotEqual(replacementLastWriteUtc, copy.SourceLastWriteUtc.UtcDateTime);
    }

    [Fact]
    public async Task CopyAsync_ThrowsInvalidDataException_WhenSourceIsTruncatedBeforeItsClosingElement()
    {
        var content = File.ReadAllText(_fwDataPath);
        var closingIndex = content.LastIndexOf("</languageproject>", StringComparison.Ordinal);
        Assert.True(closingIndex > 0);

        var truncatedPath = Path.Combine(_root, "truncated.fwdata");
        File.WriteAllText(truncatedPath, content[..closingIndex]);

        var destination = Path.Combine(_root, "captured");
        await Assert.ThrowsAsync<InvalidDataException>(
            () => new SavedProjectFileCopier().CopyAsync(truncatedPath, destination, CancellationToken.None));
    }
}
