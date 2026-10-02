namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Producer diagnostic envelopes with recorded category shapes, plus host capture evidence for display tests.
/// </summary>
internal static class TraceEnvelope
{
    internal const string CapturedRuleId = "aaaaaaaa-0000-0000-0000-000000000001";

    internal const string UnresolvedRuleTiming = """
        {"schemaVersion":"pangloss.trace-details.v3","word":"word",
         "search":{"completed":true,"capped":false,"timedOut":false,"invalidShape":false,"steps":3,"elapsedNs":2},
         "result":{"signature":"","guessed":false,"analyses":[]},"categories":{},
         "trace":{"type":"MorphologicalRuleSynthesis","source":"Rule",
          "sourceIdentity":{"kind":"morphRule","id":"","quality":"structural"},
          "children":[{"type":"Successful","children":[]}]}}
        """;

    internal const string CapturedRuleLabel = """
        {"schemaVersion":"pangloss.trace-details.v3","word":"word",
         "search":{"completed":true,"capped":false,"timedOut":false,"invalidShape":false,"steps":3,"elapsedNs":2},
         "result":{"signature":"","guessed":false,"analyses":[]},"categories":{},
         "hostCapture":{"traceLabels":[{"refId":"phonRule:aaaaaaaa-0000-0000-0000-000000000001","label":"Vowel harmony"}]},
         "trace":{"type":"WordAnalysis","children":[
           {"type":"PhonologicalRuleSynthesis","source":"Producer name",
            "sourceIdentity":{"kind":"phonRule","id":"aaaaaaaa-0000-0000-0000-000000000001","quality":"authored"},
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
        "\"schemaVersion\":\"pangloss.trace-details.v3\"," +
        "\"search\":{\"capped\":" + Bool(capped) + ",\"completed\":" + Bool(!capped && !invalidShape) +
        ",\"elapsedNs\":287600,\"invalidShape\":" + Bool(invalidShape) + ",\"steps\":17,\"timedOut\":false}," +
        "\"trace\":" + (tree ?? "null") + ",\"word\":\"sagd\"}";

    internal const string InterruptedTree = """
        {"type":"WordAnalysis","children":[{"type":"MorphologicalRuleAnalysis","source":"plural",
          "inputShape":"words","outputShape":"word","children":[]}]}
        """;

    internal static string AnalysisRecords(bool equalRendering = false, bool available = true) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = "pangloss.trace-details.v3", word = "word",
            search = new { completed = true, capped = false, timedOut = false, invalidShape = false, steps = 3, elapsedNs = 2 },
            result = new
            {
                signature = equalRendering ? "ROOT|word;ROOT|word;ROOT|word" : "A|word;A|word;B|word",
                guessed = false,
                analyses = new[] { Record("a0", 8, "A", 1, equalRendering, available), Record("b1", 4, "B", 2, equalRendering, available), Record("a2", 9, "A", 1, equalRendering, available) },
            },
            categories = new { }, trace = (object?)null,
        });

    private static object Record(string id, int index, string text, int identity, bool equalRendering, bool available) => new
    {
        analysisId = id, index, morphemes = equalRendering ? "ROOT" : text, surface = "word",
        projection = new { status = available ? "available" : "unavailable", errorCode = available ? null : "E" },
        morphs = available ? new object[] { new
        {
            form = equalRendering ? "ROOT" : text,
            identity = new { formId = $"00000000-0000-0000-0000-{identity:000000000000}",
                msaId = $"00000000-0000-0000-0001-{identity:000000000000}", quality = "authored" },
        } } : [],
    };

    private static string Bool(bool value) => value ? "true" : "false";
}
