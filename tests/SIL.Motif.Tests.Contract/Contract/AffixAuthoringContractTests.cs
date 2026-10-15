using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class AffixAuthoringContractTests
{
    [Fact]
    public void AuthoringSchemasAreEmbeddedAndEveryObjectIsClosed()
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.affix-authoring.schema.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        foreach (var definition in document.RootElement.GetProperty("$defs").EnumerateObject())
            if (definition.Value.TryGetProperty("type", out var type) && type.GetString() == "object")
                Assert.False(definition.Value.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void PortableAuthoringVectorsAgreeWithTheClosedParsers()
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.affix-authoring.vectors.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var accepted = 0;
        var refused = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("input");
            void Parse()
            {
                switch (vector.GetProperty("shape").GetString())
                {
                    case "AuthorLexicalEntry": _ = AuthorLexicalEntryIntentParser.Parse(input); break;
                    case "AuthorAlternateForm": _ = AuthorAlternateFormIntentParser.Parse(input); break;
                    case "AuthorFeatureStructure": _ = AuthorFeatureStructureIntentParser.Parse(input); break;
                    case "AuthorFeatureValue": _ = AuthorFeatureValueIntentParser.Parse(input); break;
                    case "InflFeatsPayload": MoInflAffMsaInflFeatsPayload.Parse(input, MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats); break;
                    case "AlternateFormsPayload": _ = LexEntryAlternateFormsCreatePayload.Parse(input); break;
                    default: throw new InvalidOperationException("Unknown vector shape.");
                }
            }
            if (vector.GetProperty("valid").GetBoolean()) { Parse(); accepted++; }
            else { Assert.ThrowsAny<Exception>(Parse); refused++; }
        }
        Assert.True(accepted >= 4);
        Assert.True(refused >= 8);
    }

    [Fact]
    public void DecisionEvidenceRecordSchemaIsClosedAndItsVectorsAgreeWithTheParser()
    {
        using var schema = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.decision-evidence-record.schema.json");
        Assert.NotNull(schema);
        using var schemaDocument = JsonDocument.Parse(schema!);
        Assert.False(schemaDocument.RootElement.GetProperty("$defs").GetProperty("DecisionEvidenceRecordPayload")
            .GetProperty("additionalProperties").GetBoolean());
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.decision-evidence-record.vectors.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("input");
            if (vector.GetProperty("valid").GetBoolean()) DecisionEvidenceRecordPayload.Parse(input);
            else Assert.Throws<SIL.Motif.Contract.Parsing.ContractParseException>(() => DecisionEvidenceRecordPayload.Parse(input));
        }
    }

    [Fact]
    public void AgentEvaluationSchemaIsClosedAndItsVectorsAgreeWithTheParser()
    {
        using var schema = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.agent-evaluation.schema.json");
        Assert.NotNull(schema);
        using var schemaDocument = JsonDocument.Parse(schema!);
        var definitions = schemaDocument.RootElement.GetProperty("$defs");
        Assert.False(definitions.GetProperty("AgentEvaluationPayload").GetProperty("additionalProperties").GetBoolean());
        Assert.False(definitions.GetProperty("Agent").GetProperty("additionalProperties").GetBoolean());
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.agent-evaluation.vectors.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var accepted = 0;
        var refused = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("input");
            if (vector.GetProperty("valid").GetBoolean())
            {
                _ = AgentEvaluationPayload.Parse(input);
                accepted++;
            }
            else
            {
                Assert.ThrowsAny<Exception>(() => AgentEvaluationPayload.Parse(input));
                refused++;
            }
        }
        Assert.Equal(2, accepted);
        Assert.Equal(12, refused);
    }

    [Fact]
    public void WordformCreateSchemaIsClosedAndItsVectorsAgreeWithTheParser()
    {
        using var schema = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.wordform-create.schema.json");
        Assert.NotNull(schema);
        using var schemaDocument = JsonDocument.Parse(schema!);
        Assert.False(schemaDocument.RootElement.GetProperty("$defs").GetProperty("WordformCreatePayload")
            .GetProperty("additionalProperties").GetBoolean());
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.wordform-create.vectors.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var accepted = 0;
        var refused = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("input");
            if (vector.GetProperty("valid").GetBoolean())
            {
                _ = LangProjectWordformsCreatePayload.Parse(input);
                accepted++;
            }
            else
            {
                Assert.ThrowsAny<Exception>(() => LangProjectWordformsCreatePayload.Parse(input));
                refused++;
            }
        }
        Assert.Equal(2, accepted);
        Assert.Equal(10, refused);
    }

    [Fact]
    public void TextCreateSchemaIsClosedAndItsVectorsAgreeWithTheParser()
    {
        using var schema = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.text-create.schema.json");
        Assert.NotNull(schema);
        using var schemaDocument = JsonDocument.Parse(schema!);
        foreach (var definition in schemaDocument.RootElement.GetProperty("$defs").EnumerateObject())
            if (definition.Value.TryGetProperty("type", out var type) && type.GetString() == "object")
                Assert.False(definition.Value.GetProperty("additionalProperties").GetBoolean());
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.text-create.vectors.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var accepted = 0;
        var refused = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("input");
            if (vector.GetProperty("valid").GetBoolean())
            {
                _ = LangProjectTextsCreatePayload.Parse(input);
                accepted++;
            }
            else
            {
                Assert.ThrowsAny<Exception>(() => LangProjectTextsCreatePayload.Parse(input));
                refused++;
            }
        }
        Assert.Equal(2, accepted);
        Assert.Equal(11, refused);
    }

    [Theory]
    [InlineData("inflectionClasses")]
    [InlineData("environments")]
    public void ParserRefusesRepeatedDecodedIdentities(string field)
    {
        using var document = JsonDocument.Parse($$$"""
            {"entry":"AAAAAAAAAAAAAAAAAAAAAQ","form":"a","formWs":"en","morphType":"AAAAAAAAAAAAAAAAAAAAAg",
             "{{{field}}}":["AAAAAAAAAAAAAAAAAAAAAw","form:AAAAAAAAAAAAAAAAAAAAAx"]}
            """);
        Assert.ThrowsAny<Exception>(() => AuthorAlternateFormIntentParser.Parse(document.RootElement));
    }

    [Theory]
    [InlineData("LexEntry", "AlternateForms", "MoForm", "Seq", "feeding", "lexical/lexEntry/createAlternateForms")]
    [InlineData("MoInflAffMsa", "InflFeats", "FsFeatStruc", "Atomic", "unordered", "grammar/moInflAffMsa/createInflFeats")]
    public void OwningFamiliesMatchTheGeneratedModelClassification(string owner, string field, string signature,
        string cardinality, string comparison, string kind)
    {
        var row = SIL.Motif.Generator.MotifModelLoader.Load().Rows.Single(row => row.DeclaringClass == owner && row.FieldName == field);
        Assert.Equal("in", row.Manifest.Scope);
        Assert.Equal(SIL.Motif.Generator.Model.FieldKind.Owning, row.Kind);
        Assert.Equal(signature, row.Sig);
        Assert.Equal(cardinality, row.Card.ToString());
        Assert.Equal(comparison, row.Manifest.ComparisonClass);
        Assert.Contains(kind, SIL.Motif.Generator.Derivation.KindNameDerivation.DeriveAll(
            SIL.Motif.Generator.Derivation.GroupDerivation.Derive(owner),
            SIL.Motif.Generator.Derivation.ConstructDerivation.Derive(owner), row.Manifest.Verbs, field));
    }

    [Fact]
    public void AlternatePayloadRefusesDuplicateProperties()
    {
        using var document = JsonDocument.Parse("""
            {"morphType":"AAAAAAAAAAAAAAAAAAAAAQ","ws":"sw","text":"m","text":"mw"}
            """);
        Assert.ThrowsAny<Exception>(() => LexEntryAlternateFormsCreatePayload.Parse(document.RootElement));
    }

    [Theory]
    [InlineData("{\"msa\":\"AAAAAAAAAAAAAAAAAAAAAQ\",\"msa\":\"AAAAAAAAAAAAAAAAAAAAAg\"}")]
    [InlineData("{\"msa\":\"AAAAAAAAAAAAAAAAAAAAAQ\",\"unknown\":false}")]
    public void FeatureStructureInputRefusesDuplicateAndUnknownProperties(string input)
    {
        using var document = JsonDocument.Parse(input);
        Assert.ThrowsAny<Exception>(() => AuthorFeatureStructureIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void InflectionalOwningCreateHasARegisteredHandlerAndAtomicSlot()
    {
        const string kind = MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats;
        Assert.NotNull(OperationHandlerRegistry.Resolve(kind, "contract coverage"));
        var owner = CanonicalId.Mint();
        var entity = CanonicalId.Mint();
        var operation = new SIL.Motif.Contract.Model.OperationEnvelope(CanonicalId.Mint(), kind,
            target: owner, entityId: entity, after: JsonSerializer.SerializeToElement(new { }));
        var slots = OperationSlotRegistry.Resolve(operation);
        Assert.Contains(slots, slot => slot.Target == owner &&
            slot.Field == SIL.Motif.Model.Snapshot.SnapshotFields.MoInflAffMsaInflFeats && slot.Discriminator is null);
        Assert.Contains(slots, slot => slot.Target == entity && slot.Field == "$identity");
    }

}
