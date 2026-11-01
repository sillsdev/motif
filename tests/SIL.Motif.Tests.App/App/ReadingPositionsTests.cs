using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that readings shown in the project's order still pair with the parser's emitted analyses, so a grade
/// or a staged change never lands on a different reading than the one the person sees.
/// </summary>
public sealed class ReadingPositionsTests
{
    private static ParserReading Reading(string form, int? source) =>
        new([new ParserReadingMorph(form, string.Empty, string.Empty, null, false, null)]) { SourceIndex = source };

    [Fact]
    public void AReadingIsFoundByTheEmittedPositionItCameFrom()
    {
        ParserReading[] shown = [Reading("c", 2), Reading("a", 0), Reading("b", 1)];

        Assert.Equal("a", ReadingPositions.For(shown, 0)!.Morphs[0].Form);
        Assert.Equal("c", ReadingPositions.For(shown, 2)!.Morphs[0].Form);
        Assert.Null(ReadingPositions.For(shown, 3));
        Assert.Equal([2, 0, 1], ReadingPositions.ShownOrder(shown, 3));
    }

    [Fact]
    public void ReadingsStoredBeforeTheReadingOrderKeepTheirPositions()
    {
        ParserReading[] stored = [Reading("a", null), Reading("b", null)];

        Assert.Equal("b", ReadingPositions.For(stored, 1)!.Morphs[0].Form);
        Assert.Equal([0, 1], ReadingPositions.ShownOrder(stored, 2));
        Assert.Equal([0, 1, 2], ReadingPositions.ShownOrder(null, 3));
    }

    [Fact]
    public void TheNumberOfAnAnalysisIsItsShownPositionSoOneReadingHasOneNumber()
    {
        ParserReading[] shown = [Reading("c", 2), Reading("a", 0), Reading("b", 1)];

        Assert.Equal([2, 3, 1], Enumerable.Range(0, 3).Select(index => ReadingPositions.NumberOf(shown, index, 3)));
        Assert.Equal([1, 2], Enumerable.Range(0, 2).Select(index => ReadingPositions.NumberOf(null, index, 2)));
    }

    [Fact]
    public void ASourceIndexThatIsNullIsNotWritten()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Reading("a", null), SIL.Motif.Host.Parser.ParseMorphEvidence.JsonOptions);

        Assert.DoesNotContain("sourceIndex", json);
        Assert.Contains("\"sourceIndex\":4", System.Text.Json.JsonSerializer.Serialize(Reading("a", 4),
            SIL.Motif.Host.Parser.ParseMorphEvidence.JsonOptions));
    }
}
