using System.Linq;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class MultiReadingTypesReaderTests
{
    private static ParserReadingMorph Stem(string form) => new(form, "root", "v", null, false, null);

    private static ParserReadingMorph Affix(string form, string gloss, string category = "") =>
        new(form, gloss, category, null, false, null);

    private static AssessmentWordResult Word(string word, params ParserReading[] readings) =>
        new(word, "Parsed", false, "complete", 1, null) { Readings = readings };

    [Fact]
    public void WordTypesWithMoreThanOneReadingAreCountedAndGroupedByMorphemePattern()
    {
        ParserReading Reading(params ParserReadingMorph[] morphs) => new(morphs);
        var words = new[]
        {
            Word("pikisha", Reading(Stem("pik"), Affix("-ish", "CAUS"), Affix("-a", "FV")),
                Reading(Stem("pikish"), Affix("-a", "FV"))),
            Word("lisha", Reading(Stem("l"), Affix("-ish", "CAUS"), Affix("-a", "FV")),
                Reading(Stem("lish"), Affix("-a", "FV"))),
            Word("pigia", Reading(Stem("pig"), Affix("-i", "APPL"), Affix("-a", "FV")),
                Reading(Stem("pig"), Affix("-i", "", "appl"), Affix("-a", "FV"))),
            Word("kula", Reading(Stem("kul"), Affix("-a", "FV"))),
            new AssessmentWordResult("zzz", "NoParse", false, "complete", 1, null),
            new AssessmentWordResult("slow", "TimedOut", true, "timed-out", 1, null),
        };

        var report = MultiReadingTypesReader.Read(words);

        Assert.Equal(5, report.MeasuredTypes);
        Assert.Equal(3, report.Types);
        Assert.Collection(report.Groups,
            first =>
            {
                Assert.Equal("stem+CAUS+FV | stem+FV", first.Pattern);
                Assert.Equal(2, first.Types);
                Assert.Equal(["lisha", "pikisha"], first.Examples);
            },
            second =>
            {
                Assert.Equal("stem+APPL+FV | stem+appl+FV", second.Pattern);
                Assert.Equal(1, second.Types);
            });
    }

    [Fact]
    public void AGrammarWithNoDoubledTypesReportsZeroAndNoGroups()
    {
        var report = MultiReadingTypesReader.Read([Word("kula", new ParserReading([Stem("kul")]))]);

        Assert.Equal((1, 0), (report.MeasuredTypes, report.Types));
        Assert.Empty(report.Groups);
    }
}
