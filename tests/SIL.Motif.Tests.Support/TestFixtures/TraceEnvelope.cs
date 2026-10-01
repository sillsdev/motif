namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Producer diagnostic envelopes with recorded category shapes, plus host capture evidence for display tests.
/// </summary>
internal static class TraceEnvelope
{
    internal const string CapturedRuleLabel = """
        {"schemaVersion":"pangloss.trace-details.v2","word":"word",
         "search":{"completed":true,"capped":false,"timedOut":false,"invalidShape":false,"steps":3,"elapsedNs":2},
         "result":{"signature":"","guessed":false,"analyses":[]},"categories":{},
         "hostCapture":{"traceLabels":[{"refId":"phonRule:rule-id","label":"Vowel harmony"}]},
         "trace":{"type":"WordAnalysis","children":[
           {"type":"PhonologicalRuleSynthesis","source":"Producer name",
            "sourceIdentity":{"kind":"phonRule","id":"rule-id","quality":"authored"},
            "failureReason":"RequiredSyntacticFeatureStruct","children":[]},
           {"type":"Failed","failureReason":"PartialParse","children":[]}]}}
        """;

    /// <summary>The document for <paramref name="tree"/>; a <see langword="null"/> tree is an untraced word.</summary>
    internal static string Of(
        string signature, string? tree, bool capped = false, bool invalidShape = false, bool guessed = false) =>
        "{\"categories\":{\"lexEntry\":{\"attempts\":1,\"noRoot\":0,\"notApplied\":0,\"outputs\":0," +
        "\"selfElapsedNs\":4800,\"surfaceMismatch\":0,\"timingAvailable\":true,\"uses\":1,\"work\":0}," +
        "\"phonRule\":{\"attempts\":0,\"noRoot\":0,\"notApplied\":0,\"outputs\":0,\"selfElapsedNs\":null," +
        "\"surfaceMismatch\":0,\"timingAvailable\":false,\"uses\":0,\"work\":0}," +
        "\"rootIndex\":{\"attempts\":2,\"noRoot\":1,\"notApplied\":0,\"outputs\":0,\"selfElapsedNs\":null," +
        "\"surfaceMismatch\":0,\"timingAvailable\":false,\"uses\":0,\"work\":11}}," +
        "\"result\":{\"analyses\":[],\"guessed\":" + Bool(guessed) + ",\"signature\":\"" + signature + "\"}," +
        "\"schemaVersion\":\"pangloss.trace-details.v1\"," +
        "\"search\":{\"capped\":" + Bool(capped) + ",\"completed\":" + Bool(!capped && !invalidShape) +
        ",\"elapsedNs\":287600,\"invalidShape\":" + Bool(invalidShape) + ",\"steps\":17,\"timedOut\":false}," +
        "\"trace\":" + (tree ?? "null") + ",\"word\":\"sagd\"}";

    private static string Bool(bool value) => value ? "true" : "false";
}
