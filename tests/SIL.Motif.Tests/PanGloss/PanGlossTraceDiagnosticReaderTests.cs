using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

public sealed class PanGlossTraceDiagnosticReaderTests
{
    [Fact]
    public void V1ReaderRetainsRawEnvelopeAnalysesCountersOrderAndUnknownFields()
    {
        const string json = "{" +
            "\"schemaVersion\":\"pangloss.trace-details.v1\",\"word\":\"sagd\",\"extension\":{\"keep\":true}," +
            "\"search\":{\"completed\":true,\"capped\":false,\"timedOut\":false,\"invalidShape\":false,\"steps\":3,\"elapsedNs\":7}," +
            "\"result\":{\"signature\":\"a|b\",\"guessed\":false," +
            "\"analyses\":[{\"morphemes\":\"root+suffix\",\"surface\":\"sagd\"},{\"morphemes\":\"other\",\"surface\":\"sagd\"}]}," +
            "\"categories\":{\"custom\":{\"attempts\":1,\"work\":9,\"outputs\":2,\"notApplied\":3,\"noRoot\":4,\"surfaceMismatch\":5,\"uses\":6,\"timingAvailable\":false,\"selfElapsedNs\":null}}," +
            "\"trace\":{\"type\":\"Successful\",\"subrule\":4,\"children\":[]}}";

        var result = PanGlossTraceDiagnosticReader.Read(json);

        Assert.Equal("pangloss.trace-details.v1", result.SchemaVersion);
        Assert.Equal("sagd", result.Word);
        Assert.Equal(2, result.Analyses.Count);
        Assert.Equal("root+suffix", result.Analyses[0].LegacyMorphemes);
        Assert.Equal("other", result.Analyses[1].LegacyMorphemes);
        Assert.Equal(9, Assert.Single(result.Details.Categories).Work);
        Assert.Equal(4, result.Root!.Subrule);
        Assert.Contains("\"extension\":{\"keep\":true}", result.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidShapeIsACompletedDocumentWithAnExplicitInvalidState()
    {
        const string json = "{" +
            "\"schemaVersion\":\"pangloss.trace-details.v1\",\"word\":\"bad\"," +
            "\"search\":{\"completed\":true,\"capped\":false,\"timedOut\":false,\"invalidShape\":true,\"steps\":0,\"elapsedNs\":0}," +
            "\"result\":{\"signature\":\"-\",\"guessed\":false,\"analyses\":[]},\"categories\":{},\"trace\":null}";

        var result = PanGlossTraceDiagnosticReader.Read(json);

        Assert.True(result.Details.InvalidShape);
        Assert.False(result.Details.SearchCompleted);
    }

    [Fact]
    public void V2ReaderKeepsAnalysesSeparateFromTraceAttemptsAndReadsSiblingContext()
    {
        const string json = """
{"schemaVersion":"pangloss.trace-details.v2","word":"sagd","search":{"completed":true,"capped":false,"timedOut":false,"invalidShape":false,"steps":1,"elapsedNs":9},"result":{"signature":"a","guessed":false,"analyses":[{"analysisId":"analysis-0","index":0,"surface":"sagd","projection":{"profile":"fieldworks-parse-analysis/v1","status":"available"},"morphs":[{"identity":{"formId":"f1","entryId":"e1","msaId":"m1","quality":"exact"},"form":{"text":"sag","writingSystem":"en","sourceId":"f1"},"headword":{"text":"say"},"gloss":{"text":"say"},"msa":{"category":{"name":"verb","abbreviation":"v"}},"slot":{"name":"past","optional":false},"features":{"status":"recorded"}}]}]},"categories":{},"trace":{"type":"Failed","children":[],"outcome":{"status":"failed","eventType":"surface-mismatch"},"attemptedMorphs":[{"identity":{"formId":"f1"},"form":{"text":"sag"}}],"failureContext":{"status":"recorded","reason":"required feature missing","required":{"tense":"past"},"actual":{"tense":"present"},"environment":"final"},"sourceIdentity":{"kind":"morphologicalRule","id":"rule-1","quality":"exact"}}}
""";

        var document = PanGlossTraceDiagnosticReader.Read(json);

        Assert.Single(document.Analyses);
        Assert.Equal("analysis-0", document.Analyses[0].AnalysisId);
        Assert.Equal("say", document.Analyses[0].Morphs[0].Headword);
        var attempt = Assert.Single(document.Attempts);
        Assert.False(attempt.Succeeded);
        Assert.Equal("required feature missing", attempt.FailureContext);
        Assert.Equal("{\"tense\":\"past\"}", attempt.FailureRequired);
        Assert.Equal("rule-1", attempt.SourceIdentityId);
        Assert.Equal("sag", attempt.Morphs[0].Form);
    }

    [Fact]
    public void ProducerV2FixturePreservesAnalysisOrderRawJsonAndAttemptedMorphs()
    {
        var raw = ReadFixture("trace-details-v2-matinlu.json");

        var document = PanGlossTraceDiagnosticReader.Read(raw);

        Assert.True(document.IsV2);
        Assert.Equal(raw, document.RawJson);
        Assert.Equal("matinlu", document.Word);
        Assert.Equal(new[] { "analysis-0", "analysis-1" }, document.Analyses.Select(analysis => analysis.AnalysisId));
        Assert.Equal(new int?[] { 0, 1 }, document.Analyses.Select(analysis => analysis.Index));
        Assert.Equal(new[] { "MA+TIN+LU", "MA+TIN+LU" }, document.Analyses.Select(analysis => analysis.LegacyMorphemes));
        Assert.All(document.Analyses, analysis =>
        {
            Assert.Equal("unavailable", analysis.Availability);
            Assert.Contains("morpheme ordinal 0 has no authoritative source MSA GUID", analysis.ProjectionError, StringComparison.Ordinal);
            Assert.Empty(analysis.Morphs);
        });

        Assert.Contains("\"errorCode\":\"MissingMsa { ordinal: 0 }\"", raw, StringComparison.Ordinal);
        Assert.Equal(6, document.Attempts.Count);
        Assert.Equal("successful", document.Attempts[0].Status);
        Assert.Equal("Successful", document.Attempts[0].EventType);
        Assert.True(document.Attempts[0].Succeeded);
        Assert.Equal(3, document.Attempts[0].Morphs.Count);
        Assert.Contains("\"allomorphId\":0", document.Attempts[0].Morphs[0].Raw.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("failed", document.Attempts[1].Status);
        Assert.Equal("PartialParse", document.Attempts[1].FailureReason);
        Assert.Equal("unavailable", document.Attempts[1].FailureContext);
        Assert.Contains("\"reasonCode\":\"PartialParse\"", document.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderAcceptsTraceDeeperThanDefaultJsonDepth()
    {
        const int depth = 70;
        var trace = new StringBuilder();
        for (var index = 0; index < depth; index++)
            trace.Append("{\"type\":\"Container\",\"children\":[");
        trace.Append("{\"type\":\"Successful\",\"children\":[]}");
        for (var index = 0; index < depth; index++)
            trace.Append("]}");

        var json = "{\"schemaVersion\":\"pangloss.trace-details.v2\",\"word\":\"deep\",\"search\":{" +
            "\"completed\":true,\"capped\":false,\"timedOut\":false,\"invalidShape\":false,\"steps\":1,\"elapsedNs\":0}," +
            "\"result\":{\"signature\":\"deep\",\"guessed\":false,\"analyses\":[]},\"categories\":{},\"trace\":" +
            trace + "}";

        var document = PanGlossTraceDiagnosticReader.Read(json);
        var node = document.Root;
        for (var index = 0; index < depth; index++)
            node = Assert.Single(node!.Children);

        Assert.NotNull(node);
        Assert.Equal("Successful", node!.Type);
        Assert.Single(document.Attempts);
    }

    [Fact]
    public void MalformedNumericFieldProducesUsefulFormatError()
    {
        const string json = "{" +
            "\"schemaVersion\":\"pangloss.trace-details.v1\",\"word\":\"bad\"," +
            "\"search\":{\"completed\":true,\"capped\":false,\"timedOut\":false,\"invalidShape\":false," +
            "\"steps\":9223372036854775808,\"elapsedNs\":0}," +
            "\"result\":{\"signature\":\"-\",\"guessed\":false,\"analyses\":[]},\"categories\":{},\"trace\":null}";

        var exception = Assert.Throws<PanGlossTraceDiagnosticFormatException>(() =>
            PanGlossTraceDiagnosticReader.Read(json));

        Assert.Contains("invalid field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownMajorVersionAndMalformedJsonHaveUsefulErrors()
    {
        var unknown = Assert.Throws<PanGlossTraceDiagnosticFormatException>(() =>
            PanGlossTraceDiagnosticReader.Read("{\"schemaVersion\":\"pangloss.trace-details.v9\"}"));
        Assert.Contains("unsupported", unknown.Message, StringComparison.OrdinalIgnoreCase);

        var malformed = Assert.Throws<PanGlossTraceDiagnosticFormatException>(() =>
            PanGlossTraceDiagnosticReader.Read("{\"schemaVersion\":\"pangloss.trace-details.v1\","));
        Assert.Contains("JSON", malformed.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadFixture(string name, [CallerFilePath] string sourceFile = "")
    {
        var testDirectory = Path.GetDirectoryName(sourceFile)!;
        return File.ReadAllText(Path.GetFullPath(Path.Combine(testDirectory, "..", "TestFixtures", name)));
    }
}
