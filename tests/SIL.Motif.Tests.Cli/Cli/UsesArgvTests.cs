using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Pins <c>motif uses</c> on the command line: a project with no stored Assessment, arguments that name no object
/// and no words, and the text it prints.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class UsesArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _managedRoot = Path.Combine(
        Path.GetTempPath(), "motif-uses-argv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UsesOnAProjectWithNoStoredAssessmentIsNotFound()
    {
        var project = pristine.CopyProjectFile();

        var result = await CliProcess.RunAsync(_managedRoot, null, true,
            "uses", "--project", project, "--timing", "phon_rule:6f1d2c3b-0000-4000-8000-000000000001",
            "--words", "motifa,motifb", "--json");

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.NotFound), result.ExitCode);
        Assert.Equal("uses.no-assessment", ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!.Code);
    }

    [Theory]
    [InlineData("uses", "--words", "motifa")]
    [InlineData("uses", "--project", "x.fwdata", "--timing", "phon_rule")]
    [InlineData("uses", "--project", "x.fwdata", "--timing", ":key")]
    [InlineData("uses", "--project", "x.fwdata", "--allomorph")]
    [InlineData("uses", "--project", "x.fwdata", "extra")]
    public async Task InvalidUsesArgumentsPrintTheUsageLine(params string[] arguments)
    {
        var result = await CliProcess.RunAsync(_managedRoot, null, true, arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif uses", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsesNamingNothingIsRefusedAsInvalid()
    {
        var result = await CliProcess.RunAsync(_managedRoot, null, true, "uses", "--project", "x.fwdata", "--json");

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.ExitCode);
        Assert.Equal("uses.invalid-request", ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!.Code);
    }

    [Fact]
    public void UsesTextNamesTheWordsTheirMeaningsTheirTimesAndWhatTheyShare()
    {
        ParserReadingMorph Morph(string form, string gloss) =>
            new(form, gloss, "v", null, false, null) { AllomorphId = "form-" + form, GrammaticalInfoId = "msa-" + form };
        var kat = Morph("kat", "cut");
        AssessmentWordResult Lost(string word) => new(word, "no-analysis", false, "Search completed", 1, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            StoredAnalyses = [new ParserReading([Morph("wa-", "3PL"), kat]) { StoredAnalysisOpinion = ReadingGrade.Approved }],
        };
        var response = SIL.Motif.Commands.Queries.ObjectUsesQuery.Read([Lost("walikata"), Lost("wamekata")],
            [new("morph_rule", "msa-kat", "authored", "analysis", "kat", "walikata", 3, null, 1_500_000)],
            ObjectUseRef.ForMorpheme(kat) with { TimingKind = "morph_rule", TimingKey = "msa-kat" },
            ["walikata", "wamekata", "mtoto"]) with { AssessmentId = "assessment-1" };

        var text = SIL.Motif.Cli.Rendering.CommandTextRenderer.Render(
            SIL.Motif.Contract.Commands.CommandOutcome<ObjectUsesResponse>.Success(response), asJson: false).Output;

        Assert.Equal(string.Join(Environment.NewLine,
            "Uses of kat (assessment-1)",
            "  Your words that use it: 2 words (Lost 2)",
            "    walikata: Lost",
            "    wamekata: Lost",
            "  Words it ran in: 1 word (Lost 1)",
            $"    walikata: Lost, 3 calls, {1.5.ToString("N3", System.Globalization.CultureInfo.CurrentCulture)} ms",
            "  What these words share:",
            "    wa- 3PL: 2 words (walikata, wamekata)",
            "    kat cut: 2 words (walikata, wamekata)",
            "  Not in this Assessment: mtoto",
            ""), text);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
