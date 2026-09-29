using SIL.Motif.Commands.Queries;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class TryWordRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task AWordInNoTextIsTracedThroughTheRealClient()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var word = SeededProject.FirstForm;
        var textWords = await project.Client.ListTextWordsAsync(
            new TextWordsRequest(project.FwDataPath, []), CancellationToken.None);
        Assert.True(textWords.Succeeded, textWords.Refusal?.Message);
        Assert.DoesNotContain(textWords.Value!.Words, row => row.Form == word);

        project.Behave(new { traceSignature = "-" });
        var noParse = await project.Client.TraceWordAsync(new WordTraceRequest(project.FwDataPath, word),
            CancellationToken.None);
        Assert.True(noParse.Succeeded, noParse.Refusal?.Message);
        Assert.False(noParse.Value!.Parsed);
        Assert.True(noParse.Value.Complete);

        project.Behave(new { traceSignature = "-", traceCapped = true });
        var capped = await project.Client.TraceWordAsync(new WordTraceRequest(project.FwDataPath, word),
            CancellationToken.None);
        Assert.True(capped.Succeeded, capped.Refusal?.Message);
        Assert.False(capped.Value!.Complete);
        Assert.Contains("\"capped\":true", capped.Value.DiagnosticJson, StringComparison.Ordinal);

        project.Behave(new { traceSignature = "-", traceTimedOut = true });
        var timedOut = await project.Client.TraceWordAsync(new WordTraceRequest(project.FwDataPath, word),
            CancellationToken.None);
        Assert.True(timedOut.Succeeded, timedOut.Refusal?.Message);
        Assert.False(timedOut.Value!.Complete);
        Assert.Contains("\"timedOut\":true", timedOut.Value.DiagnosticJson, StringComparison.Ordinal);
        Assert.Equal(3, project.Invocations().Count(invocation => invocation == "parse"));
    }
}
