using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>The class list a person copies from: each line leads with the token a pattern takes.</summary>
public sealed class PhonValuesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("phon-values").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private const string Project = """
        <?xml version="1.0" encoding="utf-8"?>
        <languageproject version="7000072">
        <rt class="PhNCSegments" guid="a" ownerguid="p"><Abbreviation><AUni ws="en">V</AUni></Abbreviation><Name><AUni ws="en">Vowels</AUni></Name><Segments><objsur guid="s1" t="r" /><objsur guid="s2" t="r" /></Segments></rt>
        <rt class="PhNCSegments" guid="b" ownerguid="p"><Abbreviation><AUni ws="en">F-V</AUni></Abbreviation><Name><AUni ws="en">Exact featured V</AUni></Name></rt>
        <rt class="PhNCFeatures" guid="c" ownerguid="p"><Abbreviation><AUni ws="fr">LAB</AUni><AUni ws="en"></AUni></Abbreviation><Name><AUni ws="fr">Labial</AUni></Name></rt>
        <rt class="FsClosedFeature" guid="f1" ownerguid="phon"><Abbreviation><AUni ws="en">voice</AUni></Abbreviation><Name><AUni ws="en">Voiced</AUni></Name></rt>
        <rt class="FsClosedFeature" guid="f2" ownerguid="msa"><Abbreviation><AUni ws="en">tense</AUni></Abbreviation><Name><AUni ws="en">Tense</AUni></Name></rt>
        <rt class="LangProject" guid="lp"><AnalysisWss><Uni>en fr</Uni></AnalysisWss><PhFeatureSystem><objsur guid="phon" t="o" /></PhFeatureSystem></rt>
        </languageproject>
        """;

    [Fact]
    public void TheProjectFileIsReadWithoutLibLcmAndEachClassCarriesTheTokenToType()
    {
        var path = Path.Combine(_folder, "p.fwdata");
        File.WriteAllText(path, Project);
        var outcome = PhonValues.ForGrammar(path);
        Assert.True(outcome.Succeeded);
        var values = outcome.Value!;
        Assert.Equal(["Exact featured V", "Labial", "Vowels"], values.NaturalClasses.Select(value => value.Name));
        var vowels = values.NaturalClasses.Single(value => value.Name == "Vowels");
        Assert.Equal(("V", "V", 2), (vowels.Token, vowels.Abbreviation, vowels.Members));
        Assert.Equal("F-V", values.NaturalClasses.Single(value => value.Name == "Exact featured V").Token);
        var labial = values.NaturalClasses.Single(value => value.Name == "Labial");
        Assert.Equal("LAB", labial.Abbreviation);
        Assert.Equal(["Voiced"], values.Features.Select(value => value.Name));
    }

    [Theory]
    [InlineData("V", "V")]
    [InlineData("F-V", "F-V")]
    [InlineData("Exact featured V", "[Exact featured V]")]
    [InlineData("A(B)", "[A(B)]")]
    public void ATokenIsBracketedOnlyWhenThePatternParserWouldSplitIt(string text, string token) =>
        Assert.Equal(token, PhonValues.PatternToken(text));

    [Fact]
    public void AClassWithNoAbbreviationIsTypedByItsName()
    {
        var value = PhonValues.ClassValue("Nasals", "", 3);
        Assert.Null(value.Abbreviation);
        Assert.Equal("Nasals", value.Token);
    }

    [Fact]
    public void TheStageTimingGoesToTheCallerWhenAsked()
    {
        var path = Path.Combine(_folder, "p.fwdata");
        File.WriteAllText(path, Project);
        var lines = new List<string>();
        PhonValues.ForGrammar(path, "grammar", lines.Add);
        Assert.Single(lines);
        Assert.StartsWith("timing: read the classes and features from the project file ", lines[0]);
    }
}
