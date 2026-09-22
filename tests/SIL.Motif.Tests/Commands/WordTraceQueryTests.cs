using System;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="WordTraceQuery"/> over a canned trace, the same captured <c>sagd</c> derivation
/// <see cref="SIL.Motif.Tests.PanGloss.PanGlossTracerTests"/> pins at the reader level: no Baseline is a
/// typed Refusal, a completed trace maps to a successful and a failed candidate with the failure explained
/// in FieldWorks' own words, and a declined parser is a typed Refusal rather than an exception.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class WordTraceQueryTests : IDisposable
{
    /// The same real capture <see cref="SIL.Motif.Tests.PanGloss.PanGlossTracerTests"/> pins at the reader level.
    private const string GoldenStandardOutput =
        "sagd\t32+PAST|sag+?d\n" +
        "{\"type\":\"WordAnalysis\",\"inputShape\":\"sagd\",\"children\":[" +
        "{\"type\":\"StratumAnalysisInput\",\"source\":\"S\",\"inputShape\":\"sagd\",\"children\":[]}," +
        "{\"type\":\"StratumAnalysisOutput\",\"source\":\"S\",\"outputShape\":\"sagd\",\"children\":[]}," +
        "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"ed_suffix\",\"subrule\":0,\"outputShape\":\"sag\",\"children\":[" +
        "{\"type\":\"StratumAnalysisOutput\",\"source\":\"S\",\"outputShape\":\"sag\",\"children\":[]}," +
        "{\"type\":\"LexicalLookup\",\"source\":\"S\",\"inputShape\":\"sag\",\"children\":[]}," +
        "{\"type\":\"StratumSynthesisInput\",\"source\":\"S\",\"inputShape\":\"sag\",\"children\":[]}," +
        "{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"ed_suffix\",\"subrule\":0,\"outputShape\":\"sagd\",\"children\":[" +
        "{\"type\":\"StratumSynthesisOutput\",\"source\":\"S\",\"outputShape\":\"sagd\",\"children\":[]}," +
        "{\"type\":\"Successful\",\"outputShape\":\"sagd\",\"children\":[]}]}," +
        "{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"ed_suffix\"," +
        "\"failureReason\":\"NonPartialRuleProhibitedAfterFinalTemplate\",\"inputShape\":\"sag\",\"children\":[]}," +
        "{\"type\":\"Failed\",\"failureReason\":\"PartialParse\",\"outputShape\":\"sag\",\"children\":[]}]}," +
        "{\"type\":\"LexicalLookup\",\"source\":\"S\",\"inputShape\":\"sagd\",\"children\":[]}]}";

    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.WordTraceQueryTests", Guid.NewGuid().ToString("N"));

    public WordTraceQueryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void NoBaselineIsATypedRefusal_AndNeverReachesTheParser()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var invoker = new FakeInvoker
        {
            Respond = _ => throw new InvalidOperationException("Must not reach the parser without a Baseline."),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("wordtrace.no-baseline", outcome.Refusal!.Code);
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public void ACompletedTraceMapsOneSuccessfulAndOneFailedCandidate_WithThePlainEnglishExplanation()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GoldenStandardOutput, string.Empty, TimeSpan.FromMilliseconds(4)),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal("sagd", response.Word);
        Assert.True(response.Parsed);
        Assert.True(response.Complete);
        Assert.Null(response.StopReason);
        Assert.Equal(13, response.StepCount);
        Assert.Equal("ed_suffix", response.DeepestRule);
        Assert.Equal("WordAnalysis", response.Root.Type);
        Assert.Equal(4, response.Root.Children.Count);

        Assert.Equal(2, response.Candidates.Count);
        var succeeded = response.Candidates[0];
        Assert.True(succeeded.Succeeded);
        Assert.Null(succeeded.FailureReason);
        Assert.Null(succeeded.Explanation);
        Assert.Equal("sag", Assert.Single(succeeded.Morphs).Form);
        Assert.Equal("Successful", succeeded.Steps[^1].Type);

        var failed = response.Candidates[1];
        Assert.False(failed.Succeeded);
        Assert.Equal("PartialParse", failed.FailureReason);
        Assert.Equal(
            "This parse does not include all analyzed morphemes. Perhaps the missing morphemes are in an " +
            "inflectional template that is not available at this point in the synthesis.",
            failed.Explanation);
        Assert.Equal("sag", Assert.Single(failed.Morphs).Form);
        Assert.Equal("Failed", failed.Steps[^1].Type);
        Assert.Equal(["LexicalLookup", "Failed"], failed.Steps.TakeLast(2).Select(step => step.Type));
    }

    [Fact]
    public void AnOutcomeOnABranchWithNoLookupOfItsOwnDoesNotBorrowANeighbouringBranchsRoot()
    {
        const string twoBranches =
            "xyz\t\n" +
            "{\"type\":\"WordAnalysis\",\"inputShape\":\"xyz\",\"children\":[" +
            "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"first\",\"outputShape\":\"xy\",\"children\":[" +
            "{\"type\":\"LexicalLookup\",\"source\":\"S\",\"inputShape\":\"xy\",\"children\":[]}," +
            "{\"type\":\"Failed\",\"failureReason\":\"PartialParse\",\"outputShape\":\"xy\",\"children\":[]}]}," +
            "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"second\",\"outputShape\":\"x\",\"children\":[" +
            "{\"type\":\"Failed\",\"failureReason\":\"PartialParse\",\"outputShape\":\"x\",\"children\":[]}]}]}";
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(twoBranches, string.Empty, TimeSpan.FromMilliseconds(1)),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "xyz"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var bySource = outcome.Value!.Candidates.ToDictionary(candidate => candidate.Steps[1].Source!);
        Assert.Equal("xy", Assert.Single(bySource["first"].Morphs).Form);
        Assert.Equal("x", Assert.Single(bySource["second"].Morphs).Form);
        Assert.DoesNotContain(bySource["second"].Steps, step => step.Type == "LexicalLookup");
    }

    [Fact]
    public void AWordWithNoTraceableShapeStillReportsASyntheticRoot()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed("zagz\t-\n", string.Empty, TimeSpan.Zero),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "zagz"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.False(response.Parsed);
        Assert.Empty(response.Candidates);
        Assert.Equal("NoTrace", response.Root.Type);
        Assert.Empty(response.Root.Children);
    }

    [Fact]
    public void ATimedOutTraceIsSuccessfulButIncomplete_WithTheStopReasonCarried()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.TimedOut(
                TimeSpan.FromMinutes(2), "pangloss parse did not finish within 2 minutes and was stopped."),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.False(outcome.Value!.Complete);
        Assert.Contains("did not finish", outcome.Value.StopReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AParserThatIsAbsentIsATypedRefusal_NotAnException()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Unavailable("Could not find the pangloss executable."),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("wordtrace.parser-unavailable", outcome.Refusal!.Code);
    }

    private void Capture(string fwDataPath)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
