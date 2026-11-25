using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Contract.Model;

public sealed class RecordImportProvenanceTests
{
    private static string Operation(string id) => JsonSerializer.Serialize(new
    {
        operationId = id, kind = "lexical/lexDb/createEntries", entityId = CanonicalId.Mint("x/").Value, after = new { },
    });

    private static Proposal Build(string[] operationIds, JsonNode? extensions)
    {
        var node = new JsonObject
        {
            ["contractVersions"] = operationIds.Length == 0 ? new JsonObject() : new JsonObject { ["lexical"] = "1.0" },
            ["proposalId"] = CanonicalId.Mint("proposal/").Value,
            ["operations"] = new JsonArray(operationIds.Select(id => JsonNode.Parse(Operation(id))).ToArray()),
        };
        if (extensions is not null) node["extensions"] = extensions;
        return ProposalJsonParser.Parse(node.ToJsonString());
    }

    private static JsonNode Composers(string composer, params string[] ids) => new JsonObject
    {
        ["composers"] = new JsonArray(new JsonObject
        {
            ["composer"] = composer, ["input"] = new JsonObject(),
            ["operationIds"] = new JsonArray(ids.Select(id => (JsonNode)id).ToArray()),
        }),
    };

    [Fact]
    public void AProposalWhoseEveryOperationTheImportListedIsWhollyAnImport()
    {
        string[] ids = [CanonicalId.Mint("x/").Value, CanonicalId.Mint("x/").Value];
        Assert.True(RecordImportProvenance.WritesEveryOperation(Build(ids, Composers(RecordImportProvenance.ComposerName, ids))));
    }

    [Fact]
    public void AnOperationTheImportDidNotListMakesTheProposalMixed()
    {
        string[] ids = [CanonicalId.Mint("x/").Value, CanonicalId.Mint("x/").Value];
        Assert.False(RecordImportProvenance.WritesEveryOperation(Build(ids, Composers(RecordImportProvenance.ComposerName, ids[0]))));
    }

    [Theory]
    [InlineData("AuthorLexicalEntry")]
    [InlineData("recordimport")]
    [InlineData("")]
    public void AnotherComposersEntryNeverCounts(string composer)
    {
        string[] ids = [CanonicalId.Mint("x/").Value];
        Assert.False(RecordImportProvenance.WritesEveryOperation(Build(ids, Composers(composer, ids))));
    }

    [Fact]
    public void NoProvenanceOrMalformedProvenanceOrNoOperationsIsNotAnImport()
    {
        string[] ids = [CanonicalId.Mint("x/").Value];
        Assert.False(RecordImportProvenance.WritesEveryOperation(Build(ids, null)));
        Assert.False(RecordImportProvenance.WritesEveryOperation(Build(ids, new JsonObject { ["composers"] = "RecordImport" })));
        Assert.False(RecordImportProvenance.WritesEveryOperation(Build(ids, new JsonObject
        {
            ["composers"] = new JsonArray(new JsonObject { ["composer"] = "RecordImport", ["operationIds"] = ids[0] }),
        })));
        Assert.False(RecordImportProvenance.WritesEveryOperation(Build(ids, new JsonObject { ["recordImport"] = true })));
        Assert.False(RecordImportProvenance.WritesEveryOperation(Build([], Composers(RecordImportProvenance.ComposerName))));
    }

    [Fact]
    public void AMarkOnTheOperationItselfIsNotTheImportsProvenance()
    {
        var id = CanonicalId.Mint("x/").Value;
        var operation = JsonNode.Parse(Operation(id))!.AsObject();
        operation["extensions"] = Composers(RecordImportProvenance.ComposerName, id);
        var node = new JsonObject
        {
            ["contractVersions"] = new JsonObject { ["lexical"] = "1.0" },
            ["proposalId"] = CanonicalId.Mint("proposal/").Value,
            ["operations"] = new JsonArray(operation),
        };
        Assert.False(RecordImportProvenance.WritesEveryOperation(ProposalJsonParser.Parse(node.ToJsonString())));
    }

    [Fact]
    public void FrozenInputsWithoutTheFieldReadAsNotExemptAndTheFieldIsOmittedWhenFalse()
    {
        var inputs = new FrozenReviewInputs("project", "proposal/x", "scope", "manifest", null, [], [], []);
        var plain = JsonSerializer.Serialize(inputs);
        Assert.DoesNotContain("recordimport", plain, StringComparison.OrdinalIgnoreCase);
        Assert.False(JsonSerializer.Deserialize<FrozenReviewInputs>(plain)!.RecordImport);
        var marked = JsonSerializer.Serialize(inputs with { RecordImport = true });
        Assert.True(JsonSerializer.Deserialize<FrozenReviewInputs>(marked)!.RecordImport);
    }
}
