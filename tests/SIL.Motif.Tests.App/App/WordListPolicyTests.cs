using SIL.Motif.App.Controls.WordPresentation;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class WordListPolicyTests
{
    [Fact]
    public void SchemaKeepsSemanticColumnOrderAndFiltersOnlyTheSharedMeaningColumn()
    {
        var schema = WordListSchema.Create(
            WordListColumnSet.Tick | WordListColumnSet.FieldWorks | WordListColumnSet.PanGloss |
            WordListColumnSet.Meaning | WordListColumnSet.Warnings | WordListColumnSet.Places |
            WordListColumnSet.Time | WordListColumnSet.Read,
            hasFieldWorksMorphology: true,
            hasPanGlossMorphology: true);

        WordListColumn[] expected =
        [
            WordListColumn.Tick, WordListColumn.Word, WordListColumn.FieldWorks, WordListColumn.PanGloss,
            WordListColumn.Meaning, WordListColumn.Warnings, WordListColumn.Places, WordListColumn.Time,
            WordListColumn.Read, WordListColumn.Next,
        ];
        Assert.Equal(expected, schema.OrderedColumns().ToArray());
        WordListColumn[] withoutMeaning =
        [
            WordListColumn.Tick, WordListColumn.Word, WordListColumn.FieldWorks, WordListColumn.PanGloss,
            WordListColumn.Warnings, WordListColumn.Places, WordListColumn.Time, WordListColumn.Read,
            WordListColumn.Next,
        ];
        Assert.Equal(withoutMeaning, schema.OrderedColumns(showsMeaning: false).ToArray());
    }

    [Fact]
    public void SchemaRejectsMorphologyWithoutItsProducerColumn()
    {
        Assert.Throws<ArgumentException>(() => WordListSchema.Create(
            WordListColumnSet.Tick, hasFieldWorksMorphology: true));
        Assert.Throws<ArgumentException>(() => WordListSchema.Create(
            WordListColumnSet.FieldWorks, hasPanGlossMorphology: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => WordListSchema.Create((WordListColumnSet)512));
    }

    [Fact]
    public void PolicySubtractsChromeAndScrollbarThenSharesOnlyTheRemainingWidthWithMorphology()
    {
        var schema = WordListSchema.Create(
            WordListColumnSet.Tick | WordListColumnSet.FieldWorks | WordListColumnSet.PanGloss |
            WordListColumnSet.Meaning | WordListColumnSet.Time,
            hasFieldWorksMorphology: true,
            hasPanGlossMorphology: true);
        var policy = new WordListPolicy();
        var layout = policy.Resolve(1000, schema, true, 4, "en", Sizing());

        Assert.Equal(958, layout.ContentWidth);
        Assert.Equal(958, layout.TrackAreaWidth);
        Assert.False(layout.UsesHorizontalViewport);
        Assert.Equal(24, layout.WidthOf(WordListColumn.Tick));
        Assert.Equal(120, layout.WidthOf(WordListColumn.Word));
        Assert.Equal(90, layout.WidthOf(WordListColumn.Meaning));
        Assert.Equal(90, layout.WidthOf(WordListColumn.Time));
        Assert.Equal(180, layout.WidthOf(WordListColumn.Next));
        Assert.Equal(8, layout.WidthOf(WordListColumn.FieldWorks) - layout.WidthOf(WordListColumn.PanGloss));

        var trackWidth = layout.Tracks.Sum(track => track.Width) +
            (layout.Tracks.Length - 1) * layout.ColumnGap;
        Assert.Equal(layout.ContentWidth, trackWidth, precision: 8);
    }

    [Fact]
    public void PolicyUsesOneHorizontalViewportWhenTheSupportedMinimumCannotFit()
    {
        var schema = WordListSchema.Create(
            WordListColumnSet.Tick | WordListColumnSet.FieldWorks | WordListColumnSet.PanGloss |
            WordListColumnSet.Meaning | WordListColumnSet.Time,
            hasFieldWorksMorphology: true,
            hasPanGlossMorphology: true);
        var layout = new WordListPolicy().Resolve(300, schema, true, 0, "en", Sizing());

        Assert.True(layout.UsesHorizontalViewport);
        Assert.Equal(258, layout.ContentWidth);
        Assert.True(layout.TrackAreaWidth > layout.ContentWidth);
        Assert.Equal(150, layout.WidthOf(WordListColumn.Next));
    }

    [Fact]
    public void PolicyReusesAnUnchangedLayoutAndAdvancesForMeaningStyleOrLocaleChanges()
    {
        var schema = WordListSchema.Create(WordListColumnSet.FieldWorks | WordListColumnSet.Meaning);
        var policy = new WordListPolicy();
        var sizing = Sizing();
        var first = policy.Resolve(1000, schema, true, 3, "en", sizing);

        Assert.Same(first, policy.Resolve(1000, schema, true, 3, "en", Sizing()));
        var hiddenMeaning = policy.Resolve(1000, schema, false, 3, "en", sizing);
        var changedStyle = policy.Resolve(1000, schema, false, 4, "en", sizing);
        var changedLocale = policy.Resolve(1000, schema, false, 4, "ar", sizing);

        Assert.Equal(first.Revision + 1, hiddenMeaning.Revision);
        Assert.Equal(hiddenMeaning.Revision + 1, changedStyle.Revision);
        Assert.Equal(changedStyle.Revision + 1, changedLocale.Revision);
        Assert.Throws<KeyNotFoundException>(() => hiddenMeaning.WidthOf(WordListColumn.Meaning));
    }

    private static WordListSizing Sizing() => new(
        outerChromeWidth: 24,
        reservedVerticalScrollbarWidth: 18,
        columnGap: 8,
        supportedMinimumWidth: 520,
        [
            new(WordListColumn.Tick, 24, 24, 24, 24),
            new(WordListColumn.Word, 80, 120, 220, 100),
            new(WordListColumn.FieldWorks, 72, 120, 400, 88),
            new(WordListColumn.PanGloss, 72, 120, 400, 80),
            new(WordListColumn.Meaning, 80, 90, 220, 90),
            new(WordListColumn.Warnings, 48, 70, 140, 64),
            new(WordListColumn.Places, 56, 72, 110, 62),
            new(WordListColumn.Time, 64, 90, 140, 84),
            new(WordListColumn.Read, 24, 24, 24, 20),
            new(WordListColumn.Next, 100, 180, 260, 150),
        ]);
}
