using SIL.Motif.App.Compare;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using Xunit;

namespace SIL.Motif.Tests.App.Compare;

/// <summary>The three-way sort with invented placeholder text (ADR 0076): who made each change, and which overlap.</summary>
public sealed class TextSortTests
{
    private static readonly TextProps Italic = new(new Dictionary<int, (int, int)> { [TextPropertyNumbers.Italic] = (0, 1) }, new Dictionary<int, string>());

    private static TextRun R(string text, string ws = "en", string? style = null, TextProps? props = null) => new(text, ws, style, props ?? TextProps.None);

    private static ParagraphVersion P(string? key, params TextRun[] runs) => new(key, "Normal", TextProps.None, runs);

    private static ParagraphVersion P(string? key, string text) => P(key, R(text));

    private static TextVersions One(string starting, ParagraphVersion mine, ParagraphVersion fieldWorks) =>
        One(P("p1", starting), mine, fieldWorks);

    private static TextVersions One(ParagraphVersion starting, ParagraphVersion mine, ParagraphVersion fieldWorks) =>
        new([new StartingParagraph(starting.Key!, starting.Text, starting.Style, starting)], [mine], [fieldWorks]);

    private static TextComparison Compared(TextVersions versions) => new(TextSort.Sort(versions));

    private static string Text(TextComparison comparison) => string.Join("\n", comparison.Result().Select(paragraph => paragraph.Text));

