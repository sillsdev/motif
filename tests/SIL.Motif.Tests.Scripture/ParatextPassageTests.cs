using SIL.Machine.Tokenization;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.Scripture.Fixtures;
using Xunit;

namespace SIL.Motif.Tests.Scripture;

public sealed class ParatextPassageTests
{
    private static readonly IWordTokeniser Tokeniser = FieldWorksTokeniser.Create();

    [Fact]
    public void AnchorPolicyCountsWordsAndDigitsButNotPunctuationAndKeepsRepeatedSurfaces()
    {
        var chapter = new ParatextPassage("MAT 5", "hash",
            [new("MAT 5:3-4", "MAT 5:3", "Alpha, alpha! 42; lamp-light.")]);
        var words = ParatextPassages.Tokenize(chapter, FieldWorksTokeniser.Create("-", "4", "2"));
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
        Assert.Equal(ParatextPassages.Tokenize(first, Tokeniser), ParatextPassages.Tokenize(notes, Tokeniser));
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
        var words = ParatextPassages.Tokenize(chapter, Tokeniser);
        Assert.Equal("MAT 1:1#1", words[0].Anchor);
        Assert.DoesNotContain(words, word => word.Anchor.StartsWith("MAT 1:2#", StringComparison.Ordinal));
        Assert.Contains(words, word => word.Anchor == "MAT 1:3#1");
    }

    private const string Mixed = "Ma'ana lamp-light 42 a1b ng'ombe, \u02BCx";

    [Fact]
    public void IndexWordsEqualTheFieldWorksTokeniserWordsForApostropheHyphenDigitAndDeclaredPunctuation()
    {
        var chapter = new ParatextPassage("MAT 5", "hash", [new("MAT 5:3", "MAT 5:3", Mixed)]);
        foreach (var declared in new[] { Array.Empty<string>(), ["'", "-"], ["'", "-", "1", "2", "4"], ["\u02BC", "'"] })
        {
            var tokeniser = FieldWorksTokeniser.Create(declared);
            var words = ParatextPassages.Tokenize(chapter, tokeniser);
            Assert.Equal(tokeniser.Tokenise(Mixed), words.Select(word => word.Surface));
            Assert.Equal(Enumerable.Range(1, words.Count).Select(n => "MAT 5:3#" + n), words.Select(word => word.Anchor));
        }
        Assert.Equal(["Ma'ana", "lamp-light", "a", "b", "ng'ombe", "\u02BCx"],
            ParatextPassages.Tokenize(chapter, FieldWorksTokeniser.Create("'", "-", "\u02BC")).Select(word => word.Surface)
                .Where(surface => surface != "42" && surface != "1"));
    }

    [Fact]
    public void TheOldMachineTokeniserAndTheFieldWorksTokeniserDisagreeOnTheSameVerse()
    {
        var old = new LatinWordTokenizer().Tokenize(Mixed).Where(t => t.EnumerateRunes().Any(System.Text.Rune.IsLetterOrDigit)).ToArray();
        var chapter = new ParatextPassage("MAT 5", "hash", [new("MAT 5:3", "MAT 5:3", Mixed)]);
        var current = ParatextPassages.Tokenize(chapter, FieldWorksTokeniser.Create()).Select(word => word.Surface).ToArray();
        Assert.NotEqual(old, current);
        Assert.Contains("42", old);
        Assert.DoesNotContain("42", current);
        Assert.Contains("a1b", old);
        Assert.Contains("a", current);
    }

    [Fact]
    public void TheIndexVersionNamesTheTokeniserAndItsRuleVersion()
    {
        var tokeniser = FieldWorksTokeniser.Create();
        Assert.Equal(ParatextPassages.TokenizerVersion, tokeniser.Name + "-" + tokeniser.Version);
    }
}
