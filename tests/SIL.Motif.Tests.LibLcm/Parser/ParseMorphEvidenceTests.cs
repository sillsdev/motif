using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Parser;

public sealed class ParseMorphEvidenceTests
{
    private const string Form = "11111111-1111-1111-1111-111111111111";
    private const string Msa = "22222222-2222-2222-2222-222222222222";
    private const string Other = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public void EveryApprovedReadingMustMatchEvenWhenExtraReadingsExist()
    {
        var first = Analysis(Form, Msa);
        var second = Analysis(Other, Msa);
        var result = MorphologyCorrectness.Compare(Row(first, Analysis(Form, Other)),
            [Expected(first), Expected(second)]);
        Assert.Equal(2, result.Expected);
        Assert.Equal(1, result.Matched);
        Assert.Equal("unmatched", result.Status);
        Assert.Equal("covered", MorphologyCorrectness.Compare(Row(first, second, Analysis(Form, Other)),
            [Expected(first), Expected(second)]).Status);
    }

    [Fact]
    public void CompleteCoverageCannotCompleteAnInterruptedSearch()
    {
        var analysis = Analysis(Form, Msa);
        var first = Row(analysis) with { Capped = true, TimedOut = true };
        var result = MorphologyCorrectness.Compare(first, [Expected(analysis)]);
        Assert.Equal(1, result.Matched);
        Assert.Equal("incomplete", result.Status);
        var later = Row(analysis) with { Index = 1 };
        Assert.Equal("covered", MorphologyCorrectness.Compare(later, [Expected(analysis)]).Status);
    }

    [Fact]
    public void MorphOrderInflectionAndGuessedTextAreConstitutive()
    {
        var first = new ParseMorph(Form, Msa, Other, null);
        var second = new ParseMorph(Other, Msa, null, null);
        var expected = Expected(new ParseAnalysis([first, second]));
        Assert.Equal("unmatched", MorphologyCorrectness.Compare(Row(new ParseAnalysis([second, first])), [expected]).Status);
        Assert.Equal("unmatched", MorphologyCorrectness.Compare(Row(new ParseAnalysis([first with { InflType = null }, second])), [expected]).Status);
        var guessed = new ParseAnalysis([first with { GuessedString = "guessed" }]);
        var approved = new ApprovedMorphology([new ApprovedMorph(first.Form, first.Msa, first.InflType, ["different", "guessed"])]);
        Assert.Equal("covered", MorphologyCorrectness.Compare(Row(guessed), [approved]).Status);
        Assert.Equal("unmatched", MorphologyCorrectness.Compare(Row(guessed),
            [approved with { Morphs = [approved.Morphs[0] with { Forms = ["different"] }] }]).Status);
    }

    [Fact]
    public void UnknownProjectionAndAbsentExpectationsAreExplicit()
    {
        var analysis = Analysis(Form, Msa);
        Assert.Equal("unavailable", MorphologyCorrectness.Compare(Row() with { Unavailable = ["source IDs missing"] }, [Expected(analysis)]).Status);
        Assert.Equal("no-expectations", MorphologyCorrectness.Compare(Row(analysis), []).Status);
        var interrupted = MorphologyCorrectness.Compare(Row(analysis) with
            { Capped = true, Unavailable = ["missing source identity"] }, [Expected(analysis)]);
        Assert.Equal("incomplete", interrupted.Status);
        Assert.Single(interrupted.Unavailable);
        var missingApproved = new ApprovedMorphology([new(null, Msa, null, ["text"])]);
        Assert.Equal("unavailable", MorphologyCorrectness.Compare(Row(analysis), [missingApproved]).Status);
    }

    [Fact]
    public void WirePreservesDuplicateSurfacesByCaseAndRejectsCorruption()
    {
        var row = Row(Analysis(Form, Msa));
        var wire = JsonSerializer.Serialize(row, ParseMorphEvidence.JsonOptions);
        var next = JsonSerializer.Serialize(row with { Index = 1 }, ParseMorphEvidence.JsonOptions);
        Assert.Equal(2, ParseMorphEvidence.Read(wire + "\n" + next, ["word", "word"]).Count);
        Assert.Throws<InvalidDataException>(() => ParseMorphEvidence.Read(wire + "\n" + wire, ["word", "word"]));
        Assert.Throws<InvalidDataException>(() => ParseMorphEvidence.Read(wire.Replace("/v1", "/v2"), ["word"]));
        Assert.Throws<InvalidDataException>(() => ParseMorphEvidence.Read(wire.Replace("\"index\":0", "\"index\":0,\"index\":1"), ["word"]));
        Assert.Throws<InvalidDataException>(() => ParseMorphEvidence.Read(wire.Replace(Form, "42"), ["word"]));
        Assert.Throws<InvalidDataException>(() => ParseMorphEvidence.Read(
            wire.Replace("\"" + Form + "\"", "null").Replace("\"guessedString\":null", "\"guessedString\":\"word\""), ["word"]));
        Assert.Throws<InvalidDataException>(() => ParseMorphEvidence.Read(wire.Replace("\"capped\":false,", ""), ["word"]));
        Assert.Throws<InvalidDataException>(() => ParseMorphEvidence.Read(wire.Replace("\"capped\":false", "\"capped\":false,\"invented\":true"), ["word"]));
    }

    private static ParseAnalysis Analysis(string form, string msa) => new([new(form, msa, null, null)]);
    private static ApprovedMorphology Expected(ParseAnalysis analysis) => new(analysis.Morphs
        .Select(morph => new ApprovedMorph(morph.Form, morph.Msa, morph.InflType, [])).ToArray());
    private static ParseWordEvidence Row(params ParseAnalysis[] analyses) =>
        new(ParseMorphEvidence.Schema, 0, "word", 12, false, false, false, analyses, []);
}
