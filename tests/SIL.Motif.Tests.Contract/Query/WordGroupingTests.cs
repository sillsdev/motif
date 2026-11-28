using System.Text;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract.Query;

public sealed class WordGroupingTests
{
    private static readonly Func<string, string> Fold = QueryFold.Compile(null);

    private static CorpusWord W(string form, int tokens = 1, string state = "unparsed") => new(form, state, tokens);

    private static GroupResponse Run(IEnumerable<CorpusWord> corpus, GroupSpec spec, params string[] lexicon) =>
        WordGrouping.Group(corpus, lexicon, spec, Fold, QueryFold.Default, "test", []);

    private static GroupSpec Suffix(string ending, string? state = null, int? limit = null, int? page = null) =>
        new(WordGrouping.Suffix, ending, null, state, limit, page is { } number ? ResultPaging.Cursor((number - 1) * Math.Min(limit ?? WordGrouping.DefaultLimit, WordGrouping.MaxLimit)) : null);

    [Fact]
    public void SuffixGroupsWordsByStemAndMarksKnownFittingAndNotFitting()
    {
        var result = Run([W("kaon", 5), W("kaonan", 3), W("baha", 2), W("bahaan", 1), W("lisan", 4), W("sulan", 1)],
            Suffix("an"), "kaon");
        Assert.Equal(["lis", "kaon", "baha", "sul"], result.Rows.Select(row => row.Stem).ToArray());
        Assert.Equal(["not-fitting", "known", "fitting", "not-fitting"], result.Rows.Select(row => row.Status).ToArray());
        Assert.Equal((1, 1, 2, 4), (result.Known, result.Fitting, result.NotFitting, result.Groups));
        Assert.Equal(6, result.CorpusTypes);
        Assert.True(result.Rows[1].StemAttested);
    }

    [Fact]
    public void AStemSharedWithAnotherEndingIsFittingEvenWhenTheBareStemIsAbsent()
    {
        var result = Run([W("kaonan"), W("kaonon")], Suffix("an"));
        var row = Assert.Single(result.Rows);
        Assert.Equal("fitting", row.Status);
        Assert.False(row.StemAttested);
        Assert.Equal(["kaonon"], row.Siblings);
    }

    [Fact]
    public void StemsShorterThanTheMinimumGraphemesAreLeftOutAndOneRestoresThem()
    {
        var corpus = new[] { W("man", 9), W("hudyaan", 1), W("e\u0301an", 1) };
        var byDefault = Run(corpus, Suffix("an"));
        Assert.Equal(2, byDefault.MinStem);
        Assert.Equal(["hudya"], byDefault.Rows.Select(row => row.Stem));
        var all = Run(corpus, new GroupSpec(WordGrouping.Suffix, "an", null, null, null, null, 1));
        Assert.Equal(3, all.Groups);
        Assert.Empty(Run(corpus, new GroupSpec(WordGrouping.Suffix, "an", null, null, null, null, 6)).Rows);
        Assert.Throws<ArgumentException>(() => Run(corpus, new GroupSpec(WordGrouping.Suffix, "an", null, null, null, null, 0)));
    }

    [Fact]
    public void PrefixGroupsByTheStemAfterTheStartAndFindsSiblingsByTheirEnd()
    {
        var result = Run([W("magbasa"), W("nagbasa"), W("basa")], new GroupSpec(WordGrouping.Prefix, "mag-", null, null, null, null));
        var row = Assert.Single(result.Rows);
        Assert.Equal("basa", row.Stem);
        Assert.Equal("fitting", row.Status);
        Assert.Equal(["nagbasa"], row.Siblings);
    }

    [Fact]
    public void WordsThatDifferOnlyByTheFoldAreOneTypeKeepingTheCommonestSpelling()
    {
        var result = Run([W("Kaonan", 1), W("kaonan", 4), W("KAONAN", 1)], Suffix("AN"));
        var row = Assert.Single(result.Rows);
        Assert.Equal("kaon", row.Stem);
        var word = Assert.Single(row.Words);
        Assert.Equal(("kaonan", 6), (word.Word, word.Tokens));
        Assert.Equal(1, result.CorpusTypes);
    }

    [Fact]
    public void FoldRulesApplyToTheStemKeyAndTheLexicon()
    {
        var fold = QueryFold.Compile("nfc,case,c~k");
        var result = WordGrouping.Group([W("cabanan")], ["kaban"], Suffix("an"), fold, "nfc,case,c~k", "test", []);
        Assert.Equal("known", Assert.Single(result.Rows).Status);
    }

