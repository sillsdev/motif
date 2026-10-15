using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class PhonologyContractTests
{
    [Fact]
    public void ClosedExampleContractsRefuseInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set", []));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set",
            Enumerable.Range(0, 501).Select(index => Example($"e{index}")).ToArray()));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set",
            [Example("same"), Example("same")]));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set",
            [Example("e", expected: Enumerable.Range(0, 17).Select(index => $"form-{index}").ToArray())]));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set",
            [Example("e", underlying: "")]));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set",
            [Example("e", expected: [""])]));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set",
            [Example("e", context: new PhonologyGenerationContext.FwdataEntry(
                "not-a-guid", "00000000-0000-0000-0000-000000000001", PhonologyMsaClass.MoStemMsa))]));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set",
            [Example("e", context: new PhonologyGenerationContext.FwdataEntry(
                "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000002",
                (PhonologyMsaClass)42))]));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set",
            [Example("e", citation: new CitationView("", null))]));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ValidateAndCopy("Set",
            [Example("e", citation: new CitationView(new string('s', 301), null))]));
    }

    [Fact]
    public void SaveFileParsingRefusesUnknownDuplicateAndOversizedNestedPayloads()
    {
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ParseSaveFile(
            """{"name":"Set","examples":[{"id":"e","underlying":"u","expected":["s"],"context":{"kind":"hcXmlEntry","entryId":"x","extra":true}}]}""",
            "/project.fwdata"));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ParseSaveFile(
            """{"name":"Set","examples":[{"id":"e","underlying":"u","expected":["s"],"context":{"kind":"hcXmlEntry","entryId":"x","entryId":"y"}}]}""",
            "/project.fwdata"));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ParseSaveFile(
            """{"name":"Set","name":"Other","examples":[]}""", "/project.fwdata"));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ParseSaveFile(
            $$"""{"name":"Set","examples":[],"payload":"{{new string('x', PhonologyExampleValidation.MaxPayloadUtf8Bytes)}}"}""",
            "/project.fwdata"));
        Assert.Throws<ArgumentException>(() => PhonologyExampleValidation.ParseSaveFile(
            """{"name":"Set","examples":[{"id":"e","underlying":"u","context":{"kind":"hcXmlEntry","entryId":"x"}}]}""",
            "/project.fwdata"));
    }

    [Fact]
    public void ContextUnionUsesClosedStringDiscriminators()
    {
        const string json = """{"kind":"fwdataEntry","entryGuid":"00000000-0000-0000-0000-000000000001","msaGuid":"00000000-0000-0000-0000-000000000002","msaClass":"MoStemMsa"}""";
        var context = JsonSerializer.Deserialize<PhonologyGenerationContext>(json, PhonologyJson.CreateOptions());

        Assert.IsType<PhonologyGenerationContext.FwdataEntry>(context);
        var kindLast = JsonSerializer.Deserialize<PhonologyGenerationContext>(
            """{"entryId":"entry-1","kind":"hcXmlEntry"}""", PhonologyJson.CreateOptions());
        Assert.IsType<PhonologyGenerationContext.HcXmlEntry>(kindLast);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyGenerationContext>(
            """{"kind":"future","entryId":"x"}""", PhonologyJson.CreateOptions()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyGenerationContext>(
            """{"kind":"hcXmlEntry","entryId":"x","other":true}""", PhonologyJson.CreateOptions()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyGenerationContext>(
            """{"kind":"hcXmlEntry"}""", PhonologyJson.CreateOptions()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyGenerationContext>(
            """{"kind":"fwdataEntry","entryGuid":"00000000-0000-0000-0000-000000000001","msaGuid":"00000000-0000-0000-0000-000000000002"}""",
            PhonologyJson.CreateOptions()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyExample>(
            """{"id":"e","underlying":"u","context":{"entryId":"x","kind":"hcXmlEntry"}}""",
            PhonologyJson.CreateOptions()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyGenerationContext>(
            """{"kind":"fwdataEntry","entryGuid":"00000000-0000-0000-0000-000000000001","msaGuid":"00000000-0000-0000-0000-000000000002","msaClass":0}""",
            PhonologyJson.CreateOptions()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("\"mostemmsa\"")]
    public void BroadMotifJsonOptionsStillRefuseInvalidMsaClassValues(string msaClass)
    {
        var json = "{\"id\":\"e\",\"underlying\":\"u\",\"expected\":[\"s\"]," +
            "\"context\":{\"kind\":\"fwdataEntry\",\"entryGuid\":\"00000000-0000-0000-0000-000000000001\"," +
            "\"msaGuid\":\"00000000-0000-0000-0000-000000000002\",\"msaClass\":" + msaClass + "}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyExample>(json, MotifJson.CreateOptions()));
    }

    [Fact]
    public void ExecutableDigestUsesTheVersionedCanonicalProjection()
    {
        var input = PhonologyExampleValidation.ValidateAndCopy("Ignored name", [Example("e1", "a", ["b"])]);

        Assert.Equal("sha256:a2cb04d024ba15b0647d1839535dcfa74c56bf2b81445e60c8f78928b5e41e4a",
            PhonologyExampleDigest.Executable(input.Examples));
    }

    [Fact]
    public void CitationChangesTheCitedDigestButNotExecutableContent()
    {
        var first = PhonologyExampleValidation.ValidateAndCopy("Set", [Example("e", citation: new CitationView("Source A", "p. 1"))]);
        var second = PhonologyExampleValidation.ValidateAndCopy("Set", [Example("e", citation: new CitationView("Source B", "p. 2"))]);

        Assert.Equal(first.ExecutableSha256, second.ExecutableSha256);
        Assert.NotEqual(first.CitedRevisionSha256, second.CitedRevisionSha256);
    }

    [Fact]
    public void InitialPhonologyCheckRequestCarriesOnlyItsBaselineBinding()
    {
        var digest = "sha256:" + new string('a', 64);
        var request = new PhonologyCheckRequest("project.fwdata", "example-revision/abc",
            new BaselineToken("project-identity", digest, "projection-v1", "2026-10-13T00:00:00Z", digest),
            new PhonologyLimits(100, 1000, 5000));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, PhonologyJson.CreateOptions()));

        Assert.Equal(["baseline", "limits", "projectPath", "revisionId"],
            json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(typeof(PhonologyCheckRequest).GetProperties(), property =>
            property.Name.Contains("Candidate", StringComparison.Ordinal) ||
            property.Name.Contains("Source", StringComparison.Ordinal));
    }

    private static PhonologyExample Example(string id, string underlying = "underlying",
        IReadOnlyList<string>? expected = null, PhonologyGenerationContext? context = null,
        CitationView? citation = null) => new(id, underlying, expected ?? ["surface"],
        context ?? new PhonologyGenerationContext.HcXmlEntry("entry-1"), citation);
}
