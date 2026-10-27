using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class MissingGlossTests
{
    [Fact]
    public void AMorphWithNoGlossShowsThreeStarsNotAQuestionMarkOrDash()
    {
        var row = WordRowViewModel.NotParsed("kitabu", "approved",
            [new ParserReadingMorph("ki", "", "n", null, false, null), new ParserReadingMorph("tabu", "book", "n", null, false, null)]);

        Assert.Equal("*** book", row.Row.Gloss);
    }

    [Fact]
    public void AnUnnamedParserMorphemeShowsThreeStarsForItsGloss()
    {
        var morph = new ParserReadingMorph("?", "", "", null, false, null);

        Assert.Equal("***", ParserReadingMorphViewModel.ForTrace(morph).GlossOrPlaceholder);
    }
}
