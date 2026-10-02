using System;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class TraceContractTests
{
    [Theory]
    [InlineData("attempted", "tried")]
    [InlineData("successful", "applied")]
    [InlineData("failed", "stopped")]
    [InlineData("blocked", "Blocked")]
    public void RuleOutcomesComeFromTheEventRatherThanItsSuccessfulDescendant(string status, string expected)
    {
        var reading = Read(Rule("Plural", "morphRule", "1", Successful, status));
        Assert.Equal(expected, Assert.Single(reading.RulesOnBestPath).Outcome);
    }

    [Fact]
    public void RepeatedRuleApplicationsRemainInBuildingOrder()
    {
        var reading = Read(Rule("A", "morphRule", "1", Rule("B", "morphRule", "2",
            Rule("A", "morphRule", "1", Successful))));
        Assert.Equal(["A", "B", "A"], reading.RulesOnBestPath.Select(rule => rule.Rule));
        Assert.Equal(["0.0", "0.0.0", "0.0.0.0"], reading.RulesOnBestPath.SelectMany(rule => rule.StepIds));
    }

    [Fact]
    public void SameLabelWithoutIdentityNamesSeparateOccurrences()
    {
        var reading = Read(Rule("Plural", null, null, Rule("Plural", null, null, Successful)));
        Assert.Equal(2, reading.Refs.Count);
        Assert.All(reading.Refs, reference => Assert.Equal("unknown", reference.IdentityQuality));
        Assert.All(reading.Refs, reference => Assert.Null(reference.Identity));
        Assert.Equal(2, reading.RulesOnBestPath.Count);
        Assert.Equal(2, reading.RulesOnBestPath.Select(rule => rule.RefId).Distinct().Count());
    }

    [Fact]
    public void EqualLocalKeysOfDifferentKindsRemainSeparateRulesAndStopCauses()
    {
        var reading = Read(Rule("Rule", "morphRule", "0", Rule("Rule", "phonRule", "0", Successful)));
        Assert.Equal(2, reading.RulesOnBestPath.Count);
        var failed = Read(Failure("morphRule") + "," + Failure("phonRule"));
        Assert.Equal(2, failed.StopGroups.Count);
        Assert.All(failed.StopGroups, group => Assert.Null(group.RuleRefId));
    }

    [Fact]
    public void SameLabelWithoutIdentityKeepsStopCausesSeparate()
    {
        var reading = Read(Failure(null) + "," + Failure(null));
        Assert.Equal(2, reading.StopGroups.Count);
        Assert.All(reading.StopGroups, group => Assert.Null(group.RuleRefId));
        Assert.All(reading.StopGroups, group => Assert.Single(group.Attempts));
    }

    [Fact]
    public void ReadingRoundTripRetainsEnrichedRefsWithoutClrAliasing()
    {
        var response = WordTraceQuery.LoadDiagnostic(TraceEnvelope.Of("", "{\"type\":\"WordAnalysis\",\"children\":[" +
            Rule("Plural", "morphRule", "1", Successful) + "]}")).Value!;
        var reading = response.Reading! with
        {
            Refs = response.Reading!.Refs.Select(reference => reference with
            {
                FieldWorks = new TraceFieldWorksTarget("tool", "Tool", "object", "silfw://recorded"),
            }).ToArray(),
        };
        var roundTrip = ProjectionJson.Deserialize<WordTraceResponse>(ProjectionJson.Serialize(response with { Reading = reading }))!;
        Assert.Equal(ProjectionJson.Serialize(reading), ProjectionJson.Serialize(TraceReadingBuilder.Build(roundTrip)));
    }

    [Fact]
    public void ResponsePublishesOneAuthoritativeReading()
    {
        var names = typeof(WordTraceResponse).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain("Root", names);
        Assert.DoesNotContain("Candidates", names);
        Assert.DoesNotContain("Analyses", names);
        Assert.Contains("Reading", names);
    }

    [Fact]
    public void ProjectionCodeAndFailureOwnerEvidenceSurviveTypedProjection()
    {
        const string json = """
            {"schemaVersion":"pangloss.trace-details.v3","word":"word",
             "search":{"completed":true,"capped":false,"timedOut":false,"invalidShape":false,"steps":1,"elapsedNs":2},
             "result":{"signature":"x","guessed":false,"analyses":[{"analysisId":"a","projection":{
                "status":"unavailable","error":"Projection failed","errorCode":"RecordedCode"},"morphs":[]}]},
             "categories":{},"trace":{"type":"Failed","children":[],"failureReason":"FutureReason",
                "failureContext":{"kind":"decisionGate","source":"owner","reasonCode":"FutureReason",
                "status":"unavailable","unavailableReason":"not-captured","required":{"features":[1,2]},
                "actual":"value","environment":"context"}}}
            """;
        var response = WordTraceQuery.LoadDiagnostic(json).Value!;
        using var typed = JsonDocument.Parse(ProjectionJson.Serialize(response.Reading!));
        Assert.Equal("RecordedCode", typed.RootElement.GetProperty("analyses")[0].GetProperty("projectionErrorCode").GetString());
        var evidence = typed.RootElement.GetProperty("root").GetProperty("failureEvidence");
        Assert.Equal("decisionGate", evidence.GetProperty("kind").GetString());
        Assert.Equal("owner", evidence.GetProperty("source").GetString());
        Assert.Equal("FutureReason", evidence.GetProperty("reasonCode").GetString());
        Assert.Equal("unavailable", evidence.GetProperty("status").GetString());
        Assert.Equal("not-captured", evidence.GetProperty("unavailableReason").GetString());
        Assert.Equal("{\"features\":[1,2]}", evidence.GetProperty("required").GetString());
        Assert.Equal(ProjectionJson.Deserialize<TraceFailureEvidence>(evidence.GetRawText()),
            ProjectionJson.Deserialize<TraceFailureEvidence>(typed.RootElement.GetProperty("attempts")[0].GetProperty("failureEvidence").GetRawText()));
        Assert.Equal(json, response.DiagnosticJson);
    }

    [Fact]
    public void AnUnknownFailureCodeHasNoInventedExplanation()
    {
        var reading = WordTraceQuery.LoadDiagnostic(TraceEnvelope.Of("", """
            {"type":"Failed","failureReason":"FutureUnificationMechanism","children":[]}
            """)).Value!.Reading;
        Assert.Null(reading.Root.ReasonExplanation);
        Assert.Null(Assert.Single(reading.Attempts).Explanation);
    }

    private const string Successful = "{\"type\":\"Successful\",\"outcome\":{\"status\":\"successful\"},\"children\":[]}";

    private static string Rule(string name, string? kind, string? id, string children, string status = "attempted") =>
        "{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"" + name + "\",\"outcome\":{\"status\":\"" + status + "\"}" +
        (kind is null ? "" : ",\"sourceIdentity\":{\"kind\":\"" + kind + "\",\"id\":\"" + id + "\",\"quality\":\"grammar-local\"}") +
        ",\"children\":[" + children + "]}";

    private static string Failure(string? kind) =>
        "{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"Rule\",\"failureReason\":\"Pattern\"," +
        (kind is null ? "" : "\"sourceIdentity\":{\"kind\":\"" + kind + "\",\"id\":\"0\",\"quality\":\"grammar-local\"},") +
        "\"children\":[]}," +
        "{\"type\":\"Failed\",\"failureReason\":\"Pattern\",\"children\":[]}";

    private static WordTraceReading Read(string children) => WordTraceQuery.LoadDiagnostic(TraceEnvelope.Of("",
        "{\"type\":\"WordAnalysis\",\"children\":[" + children + "]}")).Value!.Reading!;
}
