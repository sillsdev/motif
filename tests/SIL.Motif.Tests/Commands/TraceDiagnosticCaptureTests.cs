using System;
using System.Text.Json;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class TraceDiagnosticCaptureTests
{
    [Fact]
    public void ExplicitIncompleteSearchIsNotReinterpretedAsComplete()
    {
        const string json = """
        {"schemaVersion":"pangloss.trace-details.v2","word":"word",
         "search":{"completed":false,"capped":false,"timedOut":false,"invalidShape":false,"steps":1,"elapsedNs":2},
         "result":{"signature":"-","guessed":false,"analyses":[]},"categories":{},"trace":null}
        """;
        var outcome = WordTraceQuery.LoadDiagnostic(json);
        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Value!.Complete);
        Assert.Equal("incomplete", outcome.Value.SearchStatus);
    }
    private static TraceHostCapture Capture(string identity = "project-a", string hash = "abc", string semantics = "snapshot-semantic-sha256-v1") =>
        new(identity, hash, semantics, "bundle", DateTimeOffset.Parse("2026-09-22T12:00:00Z"), 1234,
            [new TraceWritingSystem("ar", "Arabic", true, true, "rtl", "Noto Sans Arabic")]);

    [Fact]
    public void DifferentHashSemanticsNeverEstablishGrammarCompatibility()
    {
        var comparison = TraceDiagnosticCapture.Compare(Capture(), Capture(semantics: "source-bytes-sha256-v1"));
        Assert.Equal("unknown", comparison.GrammarStatus);
        Assert.False(comparison.IsCompatible);
        Assert.Equal("match", comparison.ProjectIdentityStatus);
    }

    [Fact]
    public void MissingCurrentProjectDoesNotAuthorizeLinks()
    {
        var comparison = TraceDiagnosticCapture.Compare(Capture(), null);
        Assert.False(comparison.IsCompatible);
        Assert.Equal("unknown", comparison.ProjectIdentityStatus);
    }

    [Fact]
    public void MatchingCaptureIsCompatibleButChangedProjectIsNot()
    {
        Assert.True(TraceDiagnosticCapture.Compare(Capture(), Capture()).IsCompatible);
        Assert.Equal("mismatch", TraceDiagnosticCapture.Compare(Capture(), Capture(identity: "project-b")).ProjectIdentityStatus);
    }

    [Fact]
    public void SavedEnvelopeRetainsHostTimingUnknownFieldsAndProjectionError()
    {
        var json = """
        {"schemaVersion":"pangloss.trace-details.v2","word":"sagd",
         "search":{"completed":false,"capped":true,"timedOut":false,"invalidShape":false,"steps":12,"elapsedNs":9},
         "result":{"signature":"root:sagd","guessed":false,"analyses":[{"analysisId":"analysis-0","index":0,"morphemes":"root","surface":"sagd","projection":{"status":"unavailable","error":"recorded projection failed","errorCode":"Example"},"morphs":[]}]},
         "categories":{},"trace":null,"extension":{"preserve":[1,2,3]},
         "hostCapture":{"projectIdentity":"project-a","grammarHash":"abc","grammarHashSemantics":"snapshot-semantic-sha256-v1","capturedUtc":"2026-09-22T12:00:00Z","wallElapsedMs":1234,"writingSystems":[{"id":"ar","name":"Arabic","isVernacular":true,"isDefault":true,"direction":"rtl","font":"Noto Sans Arabic"}]}}
        """;
        var outcome = WordTraceQuery.LoadDiagnostic(json, current: Capture());
        Assert.True(outcome.Succeeded);
        var response = outcome.Value!;
        Assert.Equal(json, response.DiagnosticJson);
        Assert.Equal(1234, response.HostCapture!.WallElapsedMs);
        Assert.True(response.Parsed);
        Assert.False(response.Complete);
        Assert.Contains("recorded projection failed", Assert.Single(response.Analyses).ProjectionError);
        Assert.True(response.Provenance!.IsCompatible);
        using var parsed = JsonDocument.Parse(response.DiagnosticJson);
        Assert.Equal(3, parsed.RootElement.GetProperty("extension").GetProperty("preserve").GetArrayLength());
    }
}
