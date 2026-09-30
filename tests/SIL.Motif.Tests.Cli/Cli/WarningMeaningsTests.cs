using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class WarningMeaningsTests
{
    private static readonly WarningMeanings English = WarningMeanings.Load(CultureInfo.GetCultureInfo("en"));

    // Words the parser uses about itself that a linguist reading the Warnings page cannot act on.
    private static readonly Regex EngineWords = new(
        @"\b(MSA|msa|stratum|strata|bucket|NFD|nfd|fwdata|HermitCrab|flid|GUID)\b", RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("grammar.msa.no-rule-form-allomorphs")]
    [InlineData("grammar.environment.invalid")]
    [InlineData("conversion.unsegmentable-form")]
    [InlineData("grammar.phoneme.nfd-collision")]
    [InlineData("hc-partial-morpheme")]
    [InlineData("hc-stem-no-grammatical-category")]
    public void KnownCodeHasATitleAndAOneSentenceMeaningInLinguistWords(string code)
    {
        var meaning = English.For(code, parserGroupName: "Parser finding");

        Assert.True(meaning.IsKnown);
        Assert.NotEqual("Parser finding", meaning.Title);
        Assert.InRange(meaning.Title.Length, 3, 40);
        Assert.NotNull(meaning.Meaning);
        Assert.Single(Regex.Matches(meaning.Meaning!, @"[.!?](?:\s|$)"));
        Assert.DoesNotMatch(EngineWords, meaning.Title + " " + meaning.Meaning);
    }

    [Fact]
    public void EveryEntryIsShortPlainAndDistinct()
    {
        Assert.NotEmpty(English.Codes);
        var titles = English.Codes.Select(code => English.For(code)).ToList();
        Assert.All(titles, meaning =>
        {
            Assert.InRange(meaning.Title.Length, 3, 40);
            Assert.InRange(meaning.Meaning!.Length, 20, 160);
            Assert.EndsWith(".", meaning.Meaning);
            Assert.DoesNotMatch(EngineWords, meaning.Title + " " + meaning.Meaning);
            Assert.DoesNotContain(".", meaning.Title);
        });
        Assert.Equal(titles.Count, titles.Select(meaning => meaning.Title).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void UnknownCodeKeepsTheParsersGroupNameAndHasNoMeaning()
    {
        var meaning = English.For("grammar.future.thing", parserGroupName: "Future thing could not be loaded");

        Assert.False(meaning.IsKnown);
        Assert.Equal("Future thing could not be loaded", meaning.Title);
        Assert.Null(meaning.Meaning);
    }

    [Theory]
    [InlineData("grammar.future.some-new-check", "Some new check")]
    [InlineData("hc-some-future-check", "Some future check")]
    [InlineData("substrate.position_unmapped_again", "Position unmapped again")]
    [InlineData("PlainWord", "Plain word")]
    public void UnknownCodeWithoutAGroupNameIsHumanisedNeverShownRaw(string code, string title)
    {
        var meaning = English.For(code);

        Assert.Equal(title, meaning.Title);
        Assert.DoesNotContain(".", meaning.Title);
        Assert.DoesNotContain("-", meaning.Title);
    }

    [Fact]
    public void MissingCodeReadsAsAGrammarWarning()
    {
        Assert.Equal("Grammar warning", English.For(null).Title);
        Assert.Equal("Grammar warning", English.For("  ").Title);
    }

    [Theory]
    [InlineData("PhEnvironment", "Environment")]
    [InlineData("MoForm", "Allomorph")]
    [InlineData("PhPhoneme", "Phoneme")]
    [InlineData("lex_entry", "Lexical entry")]
    [InlineData("MoInflAffMsa", "Grammatical info")]
    [InlineData("MoSomethingNew", "Something new")]
    [InlineData(null, "Grammar-wide")]
    public void SubjectKindsReadInFieldWorksWords(string? kind, string label)
    {
        Assert.Equal(label, English.KindLabel(kind));
    }

    [Fact]
    public void AnotherCultureFallsBackToEnglishForEachEntry()
    {
        var french = WarningMeanings.Load(CultureInfo.GetCultureInfo("fr"));

        Assert.Equal(English.For("grammar.environment.invalid"), french.For("grammar.environment.invalid"));
    }
}
