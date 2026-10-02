using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class TraceEvidenceBoundaryTests
{
    [Fact]
    public void SeveralSiblingRejectionsRemainContextRatherThanTerminalCauses()
    {
        var reading = Read("""
            {"type":"WordAnalysis","children":[
              {"type":"MorphologicalRuleSynthesis","source":"first","failureReason":"Pattern","children":[]},
              {"type":"MorphologicalRuleSynthesis","source":"second","failureReason":"RequiredMprFeatures","children":[]},
              {"type":"Failed","failureReason":"PartialParse","failureContext":{
                "status":"available","reasonCode":"PartialParse","actual":"terminal actual"},"children":[]}]}
            """);
        var attempt = Assert.Single(reading.Attempts);
        Assert.Equal("PartialParse", attempt.FailureReason);
        Assert.Equal("terminal actual", attempt.FailureActual);
        Assert.Null(attempt.StoppedByRule);
        Assert.Null(Assert.Single(reading.StopGroups).RuleRefId);
        Assert.Equal(["WordAnalysis", "Failed"], attempt.Steps.Select(step => step.Type));
        Assert.Equal(["Pattern", "RequiredMprFeatures"], TraceTreeContextRange.Resolve(reading.Root, attempt.TreeContext).Select(step => step.FailureReason));
        Assert.DoesNotContain("template", attempt.Explanation!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZodutFamilyReplacementIsAnEventAndSearchContinuesToVem()
    {
        var json = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,
            "TestFixtures", "trace-details-v2-zodut-synthetic.json"));
        var document = PanGlossTraceDiagnosticReader.Read(json);
        var response = WordTraceQuery.LoadDiagnostic(json).Value!;
        Assert.Single(document.Attempts);
        var attempt = Assert.Single(response.Reading.Attempts);
        Assert.True(attempt.Succeeded);
        Assert.Equal("vem", attempt.Surface);
        var blocked = Assert.Single(TraceTreeContextRange.Resolve(response.Reading.Root, attempt.TreeContext));
        Assert.Equal("Blocked", blocked.Type);
        Assert.Equal("past2", blocked.Source);
        Assert.Equal("vem", blocked.Output);
        Assert.Null(blocked.FailureReason);
        Assert.Equal(TraceEvidenceAvailability.NotRecorded, blocked.ReasonAvailability);
        Assert.Empty(response.Reading.StopGroups);
        Assert.Equal(json, response.DiagnosticJson);
    }

    [Fact]
    public void RuleStatusesDoNotCreateTerminalAttempts()
    {
        var reading = Read("""
            {"type":"WordAnalysis","children":[
              {"type":"MorphologicalRuleSynthesis","outcome":{"status":"successful"},"children":[]},
              {"type":"PhonologicalRuleSynthesis","outcome":{"status":"failed"},"children":[]},
              {"type":"Blocked","outcome":{"status":"blocked"},"children":[]}]}
            """);
        Assert.Empty(reading.Attempts);
        Assert.Equal(3, reading.Root.Children.Count);
    }

    [Fact]
    public void InterleavedSourceAnalysesRetainIndexesAndDifferentProjectionEvidence()
    {
        var reading = WordTraceQuery.LoadDiagnostic(TraceEnvelope.AnalysisRecords()).Value!.Reading;
        Assert.Equal(["a0", "b1", "a2"], reading.Analyses.Select(analysis => analysis.AnalysisId));
        Assert.Equal([8, 4, 9], reading.Analyses.Select(analysis => analysis.Index));
        Assert.Equal(["A|word", "B|word", "A|word"], reading.Analyses.Select(analysis => analysis.Signature));
        Assert.Equal([0, 2], reading.LogicalAnalyses[0].SourcePositions);
        Assert.Equal([1], reading.LogicalAnalyses[1].SourcePositions);
        var roundTrip = ProjectionJson.Deserialize<WordTraceReading>(ProjectionJson.Serialize(reading))!;
        Assert.Equal(ProjectionJson.Serialize(reading), ProjectionJson.Serialize(roundTrip));
    }

    [Fact]
    public void MissingAndUnknownReasonsHaveTypedAvailabilityWithoutInventedExplanations()
    {
        var missing = WordTraceQuery.LoadDiagnostic(TraceEnvelope.Of("", "{\"type\":\"Failed\",\"children\":[]}")).Value!;
        Assert.Equal(TraceEvidenceAvailability.NotRecorded, missing.Reading.Root.ReasonAvailability);
        Assert.Equal(TraceEvidenceAvailability.NotRecorded, missing.Reading.Root.RejectionDetailsAvailability);
        Assert.Equal(TraceEvidenceAvailability.NotRecorded, missing.GrammarSourceAvailability);
        var unknown = Read("""
            {"type":"Failed","failureReason":"FutureReason","failureContext":{
              "status":"unavailable","unavailableReason":"not-captured","reasonCode":"FutureReason"},"children":[]}
            """);
        Assert.Equal("FutureReason", unknown.Root.FailureReason);
        Assert.Equal(TraceEvidenceAvailability.Recorded, unknown.Root.ReasonAvailability);
        Assert.Equal(TraceEvidenceAvailability.NotRecorded, unknown.Root.ExplanationAvailability);
        Assert.Equal(TraceEvidenceAvailability.NotRecorded, unknown.Root.RejectionDetailsAvailability);
        Assert.Equal("not-captured", unknown.Root.FailureEvidence!.UnavailableReason);
        Assert.Null(Assert.Single(unknown.Attempts).Explanation);
    }

    [Fact]
    public void InterruptedSearchKeepsProgressWithoutFabricatingATerminalAttempt()
    {
        var response = WordTraceQuery.LoadDiagnostic(TraceEnvelope.Of("", TraceEnvelope.InterruptedTree, capped: true)).Value!;
        Assert.False(response.Complete);
        Assert.Empty(response.Reading.Attempts);
        Assert.Equal("word", Assert.Single(response.Reading.Root.Children).Output);
        Assert.Empty(response.Reading.RulesOnBestPath);
        Assert.Empty(response.Reading.Analyses);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public void EqualRenderingsRequireExactMorphologyEvidence(bool available, int summaries)
    {
        var reading = WordTraceQuery.LoadDiagnostic(TraceEnvelope.AnalysisRecords(equalRendering: true, available)).Value!.Reading;
        Assert.Equal(summaries, reading.LogicalAnalyses.Count);
        Assert.Equal(3, reading.Analyses.Count);
        Assert.Equal("ROOT|word", reading.Analyses[1].Signature);
        Assert.Equal([1], reading.LogicalAnalyses[1].SourcePositions);
        if (!available) Assert.All(reading.LogicalAnalyses, summary => Assert.Single(summary.SourcePositions));
    }

    [Fact]
    public void InterleavedUnavailableProjectionKeepsItsOwnEvidenceAndDoesNotCompact()
    {
        var input = JsonNode.Parse(TraceEnvelope.AnalysisRecords())!;
        input["result"]!["analyses"]![2]!["projection"] = new JsonObject
        {
            ["status"] = "unavailable", ["error"] = "third projection failed", ["errorCode"] = "ThirdError",
        };
        var reading = WordTraceQuery.LoadDiagnostic(input.ToJsonString()).Value!.Reading;
        Assert.Equal(3, reading.LogicalAnalyses.Count);
        Assert.Equal("a2", reading.Analyses[2].AnalysisId);
        Assert.Equal(9, reading.Analyses[2].Index);
        Assert.Equal("ThirdError", reading.Analyses[2].ProjectionErrorCode);
        Assert.Equal("third projection failed", reading.Analyses[2].ProjectionError);
        Assert.Equal("available", reading.Analyses[0].ProjectionStatus);
    }

    [Theory]
    [InlineData("formId")]
    [InlineData("msaId")]
    [InlineData("inflTypeId")]
    public void EveryAuthoredMorphologyDiscriminatorParticipatesInEquality(string field)
    {
        var input = JsonNode.Parse(TraceEnvelope.AnalysisRecords())!;
        input["result"]!["analyses"]![2]!["morphs"]![0]!["identity"]![field] = "00000000-0000-0000-0000-000000000099";
        var reading = WordTraceQuery.LoadDiagnostic(input.ToJsonString()).Value!.Reading;
        Assert.Equal(3, reading.LogicalAnalyses.Count);
        Assert.All(reading.LogicalAnalyses, summary => Assert.Single(summary.SourcePositions));
    }

    [Fact]
    public void ThousandSiblingBranchesRetainEveryAddressWithLinearContextRepresentation()
    {
        static WordTraceReading Broad(int count)
        {
            var branches = string.Join(",", Enumerable.Range(0, count).Select(index =>
                $"{{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"rule-{index}\",\"children\":[{{\"type\":\"Failed\",\"failureReason\":\"PartialParse\",\"children\":[]}}]}}"));
            return Read("{\"type\":\"WordAnalysis\",\"children\":[" + branches + "]}");
        }
        var small = Broad(500);
        var large = Broad(1000);
        Assert.True(ProjectionJson.Serialize(large).Length < 2.1 * ProjectionJson.Serialize(small).Length);
        Assert.Equal(1000, large.Root.Children.Count);
        Assert.Equal(999, large.Attempts.Sum(attempt => attempt.TreeContext.Count));
        for (var index = 0; index < 1000; index++)
        {
            var branch = large.Root.Children[index];
            Assert.Equal($"0.{index}", branch.StepId);
            Assert.Equal($"0.{index}.0", Assert.Single(branch.Children).StepId);
            if (index == 0) Assert.Empty(large.Attempts[index].TreeContext);
            else Assert.Equal(new TraceTreeContextRange("0", index), Assert.Single(large.Attempts[index].TreeContext));
        }
        Assert.Equal(large.Root.Children.Take(999), TraceTreeContextRange.Resolve(large.Root, large.Attempts[999].TreeContext));
    }

    private static WordTraceReading Read(string tree) =>
        WordTraceQuery.LoadDiagnostic(TraceEnvelope.Of("", tree)).Value!.Reading;
}
