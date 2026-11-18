using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using Xunit;

namespace SIL.Motif.Tests.Contract.HandAnalysis;

public sealed class HandAnalysisReadJsonTests
{
    private static readonly JsonSerializerOptions Json = MotifJson.CreateHandAnalysisReadOptions();
    private static readonly Guid Id = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly string AssessmentId = CanonicalId.FromGuid(Id, "assessment/").Value;
    private static readonly SnapshotStamp Stamp = new("snapshot", Id, "home", "source", "parser", "settings", "ICU 70", 1,
        SnapshotVerification.Full);
    private static readonly WordformKey Key = new("fr", "e\u0301lan");
    private static readonly HandAnalysisAnchor Anchor = new HandAnalysisTextAnchor(Id, Id, Id, 2, 7);
    private static readonly ReferenceTarget Target = new(ReferenceKind.Allomorph, Id);
    private static readonly ImmutableDictionary<string, string> Text = ImmutableDictionary<string, string>.Empty.Add("fr", Key.NfdCaseForm);
    private static readonly OccurrenceScope Scope = new OccurrenceScope.ChapterOrText(
        new HandAnalysisChapterPopulation(Id, "English", "MAT", 1));

    [Fact]
    public void AllQueryBranchesRoundTripAsClosedUnions()
    {
        RoundTrip<OccurrenceQuery>(new OccurrenceQuery.Exact(new(Anchor,
            new(Id, "home", [], "context", "segmentation"), new("fr", Key.NfdCaseForm, Key.NfdCaseForm))));
        RoundTrip<OccurrenceQuery>(new OccurrenceQuery.Range(new HandAnalysisBareTextPopulation(Id), Anchor, null,
            OccurrenceDirection.Backward, [new("gloss", "en")]));
        RoundTrip<OccurrenceQuery>(new OccurrenceQuery.Members(Scope, [Key], [OccurrenceState.Bare]));
        RoundTrip<OccurrenceQuery>(new OccurrenceQuery.Uses([Target], new OccurrenceScope.Corpus(new(Id))));
        RoundTrip<WordQuery>(new WordQuery.Identity(new WordIdentity.Native(Id)));
        RoundTrip<WordQuery>(new WordQuery.Identity(new WordIdentity.CaseForm(Key)));
        RoundTrip<WordQuery>(new WordQuery.Reading(new ReadingKey.Analysis(new AnalysisRef.Native(Id))));
        RoundTrip<WordQuery>(new WordQuery.Reading(new ReadingKey.Analysis(new AnalysisRef.Parser(AssessmentId, "result/1"))));
        RoundTrip<WordQuery>(new WordQuery.Reading(new ReadingKey.NativeGloss(Id)));
        RoundTrip<WordQuery>(new WordQuery.Alternatives(new WordIdentity.Native(Id), WordOrder.DefaultRank));
        RoundTrip(new FormQuery("fr", Key.NfdCaseForm, [new(Id, new("", "-"))], FormMatch.Pattern, FormOrder.Chooser, [Id]));
        RoundTrip<LexiconQuery>(new LexiconQuery.Search(LexicalField.Sense, LexicalMatch.Prefix, "fr", Key.NfdCaseForm));
        RoundTrip<LexiconQuery>(new LexiconQuery.ByIds([Id], [], [], []));
        RoundTrip<ReferenceQuery>(new ReferenceQuery.Uses([Target], true, Scope));
        RoundTrip<ReferenceQuery>(new ReferenceQuery.DeleteClosure(new(DeletionKind.Allomorph, Id)));
        Assert.Equal(new PageRequest(), JsonSerializer.Deserialize<PageRequest>("{}", Json));
        Assert.Equal(50, new PageRequest().Limit);
        Assert.Equal(200, PageRequest.MaxLimit);
        RoundTrip(new CursorBinding("snapshot", "query", "last-key"));
    }

    [Theory]
    [InlineData(SnapshotVerification.Full, "Full")]
    [InlineData(SnapshotVerification.Certified, "Certified")]
    public void StampPreservesVerificationAndThePinnedBasis(SnapshotVerification verification, string wireValue)
    {
        var stamp = new SnapshotStamp("snapshot", Id, "home", "source", "assessment", "settings", "ICU 70", 1, verification);
        Assert.Equal(stamp, RoundTrip(stamp));
        Assert.Equal(wireValue, JsonSerializer.SerializeToNode(stamp, Json)!["verification"]!.GetValue<string>());
    }