    [Fact]
    public void TextNobodyChangedHasNoChanges()
    {
        var comparison = Compared(One("tavu mena kolo", P("p1", "tavu mena kolo"), P("p1", "tavu mena kolo")));

        Assert.Empty(comparison.Sorted.Changes);
        Assert.Equal("tavu mena kolo", Text(comparison));
        Assert.True(comparison.IsDecided);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TokensWithIdenticalFormattingIgnoreRunSplitsEvenWhenTheStartIsUnknown(bool unknown, bool preserveSpelling)
    {
        var start = P("p1", R("abcd", props: Italic));
        var versions = One(start, P("p1", R("a", props: Italic), R("bcd", props: Italic)), start);
        if (unknown) versions = versions with { Starting = [new StartingParagraph("p1", "abcd", "Normal", null)] };

        var comparison = new TextComparison(TextSort.Sort(versions, preserveSpelling: preserveSpelling));

        Assert.Empty(comparison.Sorted.Changes);
        Assert.Equal("abcd", Text(comparison));
        Assert.True(comparison.IsDecided);
        Assert.Single(Assert.Single(comparison.Result()).Runs);
    }

    [Fact]
    public void TokensStillDistinguishWhereFormattingChangesWithinAWord()
    {
        var start = P("p1", R("a", props: Italic), R("bcd"));
        var comparison = Compared(One(start, P("p1", R("ab", props: Italic), R("cd")), start));

        Assert.Equal(ChangeKind.Formatting, Assert.Single(comparison.Sorted.Changes).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AParagraphMovedOnOneSideHasASwitchableMoveWithoutChangingItsWords(bool otherMoved)
    {
        ParagraphVersion[] start = [P("p1", "a"), P("p2", "b"), P("p3", "c")];
        ParagraphVersion[] moved = [start[2], start[0], start[1]];
        var comparison = Compared(Paragraphs(start, otherMoved ? start : moved, otherMoved ? moved : start));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Contains("moved", change.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(otherMoved ? ChangeAuthor.FieldWorks : ChangeAuthor.Mine, change.Author);
        Assert.Equal("c\na\nb", Text(comparison));
        Assert.False(change.CanUseBoth);
        Assert.False(comparison.Choose(change, Choice.Both));
        comparison.Choose(change, otherMoved ? Choice.Mine : Choice.FieldWorks);
        Assert.Equal("a\nb\nc", Text(comparison));
        comparison.Choose(change, otherMoved ? Choice.FieldWorks : Choice.Mine);
        Assert.Equal("c\na\nb", Text(comparison));
        Assert.Equal(3, comparison.Result().Select(paragraph => paragraph.Key).Distinct().Count());
    }

    [Fact]
    public void TheSameParagraphOrderOnBothSidesNeedsNoMoveChoice()
    {
        ParagraphVersion[] start = [P("p1", "a"), P("p2", "b"), P("p3", "c")];
        ParagraphVersion[] moved = [start[2], start[0], start[1]];
        var comparison = Compared(Paragraphs(start, moved, moved));

        Assert.Empty(comparison.Sorted.Changes);
        Assert.Equal("c\na\nb", Text(comparison));
    }

    [Fact]
    public void MovesAndWordingAreSeparateChoicesAndChoosingEitherSideForAllGivesItsOrder()
    {
        ParagraphVersion[] start = [P("p1", "a"), P("p2", "b"), P("p3", "c"), P("p4", "d")];
        var comparison = Compared(Paragraphs(start, [start[3], start[0], P("p2", "x"), start[2]],
            [start[1], start[2], start[3], start[0]]));

        Assert.Contains(comparison.Sorted.Changes, change => change.Description.Contains("moved", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("d\nx\nc\na", Text(comparison));
        comparison.ChooseAll(Choice.Mine);
        Assert.Equal("d\na\nx\nc", Text(comparison));
        comparison.ChooseAll(Choice.FieldWorks);
        Assert.Equal("b\nc\nd\na", Text(comparison));
    }

    [Fact]
    public void EachSidesOrderCanBeChosenAcrossAllPermutationsOfFourParagraphs()
    {
        ParagraphVersion[] start = [P("p1", "a"), P("p2", "b"), P("p3", "c"), P("p4", "d")];
        var orders = (from a in start from b in start from c in start from d in start
                      let order = new[] { a, b, c, d }
                      where order.Distinct().Count() == start.Length
                      select order).ToArray();
        foreach (var mine in orders)
        foreach (var other in orders)
        {
            var comparison = Compared(Paragraphs(start, mine, other));
            Assert.Equal(start.Select(paragraph => paragraph.Key).Order(), comparison.Result().Select(paragraph => paragraph.Key).Order());
            comparison.ChooseAll(Choice.Mine);
            Assert.Equal(mine.Select(paragraph => paragraph.Key), comparison.Result().Select(paragraph => paragraph.Key));
            comparison.ChooseAll(Choice.FieldWorks);
            Assert.Equal(other.Select(paragraph => paragraph.Key), comparison.Result().Select(paragraph => paragraph.Key));
            foreach (var change in comparison.Sorted.Changes)
                comparison.Choose(change, change.Paragraph % 2 == 0 ? Choice.Mine : Choice.FieldWorks);
            Assert.Equal(start.Select(paragraph => paragraph.Key).Order(), comparison.Result().Select(paragraph => paragraph.Key).Order());
            Assert.Equal(start.Length, comparison.Result().Select(paragraph => paragraph.Key).Distinct().Count());
        }
    }

    [Fact]
    public void AParagraphMovedToDifferentPlacesByBothNeedsAPositionChoiceEvenAfterItsWordsAreRewritten()
    {
        ParagraphVersion[] start = [P("p1", "a"), P("p2", "b"), P("p3", "c"), P("p4", "d"), P("p5", "e")];
        var comparison = Compared(Paragraphs(start, [start[0], start[4], start[1], start[2], start[3]],
            [start[0], start[1], start[4], start[2], start[3]]));

        var move = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(ChangeAuthor.Both, move.Author);
        Assert.False(move.CanUseBoth);
        Assert.False(comparison.IsDecided);
        comparison.Rewrite(move.Paragraph, [P("p5", "x")]);
        Assert.False(comparison.IsDecided);
        comparison.Choose(move, Choice.FieldWorks);
        Assert.True(comparison.IsDecided);
        Assert.Equal("a\nb\nx\nc\nd", Text(comparison));
        comparison.Choose(move, Choice.Mine);
        Assert.Equal("a\nx\nb\nc\nd", Text(comparison));
    }

    [Fact]
    public void IndependentMovesInFiveParagraphsCanBeSwitchedOneAtATime()
    {
        ParagraphVersion[] start = [P("p1", "a"), P("p2", "b"), P("p3", "c"), P("p4", "d"), P("p5", "e")];
        var comparison = Compared(Paragraphs(start, [start[3], start[0], start[1], start[2], start[4]],
            [start[1], start[2], start[3], start[0], start[4]]));
        var mine = comparison.Sorted.Changes.Single(change => change.Author == ChangeAuthor.Mine);
        var other = comparison.Sorted.Changes.Single(change => change.Author == ChangeAuthor.FieldWorks);

        Assert.Equal("d\nb\nc\na\ne", Text(comparison));
        comparison.Choose(other, Choice.Mine);
        Assert.Equal("d\na\nb\nc\ne", Text(comparison));
        comparison.Choose(mine, Choice.FieldWorks);
        Assert.Equal("a\nb\nc\nd\ne", Text(comparison));
        comparison.Choose(other, Choice.FieldWorks);
        Assert.Equal("b\nc\nd\na\ne", Text(comparison));
        Assert.Equal(5, comparison.Result().Select(paragraph => paragraph.Key).Distinct().Count());
    }

    [Fact]
    public void DuplicateKeysInThePersonsTextPreserveBothParagraphs()
    {
        var original = P("p1", "a");
        var comparison = Compared(Paragraphs([original], [original, P("p1", "b")], [original]));

        Assert.Equal("a\nb", Text(comparison));
        Assert.Equal(["p1", null], comparison.Result().Select(paragraph => paragraph.Key));
        Assert.Equal(ChangeKind.ParagraphAdded, Assert.Single(comparison.Sorted.Changes).Kind);
    }

    [Fact]
    public void DuplicateFieldWorksKeysPreserveBothParagraphsWithOnlyOneMovePerKey()
    {
        ParagraphVersion[] start = [P("p1", "a"), P("p2", "b"), P("p3", "c")];
        var comparison = Compared(Paragraphs(start, [start[2], start[0], start[1]],
            [start[0], start[1], start[2], P("p3", "x")]));

        Assert.Single(comparison.Sorted.Changes, change => change.Kind == ChangeKind.ParagraphMoved);
        Assert.Equal(4, comparison.Result().Count);
        Assert.Equal(new[] { "a", "b", "c", "x" }, comparison.Result().Select(paragraph => paragraph.Text).Order());
    }

    [Fact]
    public void EmptyParagraphsCanBeMovedAndRestoredAfterRemoval()
    {
        ParagraphVersion[] start = [P("p1", "a"), P("p2", ""), P("p3", "c")];
        var comparison = Compared(Paragraphs(start, [start[1], start[0], start[2]], start));
        var move = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(ChangeKind.ParagraphMoved, move.Kind);
        Assert.Equal(["p2", "p1", "p3"], comparison.Result().Select(paragraph => paragraph.Key));
        comparison.Choose(move, Choice.FieldWorks);
        Assert.Equal(["p1", "p2", "p3"], comparison.Result().Select(paragraph => paragraph.Key));
        var removal = Compared(Paragraphs(start, start, [start[0], start[2]]));
        var removed = Assert.Single(removal.Sorted.Changes);
        Assert.Equal(ChangeKind.ParagraphRemoved, removed.Kind);
        removal.Choose(removed, Choice.Mine);
        Assert.Equal(["a", "", "c"], removal.Result().Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void AChangeMadeOnlyByThePersonIsTheirsAndIncluded()
    {
        var comparison = Compared(One("tavu mena kolo", P("p1", "tavu sipa kolo"), P("p1", "tavu mena kolo")));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal((ChangeAuthor.Mine, ChangeKind.Wording), (change.Author, change.Kind));
        Assert.Equal(("mena", "sipa", "mena"), (ChangeWords.Text(change.Starting), ChangeWords.Text(change.Mine), ChangeWords.Text(change.FieldWorks)));
        Assert.Equal(Choice.Mine, comparison.ChoiceFor(change));
        Assert.Equal("tavu sipa kolo", Text(comparison));
        Assert.Equal("You changed ‘mena’ to ‘sipa’", change.Description);
    }

    [Fact]
    public void AChangeMadeOnlyInFieldWorksIsIncludedAndCanBeSwitchedOff()
    {
        var comparison = Compared(One("tavu mena kolo", P("p1", "tavu mena kolo"), P("p1", "tavu mena kolo rupa")));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(ChangeAuthor.FieldWorks, change.Author);
        Assert.Equal("tavu mena kolo rupa", Text(comparison));
        Assert.Equal("FieldWorks added ‘ rupa’", change.Description);

        comparison.Choose(change, Choice.Mine);

        Assert.Equal("tavu mena kolo", Text(comparison));
    }

    [Fact]
    public void ChangesToDifferentWordsOfOneParagraphAreEachOneSidedAndBothIncluded()
    {
        var comparison = Compared(One("tavu mena kolo rupa", P("p1", "sipa mena kolo rupa"), P("p1", "tavu mena kolo nali")));

        Assert.Equal([ChangeAuthor.Mine, ChangeAuthor.FieldWorks], comparison.Sorted.Changes.Select(change => change.Author));
        Assert.Equal("sipa mena kolo nali", Text(comparison));
        Assert.True(comparison.IsDecided);
    }

    [Fact]
    public void TheSameWordsReplacedOnBothSidesNeedAChoiceAndCannotUseBoth()
    {
        var comparison = Compared(One("tavu mena kolo", P("p1", "tavu sipa kolo"), P("p1", "tavu nali kolo")));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(ChangeAuthor.Both, change.Author);
        Assert.Null(comparison.ChoiceFor(change));
        Assert.False(change.CanUseBoth);
        Assert.Equal(TextSort.ReplacesSameText, change.WhyNotBoth);
        Assert.False(comparison.Choose(change, Choice.Both));
        Assert.False(comparison.IsDecided);
        Assert.Equal("1 change still needs a choice (paragraph 1)", comparison.Waiting);

        Assert.True(comparison.Choose(change, Choice.FieldWorks));

        Assert.Equal("tavu nali kolo", Text(comparison));
        Assert.True(comparison.IsDecided);
        Assert.Null(comparison.Waiting);
    }

    [Fact]
    public void OnlyTheWordsBothReplacedNeedAChoiceAndTheirNeighboursStayOneSided()
    {
        var comparison = Compared(One("a b c d", P("p1", "a x y d"), P("p1", "a b z z")));

        Assert.Equal([ChangeAuthor.Mine, ChangeAuthor.Both, ChangeAuthor.FieldWorks], comparison.Sorted.Changes.Select(change => change.Author));
        var both = comparison.Sorted.Changes[1];
        Assert.Equal(("c", "y", "z"), (ChangeWords.Text(both.Starting), ChangeWords.Text(both.Mine), ChangeWords.Text(both.FieldWorks)));
        comparison.Choose(both, Choice.Mine);
        Assert.Equal("a x y z", Text(comparison));
    }

    [Fact]
    public void InsertionsAtTheSamePointCanUseBothMineFirst()
    {
        var comparison = Compared(One("tavu kolo", P("p1", "tavu sipa kolo"), P("p1", "tavu nali kolo")));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(ChangeAuthor.Both, change.Author);
        Assert.True(change.CanUseBoth);

        Assert.True(comparison.Choose(change, Choice.Both));

        Assert.Equal("tavu sipa nali kolo", Text(comparison));
    }

    [Fact]
    public void TheSameChangeOnBothSidesIsShownOnceUnmarked()
    {
        var comparison = Compared(One("tavu mena kolo", P("p1", "tavu sipa kolo"), P("p1", "tavu sipa kolo")));

        Assert.Empty(comparison.Sorted.Changes);
        Assert.Equal("tavu sipa kolo", Text(comparison));
    }

    [Fact]
    public void AFormattingOnlyChangeIsNamedInWordsAndDoesNotOverlapAWordingChangeElsewhere()
    {
        var starting = P("p1", R("tavu mena kolo"));
        var fieldWorks = P("p1", R("tavu "), R("mena", props: Italic), R(" kolo"));
        var comparison = Compared(One(starting, P("p1", "tavu mena sipa"), fieldWorks));

        Assert.Equal([(ChangeAuthor.FieldWorks, ChangeKind.Formatting), (ChangeAuthor.Mine, ChangeKind.Wording)],
            comparison.Sorted.Changes.Select(change => (change.Author, change.Kind)));
        Assert.Equal("Formatting only: ‘mena’ made italic in FieldWorks", comparison.Sorted.Changes[0].Description);
        var result = Assert.Single(comparison.Result());
        Assert.Equal("tavu mena sipa", result.Text);
        Assert.Equal(Italic, result.Runs.Single(run => run.Text == "mena").Props);
    }

    [Fact]
    public void RemovingWordsTheOtherSideReformatsNeedsAChoice()
    {
        var starting = P("p1", R("tavu mena kolo"));
        var fieldWorks = P("p1", R("tavu "), R("mena", props: Italic), R(" kolo"));
        var comparison = Compared(One(starting, P("p1", "tavu kolo"), fieldWorks));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(ChangeAuthor.Both, change.Author);
        Assert.Equal(TextSort.RemovesReformatted, change.WhyNotBoth);
    }

    [Fact]
    public void AWritingSystemOnlyChangeIsItsOwnKind()
    {
        var comparison = Compared(One(P("p1", R("tavu mena")), P("p1", R("tavu "), R("mena", "ar")), P("p1", R("tavu mena"))));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal((ChangeAuthor.Mine, ChangeKind.WritingSystem), (change.Author, change.Kind));
        Assert.Contains("from en to ar", change.Description, StringComparison.Ordinal);
        Assert.Contains("from English to Arabic", ChangeWords.Describe(change, tag => tag == "en" ? "English" : "Arabic"), StringComparison.Ordinal);
    }

    [Fact]
    public void ANormalisationChangeIsNoDifferenceAndAChangedMarkMarksTheWholeWord()
    {
        const string composed = "mbéla tavu";
        const string decomposed = "mbéla tavu";
        Assert.Empty(Compared(One(composed, P("p1", decomposed), P("p1", composed))).Sorted.Changes);

        var comparison = Compared(One(decomposed, P("p1", "mbèla tavu"), P("p1", decomposed)));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(("mbéla", "mbèla"), (ChangeWords.Text(change.Starting), ChangeWords.Text(change.Mine)));
    }

    [Fact]
    public void ABaseLetterIsNeverPartedFromItsCombiningMarks()
    {
        var tokens = Tokens.Of([R("kạ́mi to")]);

        Assert.Equal(["kạ́mi".Normalize(System.Text.NormalizationForm.FormD), " ", "to"], tokens.Select(token => token.Text));
    }

    [Fact]
    public void ARightToLeftWordReplacedOnOneSideIsOneChange()
    {
        var comparison = Compared(One(P("p1", R("ظغوت ثذيك", "ar")), P("p1", R("ظغوت ضخوج", "ar")), P("p1", R("ظغوت ثذيك", "ar"))));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(("ثذيك", "ضخوج"), (ChangeWords.Text(change.Starting), ChangeWords.Text(change.Mine)));
    }

    [Fact]
    public void ATabSeparatedParagraphIsComparedAsOneParagraphWithChangesInEachCell()
    {
        var comparison = Compared(One("ka\tmena\tkolo", P("p1", "ki\tmena\tkolo"), P("p1", "ka\tmena\tkwolo")));

        Assert.Equal([ChangeAuthor.Mine, ChangeAuthor.FieldWorks], comparison.Sorted.Changes.Select(change => change.Author));
        Assert.Equal("ki\tmena\tkwolo", Text(comparison));
    }

    [Fact]
    public void AParagraphRewrittenOnBothSidesIsOneChoiceForTheWholeParagraph()
    {
        var comparison = Compared(One("a b c d", P("p1", "w x y d"), P("p1", "a q r s")));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal((ChangeAuthor.Both, ChangeKind.Rewritten), (change.Author, change.Kind));
        Assert.Same(change, comparison.Sorted.Paragraphs[0].Whole);
        Assert.Equal(ChangeWords.RewrittenInBoth, change.Description);
        comparison.Choose(change, Choice.FieldWorks);
        Assert.Equal("a q r s", Text(comparison));
    }

    private static TextVersions Paragraphs(IReadOnlyList<ParagraphVersion> starting, IReadOnlyList<ParagraphVersion> mine,
        IReadOnlyList<ParagraphVersion> fieldWorks) =>
        new(starting.Select(paragraph => new StartingParagraph(paragraph.Key!, paragraph.Text, paragraph.Style, paragraph)).ToArray(), mine, fieldWorks);

    [Fact]
    public void ParagraphsAddedOnEachSideAreOneSidedAndInReadingOrder()
    {
        var comparison = Compared(Paragraphs([P("p1", "one"), P("p2", "two")],
            [P("p1", "one"), P(null, "mine"), P("p2", "two")],
            [P("p1", "one"), P("p2", "two"), P("p3", "theirs")]));

        Assert.Equal([(ChangeAuthor.Mine, ChangeKind.ParagraphAdded), (ChangeAuthor.FieldWorks, ChangeKind.ParagraphAdded)],
            comparison.Sorted.Changes.Select(change => (change.Author, change.Kind)));
        Assert.Equal("one\nmine\ntwo\ntheirs", Text(comparison));
        Assert.Equal(["p1", null, "p2", "p3"], comparison.Result().Select(paragraph => paragraph.Key));
    }

    [Fact]
    public void AParagraphRemovedOnOneSideAndUntouchedOnTheOtherIsThatSidesRemoval()
    {
        var comparison = Compared(Paragraphs([P("p1", "one"), P("p2", "two")], [P("p1", "one")], [P("p1", "one"), P("p2", "two")]));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal((ChangeAuthor.Mine, ChangeKind.ParagraphRemoved), (change.Author, change.Kind));
        Assert.Equal("one", Text(comparison));
        comparison.Choose(change, Choice.FieldWorks);
        Assert.Equal("one\ntwo", Text(comparison));
    }

    [Fact]
    public void ARunSplitDoesNotTurnAnOtherwiseUntouchedRemovalIntoAChoiceBothMade()
    {
        var start = P("p1", R("abcd", props: Italic));
        var comparison = Compared(Paragraphs([start], [P("p1", R("a", props: Italic), R("bcd", props: Italic))], []));

        var removal = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(ChangeAuthor.FieldWorks, removal.Author);
        Assert.True(comparison.IsDecided);
        comparison.Choose(removal, Choice.Mine);
        Assert.Equal("abcd", Text(comparison));
    }

    [Fact]
    public void AParagraphRemovedInFieldWorksThatThePersonChangedNeedsAChoice()
    {
        var comparison = Compared(Paragraphs([P("p1", "one"), P("p2", "two")], [P("p1", "one"), P("p2", "two more")], [P("p1", "one")]));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal((ChangeAuthor.Both, ChangeKind.ParagraphRemoved), (change.Author, change.Kind));
        Assert.Equal("Changed by you, removed in FieldWorks", change.Description);
        comparison.Choose(change, Choice.Mine);
        Assert.Equal("one\ntwo more", Text(comparison));
        comparison.Choose(change, Choice.FieldWorks);
        Assert.Equal("one", Text(comparison));
    }

    [Fact]
    public void AParagraphRemovedOnBothSidesIsTheSameChange()
    {
        var comparison = Compared(Paragraphs([P("p1", "one"), P("p2", "two")], [P("p1", "one")], [P("p1", "one")]));

        Assert.Empty(comparison.Sorted.Changes);
        Assert.Equal("one", Text(comparison));
    }

    [Fact]
    public void AParagraphStyleChangedOnOneSideIsAFormattingChange()
    {
        var quoted = P("p1", "one") with { Style = "Quotation" };
        var comparison = Compared(Paragraphs([P("p1", "one")], [P("p1", "one")], [quoted]));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal((ChangeAuthor.FieldWorks, ChangeKind.ParagraphFormatting), (change.Author, change.Kind));
        Assert.Equal("Quotation", Assert.Single(comparison.Result()).Style);
    }

    [Fact]
    public void WithoutTheStartingRunsAFormattingDifferenceBetweenTheSidesNeedsAChoice()
    {
        var versions = new TextVersions([new StartingParagraph("p1", "tavu mena", "Normal", null)],
            [P("p1", R("tavu mena sipa"))], [P("p1", R("tavu "), R("mena", props: Italic))]);

        var comparison = Compared(versions);

        Assert.Equal([(ChangeAuthor.Both, ChangeKind.Formatting), (ChangeAuthor.Mine, ChangeKind.Wording)],
            comparison.Sorted.Changes.Select(change => (change.Author, change.Kind)));
        Assert.Equal(TextSort.FormatsDiffer, comparison.Sorted.Changes[0].WhyNotBoth);
        comparison.Choose(comparison.Sorted.Changes[0], Choice.FieldWorks);
        Assert.Equal(Italic, Assert.Single(comparison.Result()).Runs.Single(run => run.Text == "mena").Props);
    }

    [Fact]
    public void ChoosingOneSideForAllDecidesEveryChange()
    {
        var comparison = Compared(One("tavu mena kolo rupa", P("p1", "sipa nali kolo rupa"), P("p1", "tavu lasi kolo rupa")));
        Assert.False(comparison.IsDecided);

        comparison.ChooseAll(Choice.FieldWorks);

        Assert.True(comparison.IsDecided);
        Assert.Equal("tavu lasi kolo rupa", Text(comparison));
    }

    [Fact]
    public void TheSummaryCountsEachSidesChangesAndThoseThatNeedAChoice()
    {
        var comparison = Compared(One("a b c d e", P("p1", "x b c q e"), P("p1", "a b y r e")));

        Assert.Equal("3 changes: 1 yours · 1 from FieldWorks · 1 needs your choice", comparison.Summary);
    }

    [Fact]
    public void ARewrittenParagraphReplacesTheChoicesInsideItUntilSetBack()
    {
        var comparison = Compared(One("tavu mena kolo", P("p1", "tavu sipa kolo"), P("p1", "tavu nali kolo")));

        comparison.Rewrite(0, [P("p1", "typed side by side")]);

        Assert.True(comparison.IsDecided);
        Assert.Equal("typed side by side", Text(comparison));
        comparison.Rewrite(0, null);
        Assert.False(comparison.IsDecided);
    }

    [Fact]
    public void TheScreenReaderHearsWhatWhereAndWhichVersionIsInUse()
    {
        var comparison = Compared(One("tavu mena kolo", P("p1", "tavu mena kolo"), P("p1", "tavu mena kolo rupa")));
        var change = comparison.Sorted.Changes[0];

        Assert.Equal("FieldWorks added ‘ rupa’, in paragraph 1. In use. Alt+M for yours.", ChangeWords.Announce(change, Choice.FieldWorks));

        var both = Compared(One("tavu mena kolo rupa", P("p1", "tavu sipa kolo rupa"), P("p1", "tavu nali kolo rupa"))).Sorted.Changes[0];
        Assert.Equal("Both changed this. Yours ‘sipa’, FieldWorks ‘nali’, paragraph 1. Needs a choice.", ChangeWords.Announce(both, null));
    }

    [Fact]
    public void NoWordOnScreenIsOneTheViewerAvoids()
    {
        var words = typeof(ChangeWords).GetFields().Select(field => (string)field.GetValue(null)!);

        Assert.All(words, word =>
        {
            foreach (var avoided in new[] { "conflict", "keep", "accept", "reject" })
                Assert.DoesNotContain(avoided, word, StringComparison.OrdinalIgnoreCase);
        });
    }
}

/// <summary>Building the three versions from a pending edit and the project, and turning a result back into a pending edit.</summary>
public sealed class ComparedPayloadsTests
{
    private static GdrTextProperties Props(string? style = null) => new(new Dictionary<int, GdrIntProperty>(),
        style is null ? new Dictionary<int, string>() : new Dictionary<int, string> { [TextPropertyNumbers.NamedStyle] = style });

    private static GdrParagraph Live(string key, params GdrRun[] runs) => new("Normal", false, "en", Props("Normal"), runs, key);

    private static GdrRun Run(string text, string? style = null) => new(text, "en", style, Props(style));

    private static TextParagraphExpected Expected(GdrParagraph paragraph) =>
        new(string.Concat(paragraph.Runs.Select(run => run.Text)), paragraph.Style ?? "", TextRunsDigest.Of(paragraph));

    [Fact]
    public void ThePayloadDigestOfRunsMatchesTheProjectsDigestOfTheSameRuns()
    {
        GdrRun[] runs = [Run("x "), Run("y", "Emphasis"), new GdrRun("z", "ar", null, Props()) { ObjData = "link" }];

        Assert.Equal(TextRunsDigest.OfRuns(runs), TextVersions.Digest(runs.Select(TextVersions.Run)));
    }

    [Fact]
    public void AnEditsStartIsTheFieldWorksRunsWhenTheyStillMatchItsFingerprint()
    {
        var p1 = Live("p1", Run("tavu "), Run("mena", "Emphasis"));
        var p2 = Live("p2", Run("kolo"));
        var edit = new TextParagraphsPayload("CmPossibility.Discussion", ["p1", "p2"],
            [new TextParagraphChange(0, "p1", "Normal", [new TextRun("tavu sipa", "en")])], [], new Dictionary<string, TextParagraphExpected> { ["p1"] = Expected(p1) });

        var versions = TextVersions.FromParagraphs(edit, [p1, p2 with { Runs = [Run("kolo rupa")] }]);

        var start = versions.Starting.Single(paragraph => paragraph.Key == "p1");
        Assert.NotNull(start.Exact);
        Assert.Equal("Emphasis", start.Exact!.Runs[1].CharacterStyle);
        // p2 was not in the edit, so every side holds FieldWorks' version of it.
        Assert.Equal("kolo rupa", versions.Mine.Single(paragraph => paragraph.Key == "p2").Text);
        var comparison = new TextComparison(TextSort.Sort(versions));
        Assert.Equal(["tavu sipa", "kolo rupa"], comparison.Result().Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void AnUntouchedParagraphRemovedFromFieldWorksCanBeRestoredFromTheEditingSession()
    {
        var first = Live("p1", Run("a"));
        var removed = Live("p2", Run("b", "Emphasis"));
        var last = Live("p3", Run("c"));
        var edit = new TextParagraphsPayload("CmPossibility.Discussion", ["p1", "p2", "p3"], [], [],
            new Dictionary<string, TextParagraphExpected>());
        var comparison = new TextComparison(TextSort.Sort(TextVersions.FromParagraphs(edit, [first, last],
            new[] { first, removed, last }.Select(TextVersions.Paragraph))));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal((ChangeAuthor.FieldWorks, ChangeKind.ParagraphRemoved), (change.Author, change.Kind));
        Assert.Equal(["a", "c"], comparison.Result().Select(paragraph => paragraph.Text));
        comparison.Choose(change, Choice.Mine);
        Assert.Equal(["a", "b", "c"], comparison.Result().Select(paragraph => paragraph.Text));
        Assert.Equal("Emphasis", comparison.Result()[1].Runs[0].CharacterStyle);
        var payload = ComparedPayloads.Paragraphs("CmPossibility.Discussion", comparison.Result(), [first, last])!;
        Assert.Equal(["p1", null, "p3"], payload.Order);
        Assert.Equal("b", Assert.Single(payload.Changed).Runs[0].Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMissingRememberedParagraphIsListedAsAnUnavailableRemovalRatherThanDropped(bool partialSession)
    {
        var first = Live("p1", Run("a"));
        var edit = new TextParagraphsPayload("CmPossibility.Discussion", ["p1", "p2"], [], [],
            new Dictionary<string, TextParagraphExpected>());
        var comparison = new TextComparison(TextSort.Sort(TextVersions.FromParagraphs(edit, [first],
            partialSession ? [TextVersions.Paragraph(first)] : null)));

        var removal = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(ChangeKind.ParagraphRemoved, removal.Kind);
        Assert.Contains("unavailable", removal.Description, StringComparison.OrdinalIgnoreCase);
        Assert.False(comparison.Choose(removal, Choice.Mine));
        comparison.ChooseAll(Choice.Mine);
        Assert.Equal(["a"], comparison.Result().Select(paragraph => paragraph.Text));
        Assert.Equal(Choice.FieldWorks, comparison.ChoiceFor(removal));
        Assert.Null(ComparedPayloads.Paragraphs("CmPossibility.Discussion", comparison.Result(), [first]));
    }

    [Fact]
    public void MovesAndARememberedFieldWorksRemovalAreIndependentChoicesInOneRecord()
    {
        var start = new[] { Live("p1", Run("a")), Live("p2", Run("")), Live("p3", Run("c")), Live("p4", Run("d")) };
        var edit = new TextParagraphsPayload("CmPossibility.Discussion", ["p4", "p1", "p2", "p3"], [], [],
            new Dictionary<string, TextParagraphExpected>());
        var comparison = new TextComparison(TextSort.Sort(TextVersions.FromParagraphs(edit, [start[0], start[2], start[3]],
            start.Select(TextVersions.Paragraph))));
        var move = comparison.Sorted.Changes.Single(change => change.Kind == ChangeKind.ParagraphMoved);
        var removal = comparison.Sorted.Changes.Single(change => change.Kind == ChangeKind.ParagraphRemoved);

        Assert.Equal(["p4", "p1", "p3"], comparison.Result().Select(paragraph => paragraph.Key));
        comparison.Choose(removal, Choice.Mine);
        Assert.Equal(["p4", "p1", "p2", "p3"], comparison.Result().Select(paragraph => paragraph.Key));
        comparison.Choose(move, Choice.FieldWorks);
        Assert.Equal(["p1", "p2", "p3", "p4"], comparison.Result().Select(paragraph => paragraph.Key));
        Assert.Equal(4, comparison.Result().Select(paragraph => paragraph.Key).Distinct().Count());
        Assert.Equal("", comparison.Result()[1].Text);
    }

    [Fact]
    public void WithoutRememberedOrderADifferentParagraphOrderRequiresAChoice()
    {
        var live = new[] { Live("p1", Run("a")), Live("p2", Run("b")), Live("p3", Run("c")) };
        var edit = new TextParagraphsPayload("CmPossibility.Discussion", ["p3", "p1", "p2"], [], [],
            new Dictionary<string, TextParagraphExpected>());
        var comparison = new TextComparison(TextSort.Sort(TextVersions.FromParagraphs(edit, live)));

        var move = Assert.Single(comparison.Undecided);
        Assert.Equal(ChangeKind.ParagraphMoved, move.Kind);
        Assert.Equal(ChangeAuthor.Both, move.Author);
        Assert.Equal(["p3", "p1", "p2"], comparison.Result().Select(paragraph => paragraph.Key));
        comparison.Choose(move, Choice.FieldWorks);
        Assert.Equal(["p1", "p2", "p3"], comparison.Result().Select(paragraph => paragraph.Key));
    }

    [Fact]
    public void AKeyedMoveUsesTheRememberedOrderAndCanBeRestagedInTheChosenOrder()
    {
        var first = Live("p1", Run("a"));
        var middle = Live("p2", Run("b"));
        var last = Live("p3", Run("c"));
        GdrParagraph[] live = [first, middle, last];
        var edit = new TextParagraphsPayload("CmPossibility.Discussion", ["p3", "p1", "p2"], [], [],
            new Dictionary<string, TextParagraphExpected>());
        var comparison = new TextComparison(TextSort.Sort(TextVersions.FromParagraphs(edit, live,
            live.Select(TextVersions.Paragraph))));

        var move = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(ChangeKind.ParagraphMoved, move.Kind);
        Assert.Equal(["c", "a", "b"], comparison.Result().Select(paragraph => paragraph.Text));
        var payload = ComparedPayloads.Paragraphs("CmPossibility.Discussion", comparison.Result(), live)!;
        Assert.Equal(["p3", null, null], payload.Order);
        Assert.Equal(["a", "b"], payload.Changed.Select(change => change.Runs[0].Text));
        comparison.Choose(move, Choice.FieldWorks);
        Assert.Null(ComparedPayloads.Paragraphs("CmPossibility.Discussion", comparison.Result(), live));
    }

    [Fact]
    public void AnEditsStartIsUnknownWhenNeitherSideMatchesItsFingerprint()
    {
        var p1 = Live("p1", Run("tavu mena"));
        var edit = new TextParagraphsPayload("CmPossibility.Discussion", ["p1"],
            [new TextParagraphChange(0, "p1", "Normal", [new TextRun("tavu sipa", "en")])], [], new Dictionary<string, TextParagraphExpected> { ["p1"] = Expected(p1) });

        var versions = TextVersions.FromParagraphs(edit, [p1 with { Runs = [Run("tavu "), Run("mena", "Emphasis")] }]);

        Assert.Null(Assert.Single(versions.Starting).Exact);
    }

    [Fact]
    public void ADoneResultFitsTheFieldWorksTextItIsMeasuredAgainst()
    {
        var live = new[] { Live("p1", Run("one")), Live("p3", Run("three")) };
        ParagraphVersion[] result =
        [
            new("p1", "Normal", TextProps.None, [new TextRun("one more", "en")]),
            new("p2", "Normal", TextProps.None, [new TextRun("kept though FieldWorks removed it", "en")]),
            new(null, "Normal", TextProps.None, [new TextRun("mine", "en")]),
        ];

        var payload = ComparedPayloads.Paragraphs("CmPossibility.Discussion", result, live)!;

        Assert.Equal(["p1", null, null], payload.Order);
        Assert.Equal(["p3"], payload.Removed);
        Assert.Equal([0, 1, 2], payload.Changed.Select(change => change.Index));
        Assert.Equal(new[] { "p1", "p3" }.Order(), payload.Expected.Keys.Order());
        Assert.Equal(Expected(live[0]), payload.Expected["p1"]);
    }

    [Fact]
    public void AResultThatIsTheFieldWorksTextLeavesNothingToChange()
    {
        var live = new[] { Live("p1", Run("tavu "), Run("mena", "Emphasis")) };

        Assert.Null(ComparedPayloads.Paragraphs("CmPossibility.Discussion", live.Select(TextVersions.Paragraph).ToArray(), live));
        Assert.Null(ComparedPayloads.RichString("LexSense.Definition", "en", TextVersions.Paragraph(live[0]), live[0].Runs));
    }

    [Fact]
    public void ARichStringResultIsMeasuredAgainstTheFieldWorksAlternative()
    {
        GdrRun[] live = [Run("fw text")];

        var payload = ComparedPayloads.RichString("LexSense.Definition", "en",
            new ParagraphVersion("en", "", TextProps.None, [new TextRun("merged", "en")]), live)!;

        Assert.Equal(("merged", "fw text", TextRunsDigest.OfRuns(live)), (payload.Text, payload.Expected.Text, payload.Expected.Runs));
    }

    [Fact]
    public void ARichStringEditComparesTheOneAlternative()
    {
        GdrRun[] began = [Run("tavu mena kolo")];
        var edit = new TextRichStringPayload("LexSense.Definition", "en", [new TextRun("tavu sipa kolo", "en")],
            new TextRichStringExpected("tavu mena kolo", TextRunsDigest.OfRuns(began)));

        var comparison = new TextComparison(TextSort.Sort(TextVersions.FromRichString(edit, [Run("tavu mena kolo rupa")], "en"), "en"));

        Assert.Equal([ChangeAuthor.Mine, ChangeAuthor.FieldWorks], comparison.Sorted.Changes.Select(change => change.Author));
        Assert.Equal("en", comparison.Sorted.Changes[0].Location);
        Assert.Equal("tavu sipa kolo rupa", Assert.Single(comparison.Result()).Text);
    }
}
