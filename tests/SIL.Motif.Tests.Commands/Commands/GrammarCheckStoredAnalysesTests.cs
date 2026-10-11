using System;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Preferences;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Pins which stored-analysis choice a grammar check hands PanGloss.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class GrammarCheckStoredAnalysesTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.GrammarCheckStoredAnalysesTests", Guid.NewGuid().ToString("N"));

    public GrammarCheckStoredAnalysesTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData(StoredAnalysisComparison.Budget)]
    [InlineData(StoredAnalysisComparison.All)]
    [InlineData(StoredAnalysisComparison.Off)]
    public void TheSavedChoiceReachesPanGlossWhenTheRequestNamesNone(StoredAnalysisComparison saved)
    {
        var path = CapturedProject();
        var checks = new MemoryDefaultProposalChecksStore();
        checks.SetStoredAnalyses(saved);
        var invoker = Answering();

        var result = GrammarCheckQuery.Query(new GrammarCheckRequest(path), invoker, CancellationToken.None,
            defaultChecks: checks);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(saved, Assert.IsType<PanGlossRequest.GrammarHealth>(Assert.Single(invoker.Requests).Request)
            .StoredAnalyses);
    }

    [Theory]
    [InlineData(StoredAnalysisComparison.Budget, StoredAnalysisComparison.Off)]
    [InlineData(StoredAnalysisComparison.Off, StoredAnalysisComparison.All)]
    [InlineData(StoredAnalysisComparison.All, StoredAnalysisComparison.Budget)]
    public void ARequestsOwnChoiceWinsOverTheSavedOne(StoredAnalysisComparison saved, StoredAnalysisComparison asked)
    {
        var path = CapturedProject();
        var checks = new MemoryDefaultProposalChecksStore();
        checks.SetStoredAnalyses(saved);
        var invoker = Answering();

        var result = GrammarCheckQuery.Query(new GrammarCheckRequest(path, StoredAnalyses: asked), invoker,
            CancellationToken.None, defaultChecks: checks);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(asked, Assert.IsType<PanGlossRequest.GrammarHealth>(Assert.Single(invoker.Requests).Request)
            .StoredAnalyses);
        Assert.Equal(saved, checks.StoredAnalyses);
    }

    [Fact]
    public void AProjectWithNoBaselineNeverStartsPanGlossWhateverTheChoice()
    {
        var invoker = Answering();

        var result = GrammarCheckQuery.Query(
            new GrammarCheckRequest(_pristine.CopyProjectFile(), StoredAnalyses: StoredAnalysisComparison.All),
            invoker, CancellationToken.None, defaultChecks: new MemoryDefaultProposalChecksStore());

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.HasBaseline);
        Assert.Empty(invoker.Requests);
    }

    private string CapturedProject()
    {
        var path = _pristine.CopyProjectFile();
        var managed = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(managed);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), managed);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return path;
    }

    private static FakeInvoker Answering()
    {
        var report = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "GrammarHealth",
            "schema-v4-producer.json"));
        return new FakeInvoker { Respond = _ => new PanGlossOutcome.Completed(report, string.Empty, TimeSpan.Zero) };
    }
}