    [Fact]
    public void ReadFactsRoundTripWithoutNativeObjects()
    {
        var project = new ProjectFacts(Stamp, new(LineMode.GlossOnly, [new("gloss", "en", true, 0)], true),
            [new(Id, "stem", MorphFamily.Stem, new("", ""), 0)],
            [new("fr", Id, 0, null, false, null, null, "<ldml />", "rules", "ICU 70")],
            [new(Id, null, 0, Text, Text)], []);
        RoundTrip(project);

        var parser = new AnalysisRef.Parser(AssessmentId, "result/1");
        var guess = new GuessFact(parser, null, GuessOrigin.Grammar, "assessment");
        var reading = new WordFact.Reading(parser, null, Id, null, [new(null, "member#1", null, Id, Id, null, Text, Text)],
            [new(Id, Text)], [new(Id, Id, false, true)], "producer", "result", "encounter", 7, "rank", null,
            [new(RefusalCodes.HandAnalysisIndexUnavailable, [Id], "Identity is unavailable.")]);
        var header = new WordFact.Header(Key, [Id], [new(Id, Text)], [new(new("fr", "E\u0301lan"), [Id])], guess, null, new(3, 2, true));
        var words = RoundTrip(new Page<WordFact>(Stamp, [header, reading], 2, null, true, []));
        Assert.Equal(parser, Assert.IsType<WordFact.Reading>(words.Items[1]).Analysis);

        var occurrence = new OccurrenceFact(Stamp, Anchor, new(Id, "home", ["head"], "context", "segmentation"),
            new("fr", Key.NfdCaseForm, Key.NfdCaseForm, Id), true, 2, true, false, OccurrenceState.Bare,
            new(AttachmentKind.Wordform, Id), Id, null, null, [new("head", parser, null)], [new("settle", ["head"], "head")],
            guess, true, false, new(Id, "FreeTranslation", "en"), ImmutableDictionary<Guid, string>.Empty.Add(Id, "fingerprint"),
            [new("word", "fr")], []);
        var occurrences = RoundTrip(new OccurrencePage(new(Stamp, [occurrence], 1, null, true, []),
            new(1, 0, 0, 0, 0, 0, 0, true)));
        Assert.Equal(Key.NfdCaseForm, Assert.Single(occurrences.Rows.Items).Wordform!.CaseForm);

        var form = new FormHit(Id, "MoAffixAllomorph", Id, false, 1, Text, Id, false,
            new("discriminator", "pattern"), 3, "encounter", "display", "sort", Text);
        RoundTrip(new Page<FormHit>(Stamp, [form], 1, null, true, []));
        var lexical = new LexicalFact(Id, Text, Text, 0, Id, [Id], [], [new(Id, "MoStemMsa", Id, null, null, "key")],
            [new(Id, Id, "variant", 0, [new(EntryId: Id)], [Id])], [new(Id, Text, Text)], [form]);
        var lexicon = RoundTrip(new Page<LexicalFact>(Stamp, [lexical], 1, null, true, []));
        Assert.Empty(Assert.Single(lexicon.Items).Senses);
    }

    [Fact]
    public void IncompleteClosureCarriesUnknownTotalsAndConflictingEvidence()
    {
        var target = new ReferenceResultTarget(Id, "MoStemAllomorph");
        var fact = new ReferenceFact(Stamp, target, new(Id, "WfiMorphBundle"), new("Morph", null, null, null, null),
            Id, null, Id, [new("anchor", "anchor-id", "source", target, null, null, Anchor)], ReferenceState.Conflicting);
        var limitation = new ReadLimitation(RefusalCodes.HandAnalysisIncompleteReferences, [Id], "An edge is unresolved.");
        var page = new ReferencePage(new(Stamp, [fact], null, null, false, [limitation]),
            new(null, null, null, [new(target, null, null, null, null)], null, false, null));

        var restored = RoundTrip(page);
        Assert.Null(restored.Rows.Total);
        Assert.Null(restored.Counts.Bundles);
        Assert.False(restored.Counts.CountComplete);
        Assert.Equal(ReferenceState.Conflicting, Assert.Single(restored.Rows.Items).State);
        Assert.Equal(limitation.Code, Assert.Single(restored.Rows.Limitations).Code);
        var omitNulls = new JsonSerializerOptions(Json) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var rows = JsonSerializer.SerializeToNode(page, omitNulls)!["rows"]!.AsObject();
        Assert.True(rows.ContainsKey("total"));
        Assert.Null(rows["total"]);
    }

    [Theory]
    [InlineData("""{"kind":"unknown"}""")]
    [InlineData("""{"kind":"alternatives","wordform":{"kind":"case-form","form":{"ws":"fr","nfdCaseForm":"a"}},"order":"Encounter"}""")]
    [InlineData("""{"kind":"alternatives","wordform":{"kind":"case-form","form":{"ws":"fr","nfdCaseForm":"a"}},"order":"alphabetical"}""")]
    [InlineData("""{"kind":"alternatives","wordform":{"kind":"case-form","form":{"ws":"fr","nfdCaseForm":"a"}},"order":99}""")]
    [InlineData("""{"kind":"alternatives","wordform":{"kind":"case-form","form":{"ws":"fr","nfdCaseForm":"a"}},"order":"99"}""")]
    [InlineData("""{"kind":"alternatives","wordform":{"kind":"case-form","form":{"ws":"fr","nfdCaseForm":"a"}}}""")]
    [InlineData("""{"kind":"identity","key":{"kind":"case-form","form":{"ws":"fr","nfdCaseForm":"a"}},"order":"encounter"}""")]
    [InlineData("""{"kind":"identity","key":{"kind":"case-form","form":{"ws":"fr","nfdCaseForm":"a","other":true}}}""")]
    [InlineData("""{"kind":"identity","key":{"kind":"case-form","form":{"ws":"fr","ws":"en","nfdCaseForm":"a"}}}""")]
    public void ClosedQuerySchemaRejectsUnknownBranchesFieldsAndValues(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WordQuery>(json, Json));
    }

    [Fact]
    public void ClosedFactSchemaRejectsUnknownNestedFields()
    {
        var node = JsonSerializer.SerializeToNode(new Page<WordFact>(Stamp,
            [new WordFact.Reading(new AnalysisRef.Parser(AssessmentId, "result/1"), null, null, null,
                [], [], [], null, null, "encounter", null, null, null,
                [new(RefusalCodes.HandAnalysisIndexUnavailable, [], "Identity is unavailable.")])], 1, null, true, []), Json)!;
        node["items"]![0]!["analysis"]!["id"] = Id.ToString();
        Assert.Throws<JsonException>(() => node.Deserialize<Page<WordFact>>(Json));
    }

    private static T RoundTrip<T>(T value) where T : class
    {
        var original = JsonSerializer.SerializeToNode(value, Json);
        var restored = original.Deserialize<T>(Json);
        Assert.IsType(value.GetType(), restored);
        Assert.True(JsonNode.DeepEquals(original, JsonSerializer.SerializeToNode(restored, Json)));
        return restored!;
    }
}
