using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ExpertTraceReadingTests
{
    internal static string EnvironmentOperandDiagnostic(string operandJson) => $$"""
        {"schemaVersion":"pangloss.trace-details.v3","word":"ab",
         "search":{"completed":true,"capped":false,"timedOut":false,"invalidShape":false,"steps":1,"elapsedNs":9},
         "result":{"signature":"-","guessed":false,"analyses":[]},"categories":{},
         "trace":{"type":"Failed","children":[],"failureReason":"EnvironmentMismatch",
          "failureContext":{"status":"recorded","environment":{{operandJson}}
          }
         }
        }
        """;

    [Theory]
    [InlineData("{\"left\":\"word_edge\",\"right\":\"#\"}", "{\"left\":\"word_edge\",\"right\":\"#\"}")]
    [InlineData("\"[raw environment]\"", "[raw environment]")]
    [InlineData("\"/[C]_# (XV?)[ABC]BBWSF\"", "/[C]_# (XV?)[ABC]BBWSF")]
    [InlineData("{\"text\":\"/[C]_#\",\"compiled\":\"#_\"}", "{\"text\":\"/[C]_#\",\"compiled\":\"#_\"}")]
    [InlineData("[\"#\",\"_\"]", "[\"#\",\"_\"]")]
    public void UnqualifiedEnvironmentOperandsRemainOpaque(string operandJson, string expectedRaw)
    {
        var trace = TraceWordViewModel.FromDiagnosticJson(EnvironmentOperandDiagnostic(operandJson));
        trace.SelectedStep = trace.Root!;
        var token = Assert.Single(trace.ExpertEnvironmentTokens);
        Assert.Equal(expectedRaw, token.Raw);
        Assert.True(token.IsProjectDefined);
        Assert.Equal("Authored environment notation unavailable", token.Explanation);
        Assert.Contains("environment", trace.ExpertRawRecord);
    }

    internal static string NotationDiagnostic => TraceEnvelope.Of("", """
        {"type":"WordAnalysis","inputShape":"ab","children":[
          {"type":"TemplateAnalysisInput","source":"Template","inputShape":"ab","children":[
            {"type":"MorphologicalRuleSynthesis","source":"Suffix","inputShape":"a","outputShape":"ab",
             "outcome":{"status":"attempted"},"children":[
              {"type":"PhonologicalRuleSynthesis","source":"Vowel harmony","inputShape":"ab","outputShape":"ab",
               "sourceIdentity":{"kind":"phonRule","id":"rule-id","quality":"authored"},
               "outcome":{"status":"failed"},"failureReason":"EnvironmentMismatch",
               "failureContext":{"status":"available","environment":"/[C]_# (XV?)[ABC]BBWSF"},
               "children":[{"type":"Failed","failureReason":"PartialParse","children":[]}]}]}]}]}
        """);

    [Fact]
    public void WholeTreePhonologicalRowsAreOccurrencesAndNeverImplyAttemptMembership()
    {
        var trace = TraceWordViewModel.FromDiagnosticJson(TraceEnvelope.Of("", """
            {"type":"WordAnalysis","children":[
              {"type":"PhonologicalRuleSynthesis","source":"Repeat","inputShape":"a","outputShape":"b",
               "outcome":{"status":"attempted"},"children":[]},
              {"type":"PhonologicalRuleSynthesis","source":"Repeat","inputShape":"b","outputShape":"a",
               "outcome":{"status":"success"},"children":[]},
              {"type":"Failed","failureReason":"UnknownCode","children":[]}]}
            """));
        trace.SelectedCandidate = Assert.Single(trace.Candidates);
        trace.ExpertWholeTree = false;
        Assert.DoesNotContain(trace.ExpertEvents, row => row.Type == "PhonologicalRuleSynthesis");
        Assert.Empty(trace.ExpertPhonologicalEvents);
        trace.ExpertWholeTree = true;
        Assert.Equal(["0.0", "0.1"], trace.ExpertPhonologicalEvents.Select(row => row.RecordedStep.StepId));
        Assert.Equal(["Tried", "Applied"], trace.ExpertPhonologicalEvents.Select(row => row.RecordedOutcomeText));
        Assert.Contains("membership", trace.ExpertScopeText);
        trace.SelectedStep = trace.ExpertPhonologicalEvents[0];
        Assert.Contains("Tried", trace.ExpertReadableText);
        Assert.DoesNotContain("Applied", trace.ExpertReadableText);
    }

    [Fact]
    public void AReplacementResultKeepsItsOwnRawRecord()
    {
        var trace = TraceWordViewModel.FromDiagnosticJson(TraceEnvelope.Of("", """
            {"type":"WordAnalysis","children":[{"type":"Failed","future":"old","children":[]}]}
            """));
        trace.Result = TraceWordViewModel.FromDiagnosticJson(TraceEnvelope.Of("", """
            {"type":"WordAnalysis","children":[{"type":"Failed","future":"new","children":[]}]}
            """)).Result;
        trace.SelectedStep = trace.Root!.Children[0];
        Assert.Contains("new", trace.ExpertRawRecord);
        Assert.DoesNotContain("old", trace.ExpertRawRecord);
    }

    [Fact]
    public void SelectedRawRecordPreservesUnknownProducerFieldsAndExcludesChildren()
    {
        var trace = TraceWordViewModel.FromDiagnosticJson(TraceEnvelope.Of("", """
            {"type":"WordAnalysis","children":[
              {"type":"Failed","future":{"operand":"unchanged"},"children":[]}]}
            """));
        trace.SelectedStep = trace.Root!.Children[0];
        Assert.Contains("unchanged", trace.ExpertRawRecord);
        Assert.Contains("future", trace.ExpertRawRecord);
        Assert.DoesNotContain("children", trace.ExpertRawRecord);
        Assert.Contains("not recorded", trace.ExpertReadableText);
    }
}
