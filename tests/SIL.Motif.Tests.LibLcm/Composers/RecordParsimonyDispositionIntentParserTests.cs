using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.HumanJudgments;
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

    private static JsonDocument EdgeIntent(string role) => JsonDocument.Parse("""
        {
          "recordTypeId": "cmpossibility_0000000000000000000001",
          "measureId": "P-allo-duplicate-form",
          "subject": { "kind": "edge", "role": ROLE,
            "owner": { "class": "MoStemAllomorph", "id": "mostemallomorph_0000000000000000000002" },
            "from": { "class": "MoStemAllomorph", "id": "mostemallomorph_1000000000000000000000" },
            "to": { "class": "MoStemAllomorph", "id": "mostemallomorph_2000000000000000000000" } },
          "disposition": "keep",
          "evidenceDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "evidenceContract": "P-allo-duplicate-form/v1",
          "subjectCaption": "listed plural forms",
          "measureCaption": "Duplicate allomorph forms"
        }
        """.Replace("ROLE", role));

    [Fact]
    public void AnEdgeRoleIsTheAdvertisedKebabCaseString()
    {
        using var document = EdgeIntent("\"left-context\"");

        var intent = RecordParsimonyDispositionIntentParser.Parse(document.RootElement);

        var edge = Assert.IsType<EdgeJudgmentSubject>(intent.Subject);
        Assert.Equal(JudgmentEdgeRole.LeftContext, edge.Role);
    }

    [Fact]
    public void AnEdgeRoleGivenAsANumberIsRefused()
    {
        using var document = EdgeIntent("3");

        Assert.Throws<ContractParseException>(() =>
            RecordParsimonyDispositionIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void AGroupRoleIsTheAdvertisedKebabCaseString()
    {
        using var document = JsonDocument.Parse("""
            {
              "recordTypeId": "cmpossibility_0000000000000000000001",
              "measureId": "P-allo-duplicate-form",
              "subject": { "kind": "computed-group", "measureId": "P-allo-duplicate-form",
                "role": "duplicate-pair", "members": [
                  { "class": "MoStemAllomorph", "id": "mostemallomorph_1000000000000000000000" },
                  { "class": "MoStemAllomorph", "id": "mostemallomorph_2000000000000000000000" }] },
              "disposition": "keep",
              "evidenceDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "evidenceContract": "P-allo-duplicate-form/v1",
              "subjectCaption": "listed plural forms",
              "measureCaption": "Duplicate allomorph forms"
            }
            """);

        var intent = RecordParsimonyDispositionIntentParser.Parse(document.RootElement);

        Assert.Equal(JudgmentGroupRole.DuplicatePair, Assert.IsType<GroupJudgmentSubject>(intent.Subject).Role);
    }

    [Fact]
    public void ASubjectMixingTwoKindsFieldsIsRefusedWithTheParsersMessage()
    {
        using var document = EdgeIntent("\"membership\"");
        var mixed = JsonDocument.Parse(document.RootElement.GetRawText()
            .Replace("\"kind\": \"edge\"", "\"kind\": \"project\", \"id\": \"x\""));

        var error = Assert.Throws<ContractParseException>(() =>
            RecordParsimonyDispositionIntentParser.Parse(mixed.RootElement));

        Assert.Contains("invalid closed 'subject'", error.Message);
    }

    [Fact]
    public void AnIntentBuiltInCodeRecordsTheSameFindingIdentityAsTheAuthoredForm()
    {
        using var authored = EdgeIntent("\"right-context\"");
        var intent = RecordParsimonyDispositionIntentParser.Parse(authored.RootElement);

        var recorded = RecordParsimonyDispositionDefinition.ToAuthored(intent);

        Assert.Equal("right-context", recorded.GetProperty("subject").GetProperty("role").GetString());
        var identity = new RecordParsimonyDispositionDefinition().Replaces!.Identity!;
        Assert.NotNull(identity(authored.RootElement));
        Assert.Equal(identity(authored.RootElement), identity(recorded));
    }
}
