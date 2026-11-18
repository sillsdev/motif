using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace SIL.Motif.Tests.Contract.HandAnalyses;

public sealed class HandAnalysisSchemaTests
{
    private static readonly Lazy<JsonSchema> Schema = new(CreateSchema);

    public static JsonObject Input()
    {
        string Id() => Guid.NewGuid().ToString();
        return new JsonObject
        {
            ["schema"] = "motif.hand-analysis/1", ["requestId"] = Id(),
            ["expected"] = new JsonObject
            {
                ["projectGuid"] = Id(), ["homeRevision"] = "saved", ["occurrenceHeads"] = new JsonArray(),
                ["contextHash"] = "context", ["segmentationId"] = "segmentation"
            },
            ["actor"] = new JsonObject { ["kind"] = "agent" },
            ["intentSource"] = new JsonObject { ["kind"] = "analyst" },
            ["occurrence"] = new JsonObject
            {
                ["kind"] = "scripture", ["sourceId"] = Id(), ["versification"] = "English",
                ["anchor"] = "MAT 5:3#2", ["verseHash"] = "verse"
            },
            ["wordform"] = new JsonObject { ["ws"] = "fr", ["surface"] = "surface", ["caseForm"] = "surface" },
            ["categoryId"] = null, ["pieces"] = new JsonArray(), ["creations"] = new JsonArray(),
            ["wordGloss"] = new JsonObject { ["mode"] = "none", ["enabledWs"] = new JsonArray() },
            ["approve"] = true
        };
    }

    [Fact]
    public void ExactlyOneOccurrenceAndExplicitApprovalAreRequired()
    {
        var input = Input();
        Assert.True(Validate(input));
        input["approve"] = false;
        Assert.False(Validate(input));
        input.Remove("approve");
        Assert.False(Validate(input));
        input["approve"] = true;
        input["occurrence"] = new JsonArray(Input()["occurrence"]!.DeepClone());
        Assert.False(Validate(input));
        input.Remove("occurrence");
        Assert.False(Validate(input));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("occurrences")]
    [InlineData("bulk")]
    public void UnknownFieldsAreRejected(string field)
    {
        var input = Input();
        input[field] = true;
        Assert.False(Validate(input));
    }

    [Fact]
    public void AnchorAndGlossBranchesRejectIrrelevantFields()
    {
        var input = Input();
        input["occurrence"]!["textId"] = Guid.NewGuid().ToString();
        Assert.False(Validate(input));
        input = Input();
        input["wordGloss"]!["id"] = Guid.NewGuid().ToString();
        Assert.False(Validate(input));
        input["wordGloss"]!["mode"] = "select";
        Assert.True(Validate(input));
        input["wordGloss"]!["forms"] = new JsonObject();
        Assert.False(Validate(input));
    }

    [Fact]
    public void InvalidIdsAndWritingSystemTagsAreRejected()
    {
        var input = Input();
        input["expected"]!["projectGuid"] = "not-an-id";
        Assert.False(Validate(input));
        input = Input();
        input["wordform"]!["ws"] = "not a tag";
        Assert.False(Validate(input));
    }

    [Fact]
    public void ParserAcceptsValidInputAndReportsNestedJsonPointers()
    {
        var input = Input();
        var valid = SIL.Motif.Contract.HandAnalyses.HandAnalysisJson.Parse(input.ToJsonString());
        Assert.Empty(valid.Errors);
        Assert.NotNull(valid.Input);
        input["wordform"]!["unknown"] = true;
        var invalid = SIL.Motif.Contract.HandAnalyses.HandAnalysisJson.Parse(input.ToJsonString());
        Assert.Null(invalid.Input);
        Assert.Contains(invalid.Errors, error => error.Pointer == "/wordform/unknown");
    }

