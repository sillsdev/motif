using SIL.Motif.Commands.Queries;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class TryWordRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task AWordInNoTextIsTracedThroughTheRealClient()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var word = SeededProject.FirstForm;
        var textWords = await project.Client.OpenSelectionReaderAsync(
            new SIL.Motif.Commands.SelectionReading.OpenSelectionReaderRequest(project.FwDataPath, [], []), CancellationToken.None);
        Assert.True(textWords.Succeeded, textWords.Refusal?.Message);
        await using var reader = textWords.Value!;
        var summary = await reader.ReadSummaryAsync(new SIL.Motif.Commands.SelectionReading.SelectionViewRequest());
        Assert.True(summary.Succeeded, summary.Refusal?.Message);
        using var read = summary.Value!;
        Assert.DoesNotContain(read.Value.Words, row => row.Key.Form == word);

        project.Behave(new { traceSignature = "-" });
        var noParse = await project.Client.TraceWordAsync(new WordTraceRequest(project.FwDataPath, word),
            CancellationToken.None);
        Assert.True(noParse.Succeeded, noParse.Refusal?.Message);
        Assert.False(noParse.Value!.Parsed);
        Assert.True(noParse.Value.Complete);
        Assert.Equal("complete", noParse.Value.SearchStatus);
        Assert.Null(noParse.Value.StopReason);

        project.Behave(new { traceSignature = "-", traceCapped = true });
        var capped = await project.Client.TraceWordAsync(new WordTraceRequest(project.FwDataPath, word),
            CancellationToken.None);
        Assert.True(capped.Succeeded, capped.Refusal?.Message);
        Assert.False(capped.Value!.Complete);
        Assert.Equal("incomplete", capped.Value.SearchStatus);
        Assert.False(string.IsNullOrWhiteSpace(capped.Value.StopReason));

        project.Behave(new { traceSignature = "-", traceTimedOut = true });
        var timedOut = await project.Client.TraceWordAsync(new WordTraceRequest(project.FwDataPath, word),
            CancellationToken.None);
        Assert.True(timedOut.Succeeded, timedOut.Refusal?.Message);
        Assert.False(timedOut.Value!.Complete);
        Assert.Equal("incomplete", timedOut.Value.SearchStatus);
        Assert.False(string.IsNullOrWhiteSpace(timedOut.Value.StopReason));
        Assert.NotEqual(capped.Value.StopReason, timedOut.Value.StopReason);
        Assert.Equal(3, project.Invocations().Count(invocation => invocation == "parse"));
    }

    [Fact]
    public async Task AStoppedSearchReportedWithAFailingExitIsIncompleteNotRefused()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        project.Behave(new { traceSignature = "-", traceCapped = true, exitCode = 1 });

        var capped = await project.Client.TraceWordAsync(
            new WordTraceRequest(project.FwDataPath, SeededProject.FirstForm), CancellationToken.None);

        Assert.True(capped.Succeeded, capped.Refusal?.Message);
        Assert.False(capped.Value!.Complete);
        Assert.Equal("incomplete", capped.Value.SearchStatus);
        Assert.Contains("search limit", capped.Value.StopReason, StringComparison.Ordinal);
    }
}
