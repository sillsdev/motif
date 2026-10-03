using System.Text.Json;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ParserRefusalRegressionTests
{
    [Fact]
    public void HandoffKeepsRefusalReasonAndTheRecordedAnalysisCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "refused-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var refusal = new ParserRefusal("test-refusal", "Recorded parser reason");
            var refused = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "chats", 0,
                false, false, true, [], []) { Refusal = refusal };
            var parsed = refused with { Word = "froid", InvalidShape = false, Refusal = null,
                Analyses = [new ParseAnalysis([new ParseMorph("form", "noun", null, null)]),
                    new ParseAnalysis([new ParseMorph("form", "adjective", null, null)])] };
            HandoffWriter.WriteAssessmentJson(root,
                [new("chats", "skipped", 0, "-", refused), new("froid", "analysed", 0, "|froid;|froid", parsed)]);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "parse-results.json")));
            Assert.Equal("refused", document.RootElement[0].GetProperty("outcome").GetString());
            Assert.Equal(refusal.Reason, document.RootElement[0].GetProperty("refusal").GetProperty("reason").GetString());
            Assert.Equal(2, document.RootElement[1].GetProperty("analysisCount").GetInt32());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void HandoffUsesReadableUnicodeAndDoesNotPromiseTexts()
    {
        var root = Path.Combine(Path.GetTempPath(), "refused-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            HandoffWriter.WriteAssessmentJson(root,
                [new HandoffWriter.AssessedWordStatistics("fene\u0302tre", "skipped", 0, "-")]);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "parse-results.json")));
            Assert.Equal("fenêtre", document.RootElement[0].GetProperty("word").GetString());
            Assert.Contains("empty", HandoffWriter.BuildPastedHeader("French", "No Texts"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InvalidShapeIsRefusedWithItsReasonRatherThanNotParsed()
    {
        var word = new AssessmentWordResult("chats", "skipped", false, "Skipped", 0, "-")
        {
            ProjectStanding = ProjectStanding.Approved,
            Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "chats", 0,
                false, false, true, [], []),
        };

        var row = WordRowProjection.Of(word);

        Assert.Equal(WordRowOutcome.NoParse, row.Outcome);
        Assert.Equal(ParserRefusals.Title, row.Meaning);
        Assert.Contains("phonemes", row.MeaningDetail);
        Assert.Equal("refused", row.MeaningCode);
        Assert.Null(CompareSemantics.FixFirst(CompareWordFacts.Of(word), []));
        Assert.Contains(row.MeaningDetail, AssessmentWordRows.CompletionSummary([word]));
    }

    [Fact]
    public void ZeroRootLookupExplainsNoParseWithoutATerminalAttempt()
    {
        var json = TraceEnvelope.Of("-", """
            {"type":"WordAnalysis","inputShape":"lions","children":[
              {"type":"CompoundingRuleAnalysis","source":"Default Right Head Compounding",
               "outputShape":"s","children":[
                {"type":"LexicalLookup","source":"Morphology","inputShape":"s",
                 "lookupResult":{"completed":true,"matchCount":0,"mode":"lexicon","status":"zeroMatches"},
                 "children":[]}]}]}
            """).Replace("\"word\":\"sagd\"", "\"word\":\"lions\"");

        var response = WordTraceDiagnosticReader.Read(json).Value!;

        Assert.Empty(response.Reading.Attempts);
        Assert.NotEmpty(response.Reading.NoParseReasons);
        Assert.Contains(response.Reading.NoParseReasons, reason => reason.Contains("No lexical root matched 's'"));
        Assert.Contains("No affix-building step was recorded.", response.Reading.NoParseReasons);
    }
}