    [Fact]
    public void PartialNativeLinksAndSenseLessVariantAreRepresentable()
    {
        var input = Input();
        var sense = Guid.NewGuid().ToString();
        input["pieces"] = new JsonArray(new JsonObject
        {
            ["pieceKey"] = "piece", ["form"] = new JsonObject
            {
                ["ws"] = "fr", ["text"] = "typed", ["markers"] = new JsonObject { ["before"] = "", ["after"] = "" }
            },
            ["link"] = new JsonObject
            {
                ["entry"] = null, ["allomorph"] = null, ["sense"] = sense, ["msa"] = null, ["inflTypeId"] = null
            },
            ["variantRef"] = Guid.NewGuid().ToString()
        });
        input["creations"] = new JsonArray(new JsonObject
        {
            ["kind"] = "variant-entry", ["key"] = "variant", ["formKey"] = "form", ["refKey"] = "ref",
            ["forms"] = new JsonObject(), ["morphTypeId"] = Guid.NewGuid().ToString(),
            ["component"] = new JsonObject { ["senseId"] = sense }, ["variantTypeIds"] = new JsonArray()
        });
        Assert.True(Validate(input));
        var parsed = SIL.Motif.Contract.HandAnalyses.HandAnalysisJson.Parse(input.ToJsonString());
        Assert.Empty(parsed.Errors);
        Assert.NotNull(parsed.Input);
        Assert.Equal("AnalysisOnly", parsed.Input.LexiconPolicy);
        input["creations"]![0]!["senseKey"] = "invented";
        Assert.False(Validate(input));
        input["creations"]![0]!.AsObject().Remove("senseKey");
        input["creations"]![0]!["component"]!["entryId"] = Guid.NewGuid().ToString();
        Assert.False(Validate(input));
    }

    [Fact]
    public void TextAnchorRequiresNativeGuardsAndGlossAutoRequiresEnabledWritingSystems()
    {
        var input = Input();
        input["occurrence"] = new JsonObject
        {
            ["kind"] = "text", ["textId"] = Guid.NewGuid().ToString(),
            ["paragraphId"] = Guid.NewGuid().ToString(), ["segmentId"] = Guid.NewGuid().ToString(),
            ["index"] = 0, ["wordPosition"] = 1
        };
        Assert.False(Validate(input));
        input["expected"]!["assignmentId"] = null;
        input["expected"]!["parseIsCurrent"] = true;
        Assert.True(Validate(input));
        input["wordGloss"] = new JsonObject { ["mode"] = "auto", ["enabledWs"] = new JsonArray(), ["forms"] = new JsonObject() };
        Assert.False(Validate(input));
        input["wordGloss"]!["enabledWs"] = new JsonArray("en");
        Assert.True(Validate(input));
        input["wordGloss"]!["id"] = Guid.NewGuid().ToString();
        Assert.False(Validate(input));
    }

    [Fact]
    public void DuplicatePropertiesAndPieceKeysAreRefused()
    {
        var input = Input();
        var json = input.ToJsonString().Replace("\"approve\":true", "\"approve\":true,\"approve\":true");
        Assert.Contains(SIL.Motif.Contract.HandAnalyses.HandAnalysisJson.Parse(json).Errors,
            error => error.Pointer == "/approve");
        var piece = new JsonObject
        {
            ["pieceKey"] = "same", ["form"] = new JsonObject
            {
                ["ws"] = "fr", ["text"] = "typed", ["markers"] = new JsonObject { ["before"] = "", ["after"] = "" }
            }, ["link"] = null
        };
        input["pieces"] = new JsonArray(piece, piece.DeepClone());
        Assert.Contains(SIL.Motif.Contract.HandAnalyses.HandAnalysisJson.Parse(input.ToJsonString()).Errors,
            error => error.Pointer == "/pieces/1/pieceKey");
    }

