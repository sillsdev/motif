using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// Unit tests for the mapping from PanGloss's vocabulary into Motif's, over <b>captured real output</b>
/// rather than invented fixtures.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures under <c>Fixtures/</c> are verbatim output from real runs: the batch rows come from parsing
/// a real 56 MB project and from an Amharic run that genuinely timed out, and the refusal text is
/// the exact message a real project produced when its grammar overflowed the FST enumeration budget. Invented
/// fixtures would test this code against my assumptions about the parser rather than against the parser.
/// </para>
/// <para>
/// What these tests defend is the distinction the whole grammar coverage story rests on: <b>timed out is not
/// failed</b>. The moment a figure counts "we stopped waiting" as "the grammar cannot analyse this", coverage
/// stops being usable as a target, because it then moves when the machine is busy.
/// </para>
/// </remarks>
public class ParserOutputTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Parser", "Fixtures", name));

    [Theory]
    [InlineData("-")]
    [InlineData("approved-analysis-signature")]
    public void CappedWordRetainsPartialEvidenceAndDoesNotHideLaterCompletedWords(string signature)
    {
        var words = BatchTsvParser.Parse(
            $"0\tlimited\t3\tCAP\t{signature}\n1\tfinished\t4\tok\tfinished-sig\n");

        Assert.Equal(2, words.Count);
        Assert.Equal("capped", words[0].Outcome.ToStoredOutcome());
        Assert.Equal(signature, words[0].Signature);
        Assert.Equal(WordOutcome.Analysed, words[1].Outcome);
        Assert.True(StoredWordOutcomeNames.TryParseStoredOutcome("capped", out var restored));
        Assert.Equal(words[0].Outcome, restored);
    }

    [Fact]
    public void BatchOutput_SeparatesAnalysedFromTimedOutFromSkipped()
    {
        var words = BatchTsvParser.Parse(Fixture("batch-mixed-outcomes.tsv"));

        Assert.Equal(10, words.Count);
        Assert.Equal(5, words.Count(w => w.Outcome == WordOutcome.Analysed));
        Assert.Equal(1, words.Count(w => w.Outcome == WordOutcome.Skipped));
        Assert.Equal(2, words.Count(w => w.Outcome == WordOutcome.TimedOut));

        Assert.Equal(new[] { "pibubu", "piratu" },
            words.Where(w => w.Outcome == WordOutcome.NoAnalysis).Select(w => w.Word));
    }

    [Fact]
    public void BatchOutput_PreservesWordsIndicesAndTimings()
    {
        var words = BatchTsvParser.Parse(Fixture("batch-mixed-outcomes.tsv"));

        var mbali = words.Single(w => w.Word == "mbali");
        Assert.Equal(2, mbali.Index);
        Assert.Equal(37, mbali.ElapsedMs);
        Assert.Equal(WordOutcome.Analysed, mbali.Outcome);
        Assert.Contains("bal", mbali.Signature);

        // A skipped word still carries its row: dropping it would silently shrink the denominator.
        var skipped = words.Single(w => w.Outcome == WordOutcome.Skipped);
        Assert.Equal("n'nyumba", skipped.Word);
    }

    [Fact]
    public void ABatchWithAnyTimeout_IsIncomplete()
    {
        var analysis = new BatchAnalysis(
            BatchTsvParser.Parse(Fixture("batch-mixed-outcomes.tsv")),
            PerWordTimeoutMs: 5000,
            ProjectPath: "irrelevant.fwdata",
            Warnings: Array.Empty<string>());

        Assert.True(analysis.IsIncomplete);
        Assert.Equal(2, analysis.TimedOut);

        // Timeouts and skipped words must not dilute the fraction of adjudicated words with analyses.
        Assert.Equal(7, analysis.Adjudicated);
        Assert.Equal(5, analysis.Analysed);
    }

    [Fact]
    public void ABatchWithNoTimeouts_HasNoIncompleteSearches()
    {
        var clean = BatchTsvParser.Parse(Fixture("batch-mixed-outcomes.tsv"))
            .Where(w => w.Outcome != WordOutcome.TimedOut)
            .ToList();

        var analysis = new BatchAnalysis(
            clean, 5000, "irrelevant.fwdata", Array.Empty<string>());

        Assert.False(analysis.IsIncomplete);
    }

    [Fact]
    public void AnUnrecognisedStatus_ThrowsRatherThanBecomingAFailure()
    {
        // Prevents a future parser status being silently bucketed as "no analysis", moving coverage down invisibly.
        var ex = Assert.Throws<InvalidOperationException>(
            () => BatchTsvParser.Parse("0\tword\t5\tSOMETHING_NEW\t-\n"));

        Assert.Contains("SOMETHING_NEW", ex.Message);
        // The message must name where to add the status, or the refusal is a dead end rather than a hand-off.
        Assert.Contains(nameof(BatchTsvParser), ex.Message);
    }

    [Fact]
    public void TheRealBudgetRefusal_IsRecognisedAsAGrammarFactSoTheFallbackCanFire()
    {
        var refusal = ParserRefusalRecognizer.Recognize(Fixture("refusal-budget.txt"));

        Assert.NotNull(refusal);
        Assert.Equal(ParserRefusalKind.FstEnumerationBudgetExceeded, refusal!.Kind);

        // The parser's own numbers survive into the diagnostic: a reviewer needs to see how far over the limit it is.
        Assert.Contains("200500", refusal.Detail);
        Assert.Contains("200000", refusal.Detail);
    }

    [Fact]
    public void AMissingExecutableOrBadPath_IsNotMistakenForARefusedGrammar()
    {
        // Must stay distinguishable: "use the other engine" vs "your build is wrong" — conflating mislabels the cause.
        Assert.Null(ParserRefusalRecognizer.Recognize("No such file or directory"));
        Assert.Null(ParserRefusalRecognizer.Recognize(""));
        Assert.Null(ParserRefusalRecognizer.Recognize("thread 'main' panicked at src/main.rs:1:1"));
    }

}
