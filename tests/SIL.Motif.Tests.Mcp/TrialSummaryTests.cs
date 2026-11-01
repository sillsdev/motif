using SIL.Motif.Host.Parser;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class TrialSummaryTests
{
    [Fact]
    public void ANewDisapprovedReadingIsReportedEvenWhenAnotherWasAlreadyProduced()
    {
        var before = Word("ambiguous", true) with
        { AnalysisComparison = new([new(0, [new("old-negative", "disapproved")], null)], [], []) };
        var after = Word("ambiguous", true) with
        { AnalysisComparison = new([new(0, [new("old-negative", "disapproved"), new("new-negative", "disapproved")], null)], [], []) };

        var row = Assert.Single(TrialResults.Classify([before], [after], []));

        Assert.StartsWith("negatives-now-parsing:", row.Outcome);
    }

    [Fact]
    public void LosingAnApprovedReadingIsReportedWhenAnotherApprovedReadingReplacesIt()
    {
        var before = Word("ambiguous", true) with
        { AnalysisComparison = new([], [new("already-missing", "approved")], []) };
        var after = Word("ambiguous", true) with
        { AnalysisComparison = new([], [new("newly-missing", "approved")], []) };

        var row = Assert.Single(TrialResults.Classify([before], [after], []));

        Assert.StartsWith("positives-lost:", row.Outcome);
    }

    [Fact]
    public void AnUnfinishedSearchIsNeverALostPositive()
    {
        var before = Word("positive", true);
        var after = new AssessedWord("positive", "timeout", []) { IsIncomplete = true };
        var row = Assert.Single(TrialResults.Classify([before], [after], []));
        Assert.StartsWith("unfinished:", row.Outcome);
    }

    [Fact]
    public void CategoriesSeparateNegativesLossesIncompleteAndOtherChanges()
    {
        var before = new[] { Word("negative", false), Word("positive", true), Word("unfinished", true), Word("changed", false) };
        var after = new[] { Word("negative", true), Word("positive", false),
            new AssessedWord("unfinished", "timeout", []) { IsIncomplete = true }, Word("changed", true) };
        var rows = TrialResults.Classify(before, after, ["negative"]).ToDictionary(row => row.Word);
        Assert.StartsWith("negatives-now-parsing:", rows["negative"].Outcome);
        Assert.StartsWith("positives-lost:", rows["positive"].Outcome);
        Assert.StartsWith("unfinished:", rows["unfinished"].Outcome);
        Assert.StartsWith("other-changes:", rows["changed"].Outcome);
        Assert.Equal(new[] { "negatives-now-parsing", "positives-lost", "unfinished", "other-changes" }, TrialResults.Categories);
    }

    [Fact]
    public void AComposedNegativeWordThatNewlyParsesIsListedAsANegativeNowParsing()
    {
        var composed = "caf\u00E9";
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        var rows = TrialResults.Classify([Word(decomposed, false)], [Word(decomposed, true)], [composed]);
        var row = Assert.Single(rows);
        Assert.StartsWith("negatives-now-parsing:", row.Outcome);
    }

    [Fact]
    public void AComposedRowInTheOldBaselineStillJoinsItsDecomposedRowAfterTheTrial()
    {
        var composed = "caf\u00E9";
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);

        var row = Assert.Single(TrialResults.Classify([Word(composed, false)], [Word(decomposed, true)], [composed]));

        Assert.Equal(decomposed, row.Word);
        Assert.StartsWith("negatives-now-parsing:", row.Outcome);
    }

    [Fact]
    public void ACheckedCleanTrialReportsCleanDroppedGrammar()
    {
        var node = TrialResults.DroppedGrammarNode(new DroppedGrammar([], 2)).AsObject();

        Assert.Equal("checked", (string?)node["state"]);
        Assert.True((bool)node["clean"]!);
        Assert.Equal(2, (int)node["preExisting"]!);
        Assert.Empty(node["introduced"]!.AsArray());
    }

    [Fact]
    public void IntroducedDroppedGrammarIsReportedWithItsObjectsAndIsNotClean()
    {
        var finding = new DroppedGrammarFinding("grammar.environment.invalid",
            [new DroppedGrammarObject("id/1", "PhEnvironment", "env", null)], "dropped");

        var node = TrialResults.DroppedGrammarNode(new DroppedGrammar([finding], 0) { OmittedFindings = 4 }).AsObject();

        Assert.False((bool)node["clean"]!);
        Assert.Equal(4, (int)node["omitted"]!);
        var reported = Assert.Single(node["introduced"]!.AsArray());
        Assert.Equal("grammar.environment.invalid", (string?)reported!["code"]);
        Assert.Equal("id/1", (string?)reported["objects"]![0]!["motifId"]);
    }

    [Fact]
    public void AnUncheckedTrialIsNeverReportedAsClean()
    {
        var node = TrialResults.DroppedGrammarNode(DroppedGrammar.NotChecked("no parser")).AsObject();

        Assert.Equal("not-checked", (string?)node["state"]);
        Assert.False((bool)node["clean"]!);
        Assert.Equal("no parser", (string?)node["reason"]);
    }

    private static AssessedWord Word(string word, bool parsed) => new(word, parsed ? "analysed" : "no-analysis",
        parsed ? [new ParsedAnalysis(null, ["morph"], 0, "identity")] : []);
}
