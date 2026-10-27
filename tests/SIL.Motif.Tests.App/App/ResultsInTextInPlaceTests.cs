using System.Collections.Specialized;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// An evidence publication replaces the reader under a shown Text. The lines, their words and the open card are kept
/// while their content is unchanged, only the lines that read differently are replaced, and a card whose word is gone
/// closes.
/// </summary>
public sealed partial class ResultsInTextViewModelTests
{
    private async Task<(ResultsInTextViewModel InText, SelectionModelFixture Fixture, TextWordsResponse Source,
        AssessViewModel Assess)> LoadedForRepublish()
    {
        AssessViewModel? assess = null;
        var (inText, _, _) = await Loaded(captureAssess: model => assess = model);
        var fixture = _fixtures[^1];
        return (inText, fixture, fixture.Source!, assess!);
    }

    private static async Task Settled(ResultsInTextViewModel inText)
    {
        await inText.SelectionRefresh;
        if (inText.LinePages is { } pages) await pages.Pending;
        await inText.ReadStateRefresh;
    }

    private static TextWordsResponse WithLines(TextWordsResponse source, Func<TextLine, TextLine> change) =>
        source with { Texts = source.Texts.Select(text => text with { Lines = text.Lines.Select(change).ToArray() }).ToArray() };

    private static TextLine WithToken(TextLine line, int index, TextToken token) => line with
    {
        Tokens = line.Tokens.Select(existing => existing.OccurrenceIndex == index && existing.Form is not null ? token : existing)
            .ToArray(),
    };

