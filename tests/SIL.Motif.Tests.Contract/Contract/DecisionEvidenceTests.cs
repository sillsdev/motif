using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class DecisionEvidenceTests
{
    private const string Typo = """
        {"type":"decisionEvidence","tier":"misspelled-typo",
         "sources":[{"kind":"dictionary","source":"Kamusi 2014","locator":"sita"}],
         "detail":{"intendedForm":"sita","editType":"vowel substitution","corpusCount":0},"stagedBy":"stager 1"}
        """;

    private static JsonElement Entry(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static DecisionEvidence? Read(string json) => DecisionEvidence.FromProvenance([Entry(json)]);

    [Fact]
    public void ReadsAWellFormedEntryAndWritesItBackCanonically()
    {
        var evidence = Read(Typo)!;

        Assert.Equal("misspelled-typo", evidence.Tier);
        Assert.Equal("sita", evidence.Detail["intendedForm"]);
        Assert.Equal("0", evidence.Detail["corpusCount"]);
        Assert.Equal("stager 1", evidence.StagedBy);
        var again = DecisionEvidence.FromProvenance([evidence.ToProvenanceEntry()])!;
        Assert.Equal(evidence.Tier, again.Tier);
        Assert.Equal(evidence.Detail, again.Detail);
        Assert.Equal(evidence.Sources, again.Sources);
    }

    [Fact]
    public void OtherProvenanceEntriesAreIgnoredAndNoEntryIsNull() =>
        Assert.Null(DecisionEvidence.FromProvenance([Entry("""{"type":"citation","source":"S"}"""), Entry("""{"note":1}""")]));

    [Theory]
    [InlineData("""{"type":"decisionEvidence","tier":"gold","sources":[]}""")]
    [InlineData("""{"type":"decisionEvidence","tier":"undecided","sources":[],"author":"x"}""")]
    [InlineData("""{"type":"decisionEvidence","tier":"undecided","sources":[{"kind":"web","source":"S"}]}""")]
    [InlineData("""{"type":"decisionEvidence","tier":"undecided","sources":[{"kind":"grammar","source":"S","page":3}]}""")]
    [InlineData("""{"type":"decisionEvidence","tier":"undecided","sources":[{"kind":"grammar","source":" "}]}""")]
    [InlineData("""{"type":"decisionEvidence","tier":"undecided","sources":null}""")]
    [InlineData("""{"type":"decisionEvidence","tier":"undecided","sources":[],"detail":{"gloss":"x"}}""")]
    [InlineData("""{"type":"decisionEvidence","tier":"undecided","sources":[],"detail":{"corpusCount":-1}}""")]
    [InlineData("""{"type":"decisionEvidence","tier":"undecided","sources":[],"detail":{"corpusCount":"3"}}""")]
    [InlineData("""{"type":"decisionEvidence","tier":null,"sources":[]}""")]
    [InlineData("""{"type":"decisionEvidence","tier":"undecided","tier":"ill-formed","sources":[]}""")]
    public void RefusesAnUnclosedOrMalformedEntry(string json) =>
        Assert.Throws<ContractParseException>(() => Read(json));

    [Theory]
    [InlineData("misspelled-typo", """{"intendedForm":"sita"}""")]
    [InlineData("misspelled-typo", """{"editType":"vowel"}""")]
    [InlineData("ill-formed", """{}""")]
    [InlineData("nonstandard-variant", """{"intendedForm":"x"}""")]
    public void RefusesATierMissingWhatItRequires(string tier, string detail) =>
        Assert.Throws<ContractParseException>(() => Read(
            $$"""{"type":"decisionEvidence","tier":"{{tier}}","sources":[],"detail":{{detail}}}"""));

    [Fact]
    public void SourceApprovedNeedsADictionaryAGrammarAndACorpusSource()
    {
        const string two = """
            {"type":"decisionEvidence","tier":"source-approved","sources":[
              {"kind":"dictionary","source":"D"},{"kind":"grammar","source":"G"}]}
            """;
        Assert.Throws<ContractParseException>(() => Read(two));
        var ok = Read(two.Replace("""{"kind":"grammar","source":"G"}""",
            """{"kind":"grammar","source":"G"},{"kind":"corpus","source":"C","locator":"12"}"""));
        Assert.Equal(3, ok!.Sources.Count);
    }

    [Fact]
    public void AnOperationCarriesAtMostOne() =>
        Assert.Throws<ContractParseException>(() => DecisionEvidence.FromProvenance(
            [Entry("""{"type":"decisionEvidence","tier":"undecided","sources":[]}"""),
             Entry("""{"type":"decisionEvidence","tier":"undecided","sources":[]}""")]));

    [Fact]
    public void RefusesTooManySourcesAndLongText()
    {
        var many = string.Join(",", Enumerable.Repeat("""{"kind":"example","source":"E"}""", DecisionEvidence.MaxSources + 1));
        Assert.Throws<ContractParseException>(() => Read(
            $$"""{"type":"decisionEvidence","tier":"undecided","sources":[{{many}}]}"""));
        Assert.Throws<ContractParseException>(() => Read(
            $$"""{"type":"decisionEvidence","tier":"undecided","sources":[],"stagedBy":"{{new string('x', 121)}}"}"""));
    }

    [Fact]
    public void AProposalWithAMalformedEntryIsRefusedWhenParsed()
    {
        var proposal = """
            {"contractVersions":{"sequence":"1.0"},"proposalId":"AAAAAAAAAAAAAAAAAAAAAA","requires":[],
             "operations":[{"operationId":"BBBBBBBBBBBBBBBBBBBBBB","kind":"sequence/move",
             "target":"CCCCCCCCCCCCCCCCCCCCCC","placement":{"after":"DDDDDDDDDDDDDDDDDDDDDD"},
             "provenance":[{"type":"decisionEvidence","tier":"gold","sources":[]}]}]}
            """;
        Assert.Contains("tier", Assert.Throws<ContractParseException>(() => ProposalJsonParser.Parse(proposal)).Message);
    }

    [Fact]
    public void EvidenceDoesNotChangeTheIntentDigest()
    {
        const string template = """
            {"contractVersions":{"sequence":"1.0"},"proposalId":"AAAAAAAAAAAAAAAAAAAAAA","requires":[],
             "operations":[{"operationId":"BBBBBBBBBBBBBBBBBBBBBB","kind":"sequence/move",
             "target":"CCCCCCCCCCCCCCCCCCCCCC","placement":{"after":"DDDDDDDDDDDDDDDDDDDDDD"},"provenance":[PROV]}]}
            """;
        var without = ProposalJsonParser.Parse(template.Replace("PROV", ""));
        var with = ProposalJsonParser.Parse(template.Replace("PROV", Typo));

        Assert.Equal(
            SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(without),
            SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(with));
    }
}
