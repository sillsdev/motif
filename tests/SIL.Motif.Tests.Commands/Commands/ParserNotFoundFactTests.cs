using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SIL.LCModel;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A parser-unavailable refusal says, as a fact, when the cause is that no PanGloss executable was found, so
/// the window can tell a missing parser from one that is there but would not run.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class ParserNotFoundFactTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.ParserNotFoundFactTests", Guid.NewGuid().ToString("N"));

    public ParserNotFoundFactTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task TheInvokerMarksOnlyAnAbsentExecutableAsNotFound()
    {
        using var queue = new MachinePanGlossQueue(new[]
        {
            @"Local\MotifParserNotFoundTests-" + Guid.NewGuid().ToString("N") + "-0",
            @"Local\MotifParserNotFoundTests-" + Guid.NewGuid().ToString("N") + "-1",
        });
        using var absent = new PanGlossInvoker(executablePath: null, queue);
        using var wontStart = new PanGlossInvoker(Path.Combine(_root, "no-such-pangloss.exe"), queue);
        var project = Path.Combine(_root, "p.fwdata");
        File.WriteAllText(project, "the parser never reads this.");
        var request = new PanGlossRequest.Import(project, Path.Combine(_root, "g.json"));

        var missing = Assert.IsType<PanGlossOutcome.Unavailable>(
            await absent.RunAsync(request, "test", CancellationToken.None));
        var unstartable = Assert.IsType<PanGlossOutcome.Unavailable>(
            await wontStart.RunAsync(request, "test", CancellationToken.None));

        Assert.True(missing.ExecutableMissing);
        Assert.False(unstartable.ExecutableMissing);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGrammarCheckCarriesTheFactOnlyForAMissingParser(bool missing)
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker { Respond = _ => Unavailable(missing) };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.Equal(RefusalCodes.GrammarCheckParserUnavailable, outcome.Refusal!.Code);
        Assert.Equal(missing, outcome.Refusal.Facts.ContainsKey(RefusalFactNames.ParserNotFound));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWordTraceCarriesTheFactOnlyForAMissingParser(bool missing)
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker { Respond = _ => Unavailable(missing) };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.Equal(RefusalCodes.WordTraceParserUnavailable, outcome.Refusal!.Code);
        Assert.Equal(missing, outcome.Refusal.Facts.ContainsKey(RefusalFactNames.ParserNotFound));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAssessmentCarriesTheFactOnlyForAMissingParser(bool missing)
    {
        var cache = _pristine.NewScratch();
        SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        using var _ = cache;
        var assessor = new FakeAssessor("fake-assessor", [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming],
            _ => throw new AssessorUnavailableException("fake-assessor", "no pangloss here")
            {
                ExecutableMissing = missing,
            });
        var invoker = new FakeInvoker { Respond = _ => throw new InvalidOperationException("Not reached.") };

        var outcome = AssessCommand.Run(
            new AssessRequest(fwDataPath, new SelectionRequest(true, [], [], false, null)), NewManagedRoot(),
            assessor, invoker, onProgress: null, CancellationToken.None);

        Assert.Equal(RefusalCodes.AssessParserUnavailable, outcome.Refusal!.Code);
        Assert.Equal(missing, outcome.Refusal.Facts.ContainsKey(RefusalFactNames.ParserNotFound));
    }

    [Fact]
    public void TheAssessorPassesOnAMissingParser()
    {
        var candidate = Directory.CreateDirectory(Path.Combine(_root, "candidate")).FullName;
        File.WriteAllText(Path.Combine(candidate, "source.fwdata"), "<languageproject/>");
        var assessor = new PanGlossAssessor(new FixedCachePaths(Path.Combine(_root, "cache")),
            new FakeInvoker { Respond = _ => Unavailable(missing: true) });
        var scope = new AssessmentScope(["word"], [AssessmentKind.ParseTime], TimeSpan.FromSeconds(1));

        var thrown = Assert.Throws<AssessorUnavailableException>(() =>
            assessor.ProduceAsync(scope, candidate, _ => { }, CancellationToken.None).GetAwaiter().GetResult());

        Assert.True(thrown.ExecutableMissing);
    }

    private static PanGlossOutcome Unavailable(bool missing) =>
        new PanGlossOutcome.Unavailable("Could not find the pangloss executable.") { ExecutableMissing = missing };

    private void Capture(string fwDataPath)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private string NewManagedRoot() => Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;

    private sealed class FixedCachePaths(string root) : IAssessorCachePathResolver
    {
        public string DirectoryFor(string invocationId) => Path.Combine(root, invocationId);
    }
}