    [Fact]
    public void AnEqualPublicationKeepsEveryLineAndTheOpenCard()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, fixture, source, assess) = await LoadedForRepublish();
            var lines = SelectionModelFixture.VisibleLines(inText);
            Assert.Equal(2, lines.Count);
            var firstTokens = lines.Select(line => line.Tokens.ToArray()).ToArray();
            var word = lines[0].Tokens.First(token => token.Form == "mtoto");
            inText.SelectToken(word);
            var card = inText.SelectedToken;
            Assert.NotNull(card);
            var pages = inText.LinePages;
            var created = 0;
            var notified = new int[lines.Count];
            inText.NativeModelCreated += _ => created++;
            for (var index = 0; index < lines.Count; index++)
            {
                var position = index;
                ((INotifyCollectionChanged)lines[index].TokenSource!).CollectionChanged += (_, _) => notified[position]++;
            }

            await fixture.RepublishAsync(source, assess.Result);
            await Settled(inText);

            Assert.Same(pages, inText.LinePages);
            Assert.Equal(lines, SelectionModelFixture.VisibleLines(inText));
            for (var index = 0; index < lines.Count; index++)
                Assert.Equal(firstTokens[index], lines[index].Tokens);
            Assert.Equal(0, created);
            Assert.Equal(new int[lines.Count], notified);
            Assert.Same(card, inText.SelectedToken);
            Assert.True(lines[0].HasOpenCard);
            Assert.Same(card, lines[0].OpenCard);
            // The kept words write against the reader that replaced the old one.
            Assert.Equal(inText.LinePages!.Lines.Count, lines.Count);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AChangedWordReplacesOnlyItsLineAndTheOpenCardStaysOpenOnFreshEvidence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, fixture, source, assess) = await LoadedForRepublish();
            var lines = SelectionModelFixture.VisibleLines(inText);
            var changedTokens = lines[0].Tokens.ToArray();
            var otherTokens = lines[1].Tokens.ToArray();
            inText.SelectToken(changedTokens.First(token => token.Form == "mtoto"));
            var card = inText.SelectedToken!;
            var anchor = card.Occurrence;
            var notified = new int[lines.Count];
            for (var index = 0; index < lines.Count; index++)
            {
                var position = index;
                ((INotifyCollectionChanged)lines[index].TokenSource!).CollectionChanged += (_, _) => notified[position]++;
            }
            var changed = WithLines(source, line => line.Number != 1 ? line :
                WithToken(line, 2, Word("mtoto", Stored(Child, "child"), 2)));

            await fixture.RepublishAsync(changed, assess.Result);
            await Settled(inText);

            Assert.Equal(lines, SelectionModelFixture.VisibleLines(inText));
            Assert.Equal(otherTokens, lines[1].Tokens);
            Assert.Equal(0, notified[1]);
            Assert.True(notified[0] > 0);
            Assert.NotEqual(changedTokens, lines[0].Tokens);
            Assert.Equal(changedTokens.Length, lines[0].Tokens.Count);
            var fresh = lines[0].Tokens.First(token => token.Form == "mtoto");
            Assert.Equal("child", Assert.Single(fresh.ProjectApprovedAnalyses).Gloss);
            // The card did not close; it shows the changed word.
            var open = inText.SelectedToken;
            Assert.NotNull(open);
            Assert.Equal(anchor, open.Occurrence);
            Assert.NotSame(card, open);
            Assert.Equal("child", Assert.Single(open.ProjectApprovedAnalyses).Gloss);
            Assert.True(lines[0].HasOpenCard);
            Assert.Same(open, lines[0].OpenCard);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ACardWhoseWordChangedIdentityClosesWhenThePublicationLands()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, fixture, source, assess) = await LoadedForRepublish();
            var lines = SelectionModelFixture.VisibleLines(inText);
            inText.SelectToken(lines[0].Tokens.First(token => token.Form == "mtoto"));
            Assert.NotNull(inText.SelectedToken);
            var otherTokens = lines[1].Tokens.ToArray();
            var replaced = WithLines(source, line => line.Number != 1 ? line :
                WithToken(line, 2, Word("anapenda", null, 2)));

            await fixture.RepublishAsync(replaced, assess.Result);
            await Settled(inText);

            Assert.Null(inText.SelectedToken);
            Assert.False(lines[0].HasOpenCard);
            Assert.Equal(otherTokens, lines[1].Tokens);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ClickingAndMarkingDoNotTestEveryOccurrenceAgainstTheFilter()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _, _) = await LoadedForRepublish();
            var lines = SelectionModelFixture.VisibleLines(inText);
            inText.Filter = ResultsInTextFilter.Unread;
            var tests = inText.MatchTests;
            Assert.True(tests > 0);
            inText.SelectToken(lines[0].Tokens.First(token => token.Form == "kitabu"));
            inText.SelectToken(lines[1].Tokens.First(token => token.Form == "kitabu"));
            inText.CloseTokenCard();
            Assert.Equal(tests, inText.MatchTests);
            Assert.Equal(2, inText.VisibleHeaders.Count);
            inText.Filter = ResultsInTextFilter.All;
            Assert.Equal(2, inText.VisibleHeaders.Count);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AReloadThatFailsLeavesNothingHeldOnScreen()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var lines = SelectionModelFixture.VisibleLines(inText);
            inText.SelectToken(lines[0].Tokens.First(token => token.Form == "kitabu"));
            Assert.NotNull(inText.SelectedToken);
            client.SelectionReaderHandler = (_, _) => throw new InvalidOperationException("The reader could not open.");

            await client.ReaderOwner.ReloadAsync(ProjectPath, [TextId], []);

            // With no successor, the held lines and card go rather than stay on screen for good.
            Assert.NotNull(client.ReaderOwner.Refusal);
            Assert.Null(inText.SelectedToken);
            Assert.Equal(0, inText.DisplayedLineCount);
            Assert.False(inText.HasLines);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void WhileTheReaderIsBeingReplacedTheShownLinesAndCardStandAndClicksWait()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, fixture, source, assess) = await LoadedForRepublish();
            var lines = SelectionModelFixture.VisibleLines(inText);
            inText.SelectToken(lines[0].Tokens.First(token => token.Form == "mtoto"));
            var card = inText.SelectedToken;
            var tokens = lines[0].Tokens.ToArray();
            var gate = new TaskCompletionSource();

            var reload = fixture.RepublishAsync(source, assess.Result, gate.Task);

            Assert.Same(card, inText.SelectedToken);
            Assert.Equal(2, inText.DisplayedLineCount);
            Assert.False(inText.HasMessage);
            inText.SelectToken(lines[1].Tokens.First(token => token.Form == "kitabu"));
            Assert.Same(card, inText.SelectedToken);
            Assert.Same(lines[0], inText.RealizeLine(inText.VisibleHeaders[0]));

            gate.SetResult();
            await reload;
            await Settled(inText);

            Assert.Same(card, inText.SelectedToken);
            Assert.Equal(tokens, lines[0].Tokens);
            Assert.True(lines[0].HasOpenCard);
        }, TimeSpan.FromSeconds(180));
    }
}