    [Fact]
    public void ACombiningMarkNeverSplitsFromItsBaseLetter()
    {
        var word = "ka" + "é";
        var split = Run([W(word)], Suffix("́"));
        Assert.Empty(split.Rows);
        var whole = Run([W(word)], Suffix("é"));
        Assert.Equal("ka", Assert.Single(whole.Rows).Stem);
    }

    [Fact]
    public void ComposedAndDecomposedSpellingsGroupTogether()
    {
        var result = Run([W("kaé"), W("kaé")], Suffix("é"));
        var row = Assert.Single(result.Rows);
        Assert.Equal(1, result.CorpusTypes);
        Assert.Equal("ka", row.Stem);
    }

    [Fact]
    public void WordsOutsideTheBasicPlaneGroupByWholeCharacters()
    {
        var result = Run([W("a\U00010400\U00010401"), W("b\U00010401")], new GroupSpec(WordGrouping.Suffix, "\U00010401", null, null, null, null, 1));
        Assert.Equal(["a\U00010428", "b"], result.Rows.Select(row => row.Stem).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AnEndingThatIsTheWholeWordLeavesNoStem()
    {
        Assert.Empty(Run([W("an")], Suffix("an")).Rows);
    }

    [Fact]
    public void ParseStateFiltersTheListedWordsButNotTheFitJudgement()
    {
        var corpus = new[] { W("kaonan", 1, "approved"), W("kaonon", 1, "unparsed"), W("lisan", 1, "unparsed") };
        var approved = Run(corpus, Suffix("an", "approved"));
        var row = Assert.Single(approved.Rows);
        Assert.Equal(("kaon", "fitting"), (row.Stem, row.Status));
        Assert.Equal(1, approved.MatchingTypes);
        Assert.Equal(2, Run(corpus, Suffix("an", "unparsed")).MatchingTypes + 1);
        Assert.Equal(2, Run(corpus, Suffix("an")).Groups);
        Assert.Throws<ArgumentException>(() => Run(corpus, Suffix("an", "maybe")));
    }

    [Fact]
    public void PatternMatchesTheWholeFoldedWordAndTakesItsNamedStem()
    {
        var spec = new GroupSpec(WordGrouping.Suffix, null, "(?<stem>[a-z]+)(?:an|on)", null, null, null);
        var result = Run([W("kaonan"), W("kaonon"), W("kaone"), W("anak")], spec);
        var row = Assert.Single(result.Rows);
        Assert.Equal("kaon", row.Stem);
        Assert.Equal(2, row.WordsTotal);
    }

    [Theory]
    [InlineData("(?<stem>.+)(?=an)")]
    [InlineData("(.+)an")]
    [InlineData("(?<stem>.+")]
    public void AnUnsafeOrStemlessPatternIsRefused(string pattern)
    {
        var spec = new GroupSpec(WordGrouping.Suffix, null, pattern, null, null, null);
        Assert.Throws<ArgumentException>(() => Run([W("kaonan")], spec));
    }

    [Fact]
    public void ACatastrophicPatternRunsInLinearTime()
    {
        var spec = new GroupSpec(WordGrouping.Suffix, null, "(?<stem>(a+)+)b", null, null, null);
        var word = new string('a', 5000) + "c";
        var started = DateTime.UtcNow;
        var result = Run([W(word)], spec);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ATooLongPatternAndABadEndingChoiceAreRefused()
    {
        Assert.Throws<ArgumentException>(() => Run([W("a")], new GroupSpec(WordGrouping.Suffix, null,
            "(?<stem>" + new string('a', 300) + ")", null, null, null)));
        Assert.Throws<ArgumentException>(() => Run([W("a")], new GroupSpec(WordGrouping.Suffix, null, null, null, null, null)));
        Assert.Throws<ArgumentException>(() => Run([W("a")], new GroupSpec(WordGrouping.Suffix, "a", "(?<stem>.)", null, null, null)));
        Assert.Throws<ArgumentException>(() => Run([W("a")], new GroupSpec("infix", "a", null, null, null, null)));
    }

    [Fact]
    public void TheLimitIsCappedPagesAreStableAndThePlainMessageSaysSo()
    {
        var corpus = Enumerable.Range(0, 450).SelectMany(i => new[] { W($"s{i:D3}an", 1000 - i) }).ToArray();
        var capped = Run(corpus, Suffix("an", null, 9999));
        Assert.Equal(WordGrouping.MaxLimit, capped.Limit);
        Assert.Equal(200, capped.Rows.Count);
        Assert.NotNull(capped.NextCursor);
        Assert.True(capped.Capped);
        Assert.Contains(capped.Notes, note => note.Contains("reduced to the cap of 200", StringComparison.Ordinal));
        Assert.Contains(capped.Notes, note => note.Contains("--cursor " + capped.NextCursor, StringComparison.Ordinal));
        var second = Run(corpus, Suffix("an", null, 9999, 2));
        Assert.Equal("s200", second.Rows[0].Stem);
        var last = Run(corpus, Suffix("an", null, 9999, 3));
        Assert.Equal(50, last.Rows.Count);
        Assert.Null(last.NextCursor);
        Assert.DoesNotContain(last.Notes, note => note.Contains("--cursor", StringComparison.Ordinal));
        Assert.False(Run(corpus, Suffix("an", null, 200, 3)).Capped);
        Assert.Empty(Run(corpus, Suffix("an", null, null, 99)).Rows);
        Assert.Throws<ArgumentException>(() => Run(corpus, Suffix("an", null, 0)));
    }

    [Fact]
    public void WordsAndSiblingsPerStemAreBounded()
    {
        var corpus = Enumerable.Range(0, 40).Select(i => W("s" + (char)('a' + i % 26) + (char)('a' + i / 26) + "x", i + 1))
            .Concat(Enumerable.Range(0, 30).Select(i => W("s" + i.ToString("D2")))).ToArray();
        var spec = new GroupSpec(WordGrouping.Prefix, "s", null, null, null, null);
        var result = Run(corpus, spec);
        Assert.All(result.Rows, row => Assert.True(row.Words.Count <= StemGroup.WordsShown));
        Assert.All(result.Rows, row => Assert.True(row.Siblings.Count <= StemGroup.SiblingsShown));
    }

    [Fact]
    public void TheSameInputInAnyOrderGivesTheSameBytes()
    {
        var words = Enumerable.Range(0, 300).Select(i => W("w" + (i * 7919 % 97) + "an", i % 5 + 1)).ToArray();
        var a = Run(words, Suffix("an"), "w1");
        var b = Run(words.Reverse(), Suffix("an"), "w1");
        Assert.Equal(ProjectionJson.Serialize(a), ProjectionJson.Serialize(b));
    }

    [Fact]
    public void ALeadingOrTrailingHyphenOnTheAffixIsIgnored()
    {
        var plain = Run([W("kaonan")], Suffix("an"));
        var marked = Run([W("kaonan")], Suffix("-an"));
        Assert.Equal(ProjectionJson.Serialize(plain), ProjectionJson.Serialize(marked).Replace("-an", "an"));
    }

    [Fact]
    public void ResponseCarriesItsSourceNotesAndSchemaVersion()
    {
        var result = WordGrouping.Group([W("kaonan")], [], Suffix("an"), Fold, "nfc,case", "baseline", ["note one"]);
        Assert.Equal(("baseline", 1), (result.ReadAt, result.SchemaVersion));
        Assert.Contains("note one", result.Notes);
        Assert.Equal(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(result.Query)), result.Query);
    }

    [Fact]
    public void ADecomposedRawPatternGroupsTheSameWordsAsTheComposedOne()
    {
        // Tamil vowel sign O (U+0BCB) decomposes to U+0BC7 U+0BBE.
        var corpus = new[] { W("\u0BAA\u0B9F\u0B95\u0BCB"), W("\u0BAE\u0BB0\u0B95\u0BCB"), W("\u0BAA\u0B9F\u0B95\u0BCA") };
        var composed = Run(corpus, new GroupSpec(WordGrouping.Suffix, null, "(?<stem>.+)\u0B95\u0BCB", null, null, null, 1));
        Assert.NotEmpty(composed.Rows);
        var decomposed = Run(corpus, new GroupSpec(WordGrouping.Suffix, null, "(?<stem>.+)\u0B95\u0BC7\u0BBE", null, null, null, 1));
        Assert.Equal(composed.Rows.Select(row => row.Stem), decomposed.Rows.Select(row => row.Stem));
    }
}
