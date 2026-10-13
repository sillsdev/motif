using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

/// <summary>
/// A citation is one typed entry of an operation's <c>provenance</c>: closed in shape, at most one per operation,
/// and outside the intent digest, so citing a source never changes what a Proposal does.
/// </summary>
public class OperationCitationTests
{
    private static string ProposalWithProvenance(string provenance) => $$"""
        {
          "contractVersions": { "lexical": "1.0" },
          "proposalId": "{{TestIds.Proposal1}}",
          "operations": [
            { "operationId": "{{TestIds.Op1}}", "kind": "lexical/entry/create", "entityId": "{{TestIds.Entity1}}",
              "after": { "headword": "run" }, "provenance": {{provenance}} }
          ]
        }
        """;

    [Fact]
    public void Create_TrimsBothPartsAndTreatsABlankLocatorAsAbsent()
    {
        var citation = OperationCitation.Create("  Steere 1870  ", "   ");

        Assert.Equal("Steere 1870", citation.Source);
        Assert.Null(citation.Locator);
        Assert.Equal("Steere 1870", citation.ToString());
        Assert.Equal("Steere 1870, p. 142", OperationCitation.Create("Steere 1870", " p. 142 ").ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RefusesABlankSource(string? source)
    {
        Assert.Throws<ContractParseException>(() => OperationCitation.Create(source, "p. 1"));
    }

    [Fact]
    public void Create_RefusesOverlongParts()
    {
        Assert.Throws<ContractParseException>(() =>
            OperationCitation.Create(new string('s', OperationCitation.MaxSourceLength + 1)));
        Assert.Throws<ContractParseException>(() =>
            OperationCitation.Create("Steere 1870", new string('l', OperationCitation.MaxLocatorLength + 1)));
        Assert.Equal(OperationCitation.MaxSourceLength,
            OperationCitation.Create(new string('s', OperationCitation.MaxSourceLength)).Source.Length);
    }

    [Theory]
    [InlineData("""{ "source": "Steere 1870", "author": "someone" }""")]
    [InlineData("""{ "source": "Steere 1870", "type": "citation" }""")]
    [InlineData("""{ "source": 1870 }""")]
    [InlineData("""{ "locator": "p. 142" }""")]
    [InlineData("""["Steere 1870"]""")]
    public void Parse_RefusesAnythingButASourceAndALocator(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<ContractParseException>(() => OperationCitation.Parse(document.RootElement));
    }

    [Fact]
    public void ProposalParser_ReadsTheCitationAndKeepsOtherProvenanceEntries()
    {
        var proposal = ProposalJsonParser.Parse(ProposalWithProvenance("""
            [ { "source": "corpus-42" }, { "type": "citation", "source": "Steere 1870", "locator": "p. 142" } ]
            """));

        var operation = proposal.Operations.Single();
        Assert.Equal(2, operation.Provenance.Count);
        Assert.Equal(new CitationView("Steere 1870", "p. 142"), CitationView.Of(operation));
    }

    [Theory]
    [InlineData("""[ { "type": "citation", "source": "A" }, { "type": "citation", "source": "B" } ]""")]
    [InlineData("""[ { "type": "citation", "source": "" } ]""")]
    [InlineData("""[ { "type": "citation", "source": "A", "agent": "x" } ]""")]
    public void ProposalParser_RefusesAMalformedOrRepeatedCitation(string provenance)
    {
        Assert.Throws<ContractParseException>(() => ProposalJsonParser.Parse(ProposalWithProvenance(provenance)));
    }

    [Fact]
    public void Citation_DoesNotAffectTheIntentDigest()
    {
        var uncited = IntentDigest.Compute(ProposalJsonParser.Parse(ProposalWithProvenance("[]")));
        var cited = IntentDigest.Compute(ProposalJsonParser.Parse(ProposalWithProvenance(
            """[ { "type": "citation", "source": "Steere 1870", "locator": "p. 142" } ]""")));
        var citedElsewhere = IntentDigest.Compute(ProposalJsonParser.Parse(ProposalWithProvenance(
            """[ { "type": "citation", "source": "Ashton 1944", "locator": "§ 12" } ]""")));

        Assert.Equal(uncited, cited);
        Assert.Equal(uncited, citedElsewhere);
    }

    [Fact]
    public void Replace_SwapsOrRemovesOnlyTheCitationEntry()
    {
        using var other = JsonDocument.Parse("""{ "source": "corpus-42" }""");
        var first = OperationCitation.Replace([other.RootElement], OperationCitation.Create("A", "p. 1"));
        var second = OperationCitation.Replace(first, OperationCitation.Create("B"));
        var removed = OperationCitation.Replace(second, null);

        Assert.Equal("A", OperationCitation.FromProvenance(first)!.Source);
        Assert.Equal(2, second.Count);
        Assert.Equal("B", OperationCitation.FromProvenance(second)!.Source);
        Assert.Null(OperationCitation.FromProvenance(second)!.Locator);
        Assert.Equal("corpus-42", Assert.Single(removed).GetProperty("source").GetString());
        Assert.Null(OperationCitation.FromProvenance(removed));
    }
}
