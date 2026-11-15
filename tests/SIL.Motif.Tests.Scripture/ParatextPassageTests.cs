using SIL.Motif.Scripture;
using SIL.Motif.Tests.Scripture.Fixtures;
using Xunit;

namespace SIL.Motif.Tests.Scripture;

public sealed class ParatextPassageTests
{
    [Fact]
    public void AnchorPolicyCountsWordsAndDigitsButNotPunctuationAndKeepsRepeatedSurfaces()
    {
        var chapter = new ParatextPassage("MAT 5", "hash",
            [new("MAT 5:3-4", "MAT 5:3", "Alpha, alpha! 42; lamp-light.")]);
        var words = ParatextPassages.Tokenize(chapter);
        Assert.Equal(["Alpha", "alpha", "42", "lamp-light"], words.Select(word => word.Surface));
        Assert.Equal(["MAT 5:3#1", "MAT 5:3#2", "MAT 5:3#3", "MAT 5:3#4"], words.Select(word => word.Anchor));
    }

    [Fact]
    public void MachineVerseHashesIgnoreNotesWhitespaceAndOtherChapters()
    {
        using var project = TinyProject.Create();
        var first = ParatextPassages.Read(project.Folder).Single(chapter => chapter.Locator == "MAT 1");
        var path = Path.Combine(project.Folder, "41MAT.SFM");
        var original = File.ReadAllText(path);
        File.WriteAllText(path, original.Replace("The lamp", "The   lamp\\f + \\ft A note.\\f*"));
        var notes = ParatextPassages.Read(project.Folder).Single(chapter => chapter.Locator == "MAT 1");
        Assert.Equal(first.Hash, notes.Hash);
        Assert.Equal(ParatextPassages.Tokenize(first), ParatextPassages.Tokenize(notes));
        File.WriteAllText(path, original.Replace("MAT 2 keeps", "MAT 2 always keeps"));
        var changed = ParatextPassages.Read(project.Folder);
        Assert.Equal(first.Hash, changed.Single(chapter => chapter.Locator == "MAT 1").Hash);
        File.WriteAllText(path, original.Replace("MAT 1 keeps", "MAT 1 always keeps"));
        Assert.NotEqual(first.Hash, ParatextPassages.Read(project.Folder).Single(chapter => chapter.Locator == "MAT 1").Hash);
    }

    [Fact]
    public void MachineBridgeRowsAnchorUnderTheirFirstVerse()
    {
        using var project = TinyProject.Create();
        var path = Path.Combine(project.Folder, "41MAT.SFM");
        var original = File.ReadAllText(path);
        File.WriteAllText(path, original.Replace("\\v 1 ", "\\v 1-2 ").Replace("\\v 2 ", "\\v 3 "));
        var chapter = ParatextPassages.Read(project.Folder).Single(chapter => chapter.Locator == "MAT 1");
        var words = ParatextPassages.Tokenize(chapter);
        Assert.Equal("MAT 1:1#1", words[0].Anchor);
        Assert.DoesNotContain(words, word => word.Anchor.StartsWith("MAT 1:2#", StringComparison.Ordinal));
        Assert.Contains(words, word => word.Anchor == "MAT 1:3#1");
    }
}
