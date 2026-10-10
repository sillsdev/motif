using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class SelectionSummaryCountsTests
{
    [Fact]
    public async Task CountsAndAssessmentLookupsUseCompactFactsBeforeAnyWordRowOrTextDetailIsLoaded()
    {
        var firstText = Guid.NewGuid();
        var secondText = Guid.NewGuid();
        var firstWord = Guid.NewGuid();
        var homograph = Guid.NewGuid();
        var otherWord = Guid.NewGuid();
        var first = new TextWordsProjectedText(firstText, "First", [Line(
            Token("same", firstWord, 0, "en", "fr"), Token("other", otherWord, 1, "en"))], []);
        var second = new TextWordsProjectedText(secondText, "Second", [Line(Token("same", homograph, 0, "en"))], []);
        using var fixture = new StoredSelectionFixture(new TextWordsProjection([first, second],
            new[] { firstWord, homograph, otherWord }.Select(id =>
                new TextWordsProjectedWordform(id, [], [], 0, false, [])).ToArray()));
        var fake = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
        var reads = new WorkspaceSelection(fake);
        var selection = new SelectionViewModel(fake);
        try
        {
            await reads.ReloadAsync(fixture.ProjectPath, [firstText, secondText], ["fresh"]);
            Assert.Null(reads.Refusal);
            var words = new TextWordsViewModel(fake, selection, reads);
            var reader = reads.Reader!;
            var before = reader.Diagnostics;
            Assert.Equal(3, words.WordCount);
            Assert.Equal(5, words.AllCount);
            Assert.Equal(5, words.NotPresentFilterCount);
            Assert.Equal(0, words.ApprovedFilterCount);
            Assert.Equal(3, words.OccurrenceCount);
            Assert.Equal(2, words.OccurrenceCountOf("same"));
            Assert.Equal(1, words.OccurrenceCountOf("other"));
            Assert.Equal(0, words.OccurrenceCountOf("fresh"));
            Assert.Null(words.OccurrenceCountOf("absent"));
            Assert.Null(words.WordformIdOf("same"));
            Assert.Equal(otherWord, words.WordformIdOf("other"));
            Assert.Null(words.WordformIdOf("fresh"));
            words.SearchText = "absent";
            words.StatusFilter = WordProjectStatus.Approved;
            Assert.Equal(2, words.OccurrenceCountOf("same"));
            Assert.Equal(5, words.NotPresentFilterCount);
            Assert.Equal(before.QueriesIssued, reader.Diagnostics.QueriesIssued);
            Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
            Assert.Equal(0, reader.Diagnostics.WordformRowsDeserialized);
            Assert.Equal(0, reader.Diagnostics.LiveRowModels);
            Assert.Equal(0, reader.Diagnostics.LiveTokenModels);

            await reads.ReloadAsync(fixture.ProjectPath, [], ["sole"]);
            Assert.Equal(1, words.WordCount);
            Assert.Equal(0, words.OccurrenceCount);
            Assert.Equal(0, words.OccurrenceCountOf("sole"));
            Assert.Null(words.OccurrenceCountOf("same"));
            fake.SelectionReaderHandler = (_, _) => Task.FromResult(CommandOutcome<SelectionReader>.Refused(
                new Refusal("texts.evidence-changed", FailureReason.Refused, "Reopen the Selection.")));
            await reads.ReloadAsync(fixture.ProjectPath, [], []);
            Assert.Equal(0, words.WordCount);
            Assert.Equal(0, words.AllCount);
            Assert.Equal("texts.evidence-changed", words.Refusal!.Code);
        }
        finally
        {
            await reads.StopAsync();
        }
    }

    private static TextWordsProjectedLine Line(params TextWordsProjectedToken[] tokens) =>
        new(1, string.Join(" ", tokens.Select(token => token.Text)), tokens, Guid.NewGuid(), Guid.NewGuid(), true);

    private static TextWordsProjectedToken Token(string form, Guid id, int index, params string[] writingSystems) =>
        new(form, writingSystems.Select(ws => new WritingSystemText(form, ws)).ToArray(), id,
            "unanalysed", null, null, null, null, index, null);
}
