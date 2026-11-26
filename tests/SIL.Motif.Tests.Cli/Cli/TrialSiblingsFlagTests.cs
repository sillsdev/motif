using SIL.Motif.Cli;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Contract.Commands;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class TrialSiblingsFlagTests
{
    private static Dictionary<string, string> Flags(params (string Name, string Value)[] flags) =>
        flags.ToDictionary(flag => flag.Name, flag => flag.Value);

    [Fact]
    public void WithNoSiblingFlagThereIsNoRequestAndNoRefusal()
    {
        Assert.Null(TrialSiblingsCli.Read(Flags(("touched", "true")), out var refusal));
        Assert.Null(refusal);
    }

    [Fact]
    public void ACorpusTypesFileWithTheSiblingsFlagMakesARequestWithTheDefaultCap()
    {
        var request = TrialSiblingsCli.Read(Flags(("siblings", "true"), ("corpus-types", "types.tsv")), out var refusal);

        Assert.Null(refusal);
        Assert.Equal(("types.tsv", 1000, null), (request!.CorpusTypesPath, request.Max, request.Pattern));
    }

    [Fact]
    public void AnySiblingOptionAsksForSiblingsAndSiblingsNeedTheFile()
    {
        foreach (var flags in new[]
        {
            Flags(("siblings", "true")), Flags(("siblings-max", "5")), Flags(("siblings-pattern", "x.*")), Flags(("siblings", "true"), ("corpus-types", "true")),
        })
        {
            Assert.Null(TrialSiblingsCli.Read(flags, out var refusal));
            Assert.Equal(RefusalCodes.TrialSiblingsInvalid, refusal!.Code);
            Assert.Contains("--corpus-types", refusal.Message);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public void ACapThatIsNotAPositiveWholeNumberIsRefused(string max)
    {
        Assert.Null(TrialSiblingsCli.Read(Flags(("corpus-types", "t"), ("siblings-max", max)), out var refusal));
        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, refusal!.Code);
    }

    [Fact]
    public void NoSiblingsOptsOutAndContradictsTheOtherOptions()
    {
        Assert.Null(TrialSiblingsCli.Read(Flags(("no-siblings", "true")), out var alone));
        Assert.Null(alone);
        Assert.Null(TrialSiblingsCli.Read(Flags(("no-siblings", "true"), ("corpus-types", "t")), out var refusal));
        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, refusal!.Code);
    }

    [Fact]
    public void ThePatternIsKeptAsGiven()
    {
        var request = TrialSiblingsCli.Read(Flags(("corpus-types", "t"), ("siblings-pattern", "ku{form}.*"), ("siblings-max", "7")), out _);

        Assert.Equal(("ku{form}.*", 7), (request!.Pattern, request.Max));
    }

    [Theory]
    [InlineData("--siblings")]
    [InlineData("--siblings-max", "5")]
    public async Task TrialWithSiblingOptionsButNoCorpusTypesFileRefusesBeforeAnyWork(params string[] siblingFlags)
    {
        using var fixture = new SIL.Motif.Tests.TestFixtures.ReviewAcceptanceFixture();
        var words = Path.Combine(fixture.Root, "words.txt");
        File.WriteAllText(words, "a\n");
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;

        var result = await CliProcess.RunAsync(runnerRoot, null, false,
            ["trial", "--project", fixture.Project, "proposal/none", "--words", words, .. siblingFlags, "--json"]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(RefusalCodes.TrialSiblingsInvalid, result.Output + result.Error);
    }

    [Fact]
    public async Task TrialWithSiblingsAndCorpusTypesButNoWordListRefusesNamingWords()
    {
        using var fixture = new SIL.Motif.Tests.TestFixtures.ReviewAcceptanceFixture();
        var corpus = Path.Combine(fixture.Root, "types.tsv");
        File.WriteAllText(corpus, "a\n");
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;

        var result = await CliProcess.RunAsync(runnerRoot, null, false,
            "trial", "--project", fixture.Project, "proposal/none", "--siblings", "--corpus-types", corpus, "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(RefusalCodes.TrialSiblingsInvalid, result.Output + result.Error);
        Assert.Contains("--words", result.Output + result.Error);
    }
}
