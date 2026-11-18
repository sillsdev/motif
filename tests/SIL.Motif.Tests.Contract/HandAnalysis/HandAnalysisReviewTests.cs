using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Contract.HandAnalysis;

public sealed class HandAnalysisReviewTests
{
    private static readonly JsonSerializerOptions Json = MotifJson.CreateHandAnalysisReadOptions();
    private static readonly Guid Id = Guid.Parse("6e5b91f4-4643-4816-8e32-e56f853df103");
    private static readonly HandAnalysisAnchor Anchor = new HandAnalysisTextAnchor(Id, Id, Id, 0, 0);
    private static readonly SnapshotStamp Stamp = new("point", Id, "home", "source", "parser", "settings", "ICU 70", 1,
        SnapshotVerification.Full);

    [Fact]
    public void ReadingIdentitySurvivesDisplayChangesAndAnUnavailableIdentityCarriesALimitation()
    {
        var signature = new ReadingSignature(ReadingSignatureFixture.NativeFacts(false));
        var reading = new WordFact.Reading(new AnalysisRef.Native(Id), Id, null, null, [], [], [], null, null, "order",
            null, null, signature, []);
        var spellingEdit = reading with
        {
            Glosses = [new(Id, ImmutableDictionary<string, string>.Empty.Add("fr", "nouveau"))]
        };
        var detached = Assert.IsType<WordFact.Reading>(RoundTrip<WordFact>(spellingEdit));
        Assert.Equal(signature, detached.Signature);
        Assert.Equal(ReadingSenseStatus.Ambiguous, detached.Signature!.SenseStatus);
        var unresolved = Assert.IsType<WordFact.Reading>(RoundTrip<WordFact>(reading with
        {
            Signature = null, Limitations = [new(RefusalCodes.HandAnalysisIndexUnavailable, [Id], "Identity is unavailable.")]
        }));
        Assert.Null(unresolved.Signature);
        Assert.Single(unresolved.Limitations);
    }

    [Fact]
    public void CaseLinksKeepTheirComparisonKeySeparateFromExactSelection()
    {
        var otherId = Guid.Parse("1a7d2837-2d1f-4ed0-8cbd-a25d727a7bf5");
        var comparison = new WordformKey("fr", "mot");
        var query = RoundTrip<WordQuery>(new WordQuery.Identity(new WordIdentity.Native(Id), comparison));
        Assert.Equal(comparison, Assert.IsType<WordQuery.Identity>(query).CasePair);
        var forms = ImmutableDictionary<string, string>.Empty.Add("fr", "Mot").Add("en", "Word");
        var header = RoundTrip<WordFact>(new WordFact.Header(null, [Id], [new(Id, forms)],
            [new(comparison, [otherId])], null, null, new(null, null, false)));
        var result = Assert.IsType<WordFact.Header>(header);
        Assert.Null(result.Key);
        Assert.Equal(Id, Assert.Single(result.ExactMatches));
        Assert.Equal(forms.OrderBy(pair => pair.Key), Assert.Single(result.Wordforms).Forms.OrderBy(pair => pair.Key));
        var other = Assert.Single(result.OtherCases);
        Assert.Equal(comparison, other.Key);
        Assert.Equal(otherId, Assert.Single(other.WordformIds));
    }

    [Theory]
    [InlineData(SnapshotVerification.Full)]
    [InlineData(SnapshotVerification.Certified)]
    public void PageAndDetachedRowsKeepTheIssuingStamp(SnapshotVerification mode)
    {
        var stamp = Stamp with { Verification = mode };
        var limitation = new ReadLimitation(RefusalCodes.HandAnalysisSourceUnavailable, [Id], "Context is unavailable.");
        var occurrences = RoundTrip(new OccurrencePage(new(stamp,
            [new(stamp, Anchor, null, null, null, 0, null, null, OccurrenceState.Unavailable,
                null, null, null, null, [], [], null, false, null, null, ImmutableDictionary<Guid, string>.Empty, [], [limitation])],
            1, null, false, [limitation]), new(null, null, null, null, null, 1, null, false)));
        Assert.Equal(stamp, occurrences.Stamp);
        var occurrence = RoundTrip(Assert.Single(occurrences.Rows.Items));
        Assert.Equal(stamp, occurrence.Stamp);
        Assert.Null(occurrence.Binding);
        Assert.Null(occurrence.Wordform);
        Assert.Single(occurrence.Limitations);
        Assert.Equal(stamp, RoundTrip(new Page<WordFact>(stamp, [], 0, null, true, [])).Stamp);
        Assert.Equal(stamp, RoundTrip(new Page<FormHit>(stamp, [], null, null, false, [limitation])).Stamp);
        Assert.Equal(stamp, RoundTrip(new Page<LexicalFact>(stamp, [], 0, null, true, [])).Stamp);
    }

