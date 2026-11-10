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
        var comparison = Compared(One(P("p1", R("كتاب قلم", "ar")), P("p1", R("كتاب دفتر", "ar")), P("p1", R("كتاب قلم", "ar"))));

        var change = Assert.Single(comparison.Sorted.Changes);
        Assert.Equal(("قلم", "دفتر"), (ChangeWords.Text(change.Starting), ChangeWords.Text(change.Mine)));
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
