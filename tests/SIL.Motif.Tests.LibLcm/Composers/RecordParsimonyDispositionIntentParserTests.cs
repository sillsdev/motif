using System.Text.Json;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using Xunit;

namespace SIL.Motif.Tests.Composers;

public sealed class RecordParsimonyDispositionIntentParserTests
{
    [Fact]
    public void ParsesKeepWithoutAReason()
    {
        using var document = JsonDocument.Parse("""
            {
              "recordTypeId": "cmpossibility_0000000000000000000001",
              "measureId": "P-allo-duplicate-form",
              "subject": { "kind": "object", "object": {
                "class": "MoAffixAllomorph",
                "id": "moaffixallomorph_0000000000000000000002"
              } },
              "disposition": "keep",
              "evidenceDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "evidenceContract": "P-allo-duplicate-form/v1",
              "subjectCaption": "listed plural forms",
              "measureCaption": "Duplicate allomorph forms"
            }
            """);

        var intent = RecordParsimonyDispositionIntentParser.Parse(document.RootElement);

        Assert.Equal("keep", intent.Disposition.ToString().ToLowerInvariant());
        Assert.Null(intent.Reason);
        Assert.Null(intent.Question);
    }

    [Fact]
    public void AskRequiresAQuestionButNotAReason()
    {
        using var document = JsonDocument.Parse("""
            {
              "recordTypeId": "cmpossibility_0000000000000000000001",
              "measureId": "P-allo-duplicate-form",
              "subject": { "kind": "object", "object": {
                "class": "MoAffixAllomorph",
                "id": "moaffixallomorph_0000000000000000000002"
              } },
              "disposition": "ask",
              "evidenceDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "evidenceContract": "P-allo-duplicate-form/v1",
              "subjectCaption": "listed plural forms",
              "measureCaption": "Duplicate allomorph forms"
            }
            """);

        Assert.Throws<ContractParseException>(() =>
            RecordParsimonyDispositionIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void RejectsUnknownPropertiesAtTheIntentAndSubjectLevels()
    {
        using var rootDocument = JsonDocument.Parse("""
            {
              "recordTypeId": "cmpossibility_0000000000000000000001",
              "measureId": "P-allo-duplicate-form",
              "subject": { "kind": "object", "object": {
                "class": "MoAffixAllomorph",
                "id": "moaffixallomorph_0000000000000000000002"
              } },
              "disposition": "keep",
              "evidenceDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "evidenceContract": "P-allo-duplicate-form/v1",
              "subjectCaption": "listed plural forms",
              "measureCaption": "Duplicate allomorph forms",
              "unexpected": true
            }
            """);
        using var subjectDocument = JsonDocument.Parse("""
            {
              "recordTypeId": "cmpossibility_0000000000000000000001",
              "measureId": "P-allo-duplicate-form",
              "subject": { "kind": "object", "object": {
                "class": "MoAffixAllomorph",
                "id": "moaffixallomorph_0000000000000000000002",
                "unexpected": true
              } },
              "disposition": "keep",
              "evidenceDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "evidenceContract": "P-allo-duplicate-form/v1",
              "subjectCaption": "listed plural forms",
              "measureCaption": "Duplicate allomorph forms"
            }
            """);

        Assert.Throws<ContractParseException>(() =>
            RecordParsimonyDispositionIntentParser.Parse(rootDocument.RootElement));
        Assert.Throws<ContractParseException>(() =>
            RecordParsimonyDispositionIntentParser.Parse(subjectDocument.RootElement));
    }
}