    [Fact]
    public void CtrlERequiresCurrentChapterOrTextAndNeverAcceptsACorpus()
    {
        var scope = new JsonObject
        {
            ["policy"] = "CurrentChapterOrTextBareEitherCase", ["population"] = new JsonObject
            {
                ["kind"] = "chapter", ["sourceId"] = Guid.NewGuid().ToString(), ["versification"] = "English",
                ["book"] = "MAT", ["chapter"] = 5
            }
        };
        Assert.True(Definition("ElsewhereScope").Evaluate(JsonSerializer.SerializeToElement(scope)).IsValid);
        scope["population"] = new JsonObject { ["kind"] = "corpus", ["corpusId"] = Guid.NewGuid().ToString() };
        Assert.False(Definition("ElsewhereScope").Evaluate(JsonSerializer.SerializeToElement(scope)).IsValid);
        scope["policy"] = "SameWordformAndGuess";
        Assert.True(Definition("ElsewhereScope").Evaluate(JsonSerializer.SerializeToElement(scope)).IsValid);
    }

    [Fact]
    public void CanonicalIdsNormalizeToUpstreamUuidsAndResponsesAreClosed()
    {
        var input = Input();
        var id = Guid.NewGuid();
        input["expected"]!["projectGuid"] = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(id, "project_").Value;
        var parsed = SIL.Motif.Contract.HandAnalyses.HandAnalysisJson.Parse(input.ToJsonString());
        Assert.Empty(parsed.Errors);
        Assert.Equal(id, parsed.Input!.Expected.ProjectGuid);
        var response = new SIL.Motif.Contract.HandAnalyses.HandAnalysisCheckResponse(parsed.Input.RequestId, id, [], null, [], []);
        var node = SIL.Motif.Contract.Responses.ProjectionJson.ToNode(response)!;
        Assert.True(Definition("CheckResponse").Evaluate(JsonSerializer.SerializeToElement(node)).IsValid);
        node["auditId"] = "invented";
        Assert.False(Definition("CheckResponse").Evaluate(JsonSerializer.SerializeToElement(node)).IsValid);
    }

    [Fact]
    public void FailureCodesUseTheSharedEnvelopeAndRejectUnrelatedCodes()
    {
        var schema = Definition("FailureEnvelope");
        foreach (var code in new[]
        {
            "hand-analysis.stale", "hand-analysis.ambiguous-wordform", "hand-analysis.invalid-link",
            "hand-analysis.type-mismatch", "hand-analysis.disapproved", "hand-analysis.request-id-reused",
            "hand-analysis.busy", "hand-analysis.store-unavailable", "hand-analysis.outcome-unknown"
        })
        {
            var spec = SIL.Motif.Contract.Commands.RefusalCatalog.Specs[code];
            var envelope = new SIL.Motif.Contract.Responses.FailureEnvelope(spec.DefaultReason, "Refused.",
                new Dictionary<string, string> { ["pointer"] = "/pieces/0" }, code);
            var node = SIL.Motif.Contract.Responses.ProjectionJson.ToNode(envelope)!;
            Assert.True(schema.Evaluate(JsonSerializer.SerializeToElement(node)).IsValid, code);
            node["extra"] = true;
            Assert.False(schema.Evaluate(JsonSerializer.SerializeToElement(node)).IsValid);
            node.AsObject().Remove("extra");
            node["code"] = "apply.not-ready";
            Assert.False(schema.Evaluate(JsonSerializer.SerializeToElement(node)).IsValid);
        }
    }

    private static JsonSchema Definition(string name)
    {
        using var stream = typeof(SIL.Motif.Contract.Responses.FailureEnvelope).Assembly
            .GetManifestResourceStream("SIL.Motif.Contract.Schemas.hand-analysis.schema.json")!;
        var node = JsonNode.Parse(stream)!;
        node["$ref"] = "#/$defs/" + name;
        return JsonSchema.FromText(node.ToJsonString(), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
    }

    private static bool Validate(JsonNode input) => Schema.Value.Evaluate(JsonSerializer.SerializeToElement(input)).IsValid;

    private static JsonSchema CreateSchema()
    {
        using var stream = typeof(SIL.Motif.Contract.Responses.FailureEnvelope).Assembly
            .GetManifestResourceStream("SIL.Motif.Contract.Schemas.hand-analysis.schema.json");
        Assert.NotNull(stream);
        return JsonSchema.FromText(new StreamReader(stream).ReadToEnd(), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
    }
}