    [Theory]
    [InlineData(SnapshotVerification.Full)]
    [InlineData(SnapshotVerification.Certified)]
    public void ReferenceWitnessesPreserveNativeLocationsAndOpaqueEvidence(SnapshotVerification mode)
    {
        var stamp = Stamp with { Verification = mode };
        var target = new ReferenceResultTarget(Id, "MoDerivAffMsa");
        var source = new ReferenceResultTarget(Id, "WfiAnalysis");
        var witness = new ReferenceWitness("conflicting-head", "head/1", "evidence/3", target, source, "MAT 5:3#2", null);
        var rows = new ReferenceFact[]
        {
            new(stamp, target, source, new("Morphs", 0, "id#1", null, null), Id, Id, Id, [witness], ReferenceState.Conflicting),
            new(stamp, target, source, new("Morphs", 1, "id#2", null, null), Id, Id, Id, [witness], ReferenceState.Conflicting),
            new(stamp, target, source, new("Comment", null, null, "fr", 2), null, null, null, [], ReferenceState.Current),
            new(stamp, target, null, null, null, null, null, [witness with { Kind = "parser" }], ReferenceState.Unknown)
        };
        var limitation = new ReadLimitation(RefusalCodes.HandAnalysisIncompleteReferences, [Id], "Coverage is incomplete.");
        var page = RoundTrip(new ReferencePage(new(stamp, [.. rows], null, "next", false, [limitation]),
            new(1, 1, null, [new(target, null, null, null, null)], "diagnostic", false, null)));
        Assert.Equal(stamp, page.Stamp);
        Assert.All(page.Rows.Items, row => Assert.Equal(stamp, RoundTrip(row).Stamp));
        Assert.Equal(new int?[] { 0, 1 }, page.Rows.Items.Take(2).Select(row => row.Location!.Position));
        Assert.Equal("id#2", page.Rows.Items[1].Location!.MemberId);
        Assert.Equal("fr", page.Rows.Items[2].Location!.Ws);
        Assert.Equal(2, page.Rows.Items[2].Location!.Run);
        Assert.Equal("MoDerivAffMsa", page.Rows.Items[0].Target.NativeClass);
        Assert.Equal(witness, Assert.Single(page.Rows.Items[0].OccurrenceWitnesses));
        Assert.Null(page.Rows.Items[3].Location);
        Assert.Null(page.Counts.PlanToken);

        var omitNulls = new JsonSerializerOptions(Json) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var counts = JsonSerializer.SerializeToNode(page.Counts, omitNulls)!.AsObject();
        Assert.True(counts.ContainsKey("occurrences"));
        Assert.Null(counts["occurrences"]);
        Assert.True(counts["usage"]![0]!.AsObject().ContainsKey("glosses"));
        Assert.Null(counts["usage"]![0]!["glosses"]);
    }

    [Fact]
    public void WireRequiresAPageStampAndRetainsExplicitUnknownCounts()
    {
        const string missingStamp = """
            {"items":[],"total":0,"nextCursor":null,"complete":true,"limitations":[]}
            """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Page<WordFact>>(missingStamp, Json));
        var omitNulls = new JsonSerializerOptions(Json) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var counts = JsonSerializer.SerializeToNode(new OccurrenceCounts(null, null, null, null, null, null, null, false),
            omitNulls)!.AsObject();
        Assert.True(counts.ContainsKey("analysis"));
        Assert.Null(counts["analysis"]);
    }

    [Fact]
    public void OccurrenceBindingCopiesTheAnnotationHeads()
    {
        var heads = new List<string> { "before" };
        var binding = OccurrenceReadBinding.FromExpected(new(Id, "home", heads, "context", "segmentation"));
        heads[0] = "after";
        Assert.Equal("before", Assert.Single(RoundTrip(binding).OccurrenceHeads));
    }

    [Fact]
    public void FormAlternativesPairTypesAndMarkers()
    {
        var unmarked = new FormTypeMarker(Id, new("", ""));
        var query = new FormQuery("fr", "mot", [unmarked], FormMatch.Equal, FormOrder.Encounter);
        Assert.Equal(unmarked, Assert.Single(RoundTrip(query).Alternatives));
        Assert.Null(query.EntryIds);
        Assert.Empty(RoundTrip(query with { EntryIds = [] }).EntryIds!.Value);
    }

    [Fact]
    public void ProjectFactsKeepTheStampWithoutPretendingSettingsWereAvailable()
    {
        var limitation = new ReadLimitation(RefusalCodes.HandAnalysisIndexUnavailable, [Id], "Settings are unavailable.");
        var facts = new ProjectFacts(Stamp, null, [], [], [], [limitation]);
        var available = Assert.IsType<ProjectRead.Available>(RoundTrip<ProjectRead>(new ProjectRead.Available(facts)));
        Assert.Equal(Stamp, available.Facts.Stamp);
        Assert.Null(available.Facts.Config);
        Assert.Equal(limitation.Code, Assert.Single(available.Facts.Limitations).Code);
        var unavailable = Assert.IsType<ProjectRead.Unavailable>(
            RoundTrip<ProjectRead>(new ProjectRead.Unavailable(Stamp, limitation)));
        Assert.Equal(Stamp, unavailable.Stamp);
        Assert.Equal(limitation.Code, unavailable.Limitation.Code);
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.SerializeToNode(value, Json).Deserialize<T>(Json)!;
}
