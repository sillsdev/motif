using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Owner = SIL.Motif.Tests.App.WorkspaceContextTests.CapturedSelectionOwner;

namespace SIL.Motif.Tests.App;

public sealed class TextsPageListCountTests
{
    private static readonly string[] Words = ["kaon", "baga", "plain"];
    private static readonly TextWordsResponse NoTexts = new([], [], true);

    private static AssessmentWordResult Result(string form) => new(form, "no-analysis", false, "Complete", 1, null);

    private static AssessCommandResponse Parsed() =>
        WorkspaceContextTests.Assessment() with { Words = Words.Select(Result).ToArray() };

    private static SelectionWordList List(char hash, int kaon, int baga) => new("freq.tsv",
        "sha256:" + new string(hash, 64), new Dictionary<string, int> { ["kaon"] = kaon, ["baga"] = baga });

    private static async Task Parse(WorkspaceContext context, AssessCommandResponse parsed)
    {
        var evidence = new WorkspaceEvidence(parsed, DateTimeOffset.UtcNow, false);
        context.Assess.Restore(evidence);
        context.PublishEvidence(evidence);
        await context.EvidencePublication;
    }

    private static string[] OnWordsPage(TextsPageModel texts) => Words
        .Select(word => texts.Words.Rows.Single(row => row.Form == word).Listed.Row.PlacesText).ToArray();

    private static string[] OnAssessPage(WorkspaceContext context)
    {
        var rows = context.Assess.Words.AllRows.ToDictionary(row => row.Word);
        return Words.Select(word => rows[word].WordRow.PlacesText).ToArray();
    }

    [Fact]
    public void ParseThenReopenKeepsTheListCountsOnWordsAssessAndCompareReadingTheListOnce()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = WorkspaceContextTests.NewContextWithFake();
            var texts = new TextsPageModel(context);
            var parsed = Parsed();
            await using Owner owner = await WorkspaceContextTests.OpenCapturedSelectionAsync(fake, context, NoTexts,
                null, Words, fixture => fixture.SaveDefaultSelection([], Words, List('c', 12, 3)));
            await context.SelectionReads.ListCountsPending;
            Assert.Equal(["×12", "×3", string.Empty], OnWordsPage(texts));

            owner.Fixture.RecordAssessment(parsed);
            await Parse(context, parsed);
            await context.SelectionReads.ListCountsPending;
            await Parse(context, parsed);

            Assert.Contains(owner.Opens, request => request.ShownAssessment is not null);
            Assert.Equal(1, context.SelectionReads.ListReadCount);
            Assert.Equal(["×12", "×3", string.Empty], OnWordsPage(texts));
            Assert.Equal(["×12", "×3", string.Empty], OnAssessPage(context));
            Assert.Equal(["12 in the word list", "3 in the word list", string.Empty],
                Words.Select(word => context.Assess.Words.AllRows.Single(row => row.Word == word).WordRow.PlacesTooltip));
            var cell = Assert.Single(context.Assess.Compare.Cells, cell => cell.WordCount == Words.Length);
            Assert.Equal("15 in the word list", cell.PlacesText);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AChangedListShowsItsNewCountsNeverTheOldOnesForTheSameWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = WorkspaceContextTests.NewContextWithFake();
            var texts = new TextsPageModel(context);
            var parsed = Parsed();
            await using Owner owner = await WorkspaceContextTests.OpenCapturedSelectionAsync(fake, context, NoTexts,
                null, Words, fixture => fixture.SaveDefaultSelection([], Words, List('c', 12, 3)));
            await context.SelectionReads.ListCountsPending;
            owner.Fixture.RecordAssessment(parsed);
            await Parse(context, parsed);
            await context.SelectionReads.ListCountsPending;
            Assert.Equal(["×12", "×3", string.Empty], OnAssessPage(context));

            owner.Fixture.SaveDefaultSelection([], Words, List('d', 99, 7));
            var reload = context.SelectionReads.ReloadAsync(owner.Fixture.ProjectPath, [], Words);
            await reload;
            Assert.True(context.SelectionReads.ListCounts is null || context.SelectionReads.ListCounts["kaon"] == 99);
            await context.SelectionReads.ListCountsPending;

