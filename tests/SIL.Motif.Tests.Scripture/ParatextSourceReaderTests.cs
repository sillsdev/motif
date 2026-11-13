using SIL.Motif.Scripture;
using SIL.Motif.Tests.Scripture.Fixtures;
using SIL.Scripture;
using Xunit;

namespace SIL.Motif.Tests.Scripture;

public sealed class ParatextSourceReaderTests
{
    [Fact]
    public void ReadsNameGuidAndVersification()
    {
        using var project = TinyProject.Create();

        var source = ParatextSourceReader.Open(project.Folder);

        Assert.Equal(TinyProject.Name, source.Name);
        Assert.Equal(TinyProject.Guid, source.Guid);
        Assert.Equal(ScrVers.English.Name, source.Versification.Name);
    }

    [Fact]
    public void ListsChaptersPerBookInCanonicalOrder()
    {
        using var project = TinyProject.Create();

        var source = ParatextSourceReader.Open(project.Folder);

        Assert.Equal(["GEN", "MAT", "1CO", "TOB"], source.ChaptersByBook.Keys);
        Assert.Equal([1, 2, 3], source.ChaptersByBook["GEN"]);
        Assert.Equal(Enumerable.Range(1, 16), source.ChaptersByBook["1CO"]);
        Assert.Equal([1], source.ChaptersByBook["TOB"]);
    }

    [Fact]
    public void ReadsTheVersificationNamedInSettings()
    {
        using var project = TinyProject.Create(versification: 1);

        var source = ParatextSourceReader.Open(project.Folder);

        Assert.Equal(ScrVers.Original.Name, source.Versification.Name);
    }

    [Fact]
    public void ABookFileOutsideTheCanonIsLeftOutWithoutFailing()
    {
        using var project = TinyProject.Create(addNonCanonBook: true);

        var source = ParatextSourceReader.Open(project.Folder);

        Assert.DoesNotContain("XXA", source.ChaptersByBook.Keys);
        Assert.Equal(4, source.ChaptersByBook.Count);
    }

    [Fact]
    public void AMissingFolderIsRefusedWithItsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "motif-no-such-" + Guid.NewGuid().ToString("N"));

        var ex = Assert.Throws<DirectoryNotFoundException>(() => ParatextSourceReader.Open(path));

        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void AFolderWithoutSettingsIsRefused()
    {
        using var project = TinyProject.Create();
        File.Delete(Path.Combine(project.Folder, "Settings.xml"));

        var ex = Assert.Throws<InvalidDataException>(() => ParatextSourceReader.Open(project.Folder));

        Assert.Contains("Settings.xml", ex.Message);
    }
}
