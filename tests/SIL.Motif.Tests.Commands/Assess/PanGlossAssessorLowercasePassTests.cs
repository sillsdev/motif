using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Assess;

/// <summary>
/// Pins the lowercase second pass at the one seam Assess and Trial share: the batch the assessor sends.
/// Each word is parsed as written and its different lowercase form is parsed as a wordform of its own.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class PanGlossAssessorLowercasePassTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-lowercase-" + Guid.NewGuid().ToString("N"));
    private readonly string _candidate;
    private readonly StatsCacheStore _paths;

    public PanGlossAssessorLowercasePassTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _candidate = Path.Combine(_root, "candidate");
        Directory.CreateDirectory(_candidate);
        File.WriteAllText(Path.Combine(_candidate, "project.fwdata"), "fake grammar bytes");
        _paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(Path.Combine(_root, "worker")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static LowercasePass Casing(string languageTag) => LowercasePass.For(new CoreWritingSystemDefinition(languageTag));

    private async Task<IReadOnlyList<WordAnalysis>> RunAsync(LowercasePass pass, string[] lexemes, params string[] words)
    {
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-" + Guid.NewGuid().ToString("N")));
        FakeParser.BehaveBesideExecutable(parser, new
        {
            words = lexemes.Select(word => new { word, outcome = "complete" }).ToArray(),
        });
        using var invoker = new PanGlossInvoker(parser,
            new MachinePanGlossQueue(["Local\\MotifLowercaseTests-" + Guid.NewGuid().ToString("N")]));
        var assessor = new PanGlossAssessor(_paths, invoker, BatchInvocationEvidence.DigestFile, (_, _) => pass);
        var produced = await assessor.ProduceAsync(
            new AssessmentScope.Words(words, [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming],
                TimeSpan.FromMilliseconds(700), 123),
            _candidate, CancellationToken.None);
        return Assert.IsType<AssessmentRaw.Batch>(produced.First(item => item.Kind == AssessmentKind.ParseTime).Raw)
            .Analysis.Words;
    }

    [Fact]
    public async Task ASentenceInitialCapitalGetsItsReadingsOnTheLowercaseWordform()
    {
        var rows = await RunAsync(Casing("ceb"), ["balay"], "Balay");

        Assert.Equal(["Balay", "balay"], rows.Select(row => row.Word));
        Assert.Equal(WordOutcome.NoAnalysis, rows[0].Outcome);
        Assert.Null(rows[0].LowercaseOf);
        Assert.Equal(WordOutcome.Analysed, rows[1].Outcome);
        Assert.Equal("Balay", rows[1].LowercaseOf);
    }

    [Fact]
    public async Task AnAlreadyLowercaseWordIsParsedExactlyOnce()
    {
        var rows = await RunAsync(Casing("ceb"), ["balay"], "balay");

        var only = Assert.Single(rows);
        Assert.Equal("balay", only.Word);
        Assert.Null(only.LowercaseOf);
    }

    [Fact]
    public async Task AProperNounKeepsItsOwnReadingsAndAlsoGetsTheLowercasePass()
    {
        var rows = await RunAsync(Casing("ceb"), ["Pidru", "pidru"], "Pidru");

        Assert.Equal(["Pidru", "pidru"], rows.Select(row => row.Word));
        Assert.All(rows, row => Assert.Equal(WordOutcome.Analysed, row.Outcome));
        Assert.Null(rows[0].LowercaseOf);
        Assert.Equal("Pidru", rows[1].LowercaseOf);
    }

    [Fact]
    public async Task AWordAskedForInBothCasesIsNotParsedTwice()
    {
        var rows = await RunAsync(Casing("ceb"), ["balay"], "Balay", "balay");

        Assert.Equal(["Balay", "balay"], rows.Select(row => row.Word));
        Assert.All(rows, row => Assert.Null(row.LowercaseOf));
    }

    [Fact]
    public async Task AnAllCapsWordIsLowercasedWhole()
    {
        var rows = await RunAsync(Casing("ceb"), ["balay"], "BALAY");

        Assert.Equal(["BALAY", "balay"], rows.Select(row => row.Word));
        Assert.Equal(WordOutcome.Analysed, rows[1].Outcome);
    }

    [Fact]
    public async Task TurkishCapitalIAndDottedCapitalIAreLowercasedByIcuForTheWritingSystem()
    {
        var rows = await RunAsync(Casing("tr"), ["ışık", "izmir"], "Işık", "İzmir");

        Assert.Equal(["Işık", "İzmir", "ışık", "izmir"], rows.Select(row => row.Word));
        Assert.All(rows.Skip(2), row => Assert.Equal(WordOutcome.Analysed, row.Outcome));
        Assert.Equal("ışık", Casing("tr").LowercaseOf("Işık"));
        Assert.Equal("iştah", Casing("tr").LowercaseOf("İŞTAH"));
        Assert.Equal("i̇ştah", Casing("en").LowercaseOf("İŞTAH"));
    }

    [Fact]
    public void AProjectThatCannotBeOpenedGetsNoPassSoTheParserReportsItsOwnFailure()
    {
        var pass = LowercasePass.ForProject(Path.Combine(_candidate, "project.fwdata"));

        Assert.Null(pass.LowercaseOf("Balay"));
    }

    [Fact]
    public void ARealProjectsDefaultVernacularCasingDecidesTheLowercaseForm()
    {
        var pass = LowercasePass.ForProject(_pristine.CopyProjectFile());

        Assert.Equal("balay", pass.LowercaseOf("Balay"));
        Assert.Null(pass.LowercaseOf("balay"));
    }
}