            Assert.Equal(["×99", "×7", string.Empty], OnWordsPage(texts));
            Assert.Equal(["×99", "×7", string.Empty], OnAssessPage(context));
            Assert.Equal(2, context.SelectionReads.ListReadCount);
        }, TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(false, "kaon\nbaga\nplain\ntyped")]
    [InlineData(false, "kaon\nother")]
    [InlineData(true, "kaon\nbaga\nplain\ntyped")]
    [InlineData(true, "kaon\nother")]
    public void WordsPastedBesideASavedListThatAreNotItsWordsShowNoCountFromItOnWordsOrAssess(bool afterParse,
        string pasted)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = WorkspaceContextTests.NewContextWithFake();
            var texts = new TextsPageModel(context);
            var parsed = Parsed() with { Words = Words.Append("typed").Append("other").Select(Result).ToArray() };
            await using Owner owner = await WorkspaceContextTests.OpenCapturedSelectionAsync(fake, context, NoTexts,
                null, Words, fixture => fixture.SaveDefaultSelection([], Words, List('c', 12, 3)));
            await context.SelectionReads.ListCountsPending;
            Assert.Equal(["×12", "×3", string.Empty], OnWordsPage(texts));
            if (afterParse)
            {
                owner.Fixture.RecordAssessment(parsed);
                await Parse(context, parsed);
                await context.SelectionReads.ListCountsPending;
            }

            context.Selection.PastedWords = pasted;
            await context.EvidencePublication;
            await context.SelectionReads.ListCountsPending;

            Assert.Null(context.SelectionReads.Refusal);
            Assert.Equal(afterParse, owner.Opens[^1].ShownAssessment is not null);
            Assert.Null(context.SelectionReads.Summary!.WordListSha256);
            Assert.Null(context.SelectionReads.ListCounts);
            Assert.All(context.SelectionReads.Summary.Words, word => Assert.Null(word.PlaceCount));
            foreach (var word in pasted.Split('\n').Where(Words.Contains))
                Assert.Equal(string.Empty, texts.Words.Rows.Single(row => row.Form == word).Listed.Row.PlacesText);
            if (afterParse)
                Assert.All(context.Assess.Words.AllRows.Where(row => pasted.Split('\n').Contains(row.Word)),
                    row => Assert.Equal(string.Empty, row.WordRow.PlacesText));
            Assert.Equal(1, context.SelectionReads.ListReadCount);

            context.Selection.PastedWords = string.Join("\n", Words);
            await context.EvidencePublication;
            await context.SelectionReads.ListCountsPending;
            Assert.Equal(["×12", "×3", string.Empty], OnWordsPage(texts));
        }, TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADamagedListShowsBlankWithoutRefusingTheOpenTheWindowTakesAfterAParse(bool keepHash)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = WorkspaceContextTests.NewContextWithFake();
            var texts = new TextsPageModel(context);
            var parsed = Parsed();
            await using Owner owner = await WorkspaceContextTests.OpenCapturedSelectionAsync(fake, context, NoTexts,
                null, Words, fixture => fixture.SaveDefaultSelection([], Words, List('c', 12, 3)));
            owner.Fixture.RecordAssessment(parsed);
            await Parse(context, parsed);
            await context.SelectionReads.ListCountsPending;
            owner.Fixture.SaveDefaultSelection([], Words, List('d', 99, 7));
            owner.Fixture.DamageDefaultWordList(keepHash);

            await Parse(context, parsed);
            await context.SelectionReads.ListCountsPending;

            Assert.Null(context.SelectionReads.Refusal);
            Assert.NotNull(owner.Opens[^1].ShownAssessment);
            Assert.All(OnWordsPage(texts), text => Assert.Equal(string.Empty, text));
            Assert.All(OnAssessPage(context), text => Assert.Equal(string.Empty, text));
            var reads = context.SelectionReads.ListReadCount;
            await Parse(context, parsed);
            await context.SelectionReads.ListCountsPending;
            Assert.Equal(reads, context.SelectionReads.ListReadCount);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AssessAndCompareShowNoCountForAPlainListRatherThanZeroOnEveryWordAndNothingIsRead()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = WorkspaceContextTests.NewContextWithFake();
            _ = new TextsPageModel(context);
            await using Owner owner = await WorkspaceContextTests.OpenCapturedSelectionAsync(fake, context, NoTexts,
                Parsed(), Words, fixture => fixture.SaveDefaultSelection([], Words));

            Assert.All(context.Assess.Words.AllRows, row =>
            {
                Assert.Equal(string.Empty, row.WordRow.PlacesText);
                Assert.Null(row.OccurrenceCount);
            });
            Assert.All(context.Assess.Compare.Cells, cell => Assert.False(cell.HasPlaces));
            Assert.Equal(0, context.SelectionReads.ListReadCount);
        }, TimeSpan.FromSeconds(60));
    }
}
