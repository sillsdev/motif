using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract.HandAnalysis;

public sealed class HandAnalysisIndexTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly string AssessmentId = CanonicalId.FromGuid(ProjectId, "assessment/").Value;
    private static readonly SnapshotStamp First = new("first", ProjectId, "home", "source", "parser",
        "settings", "ICU 70", 1, SnapshotVerification.Full);
    private static readonly WordQuery.Alternatives Alternatives =
        new(new WordIdentity.CaseForm(new("fr", "e\u0301lan")), WordOrder.Encounter);

    [Fact]
    public async Task CursorBindingRefusesChangedQueriesAndOtherSnapshots()
    {
        var index = new InMemoryHandAnalysisIndex();
        index.Publish(First, Alternatives.Wordform, Reading("c"), Reading("a"), Reading("b"));
        await using var old = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        var first = (await old.WordsAsync(Alternatives, new(1), CancellationToken.None)).Value!;
        var token = Assert.IsType<string>(first.NextCursor);
        Assert.Equal("a", Assert.IsType<WordFact.Reading>(Assert.Single(first.Items)).EncounterKey);
        Assert.True(first.Complete);
        Assert.Equal(3, first.Total);

        var nextPage = new PageRequest(2, token);
        var changedOrder = await old.WordsAsync(Alternatives with { Order = WordOrder.DefaultRank }, nextPage, CancellationToken.None);
        var changedWord = await old.WordsAsync(Alternatives with
        {
            Wordform = new WordIdentity.CaseForm(new("fr", "E\u0301lan"))
        }, nextPage, CancellationToken.None);
        var unknownToken = await old.WordsAsync(Alternatives, new(2, "unknown"), CancellationToken.None);

        var second = First with { SnapshotId = "second", ComparisonVersion = "ICU 71", Verification = SnapshotVerification.Certified };
        index.Publish(second, Alternatives.Wordform, Reading("a", "9"), Reading("new", "1"));
        await using var latest = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        Assert.Equal(second, latest.Stamp);
        var otherSnapshot = await latest.WordsAsync(Alternatives, nextPage, CancellationToken.None);

        foreach (var result in new[] { changedOrder, changedWord, unknownToken, otherSnapshot })
        {
            Assert.False(result.Succeeded);
            Assert.Null(result.Value);
            Assert.Equal(RefusalCodes.PagingInvalid, result.Refusal?.Code);
        }

        var newest = (await latest.WordsAsync(Alternatives, new(), CancellationToken.None)).Value!;
        Assert.Equal(second, newest.Stamp);
        Assert.Equal(new[] { "a", "new" }, newest.Items.Cast<WordFact.Reading>().Select(r => r.EncounterKey));
        var oldContinuation = (await old.WordsAsync(Alternatives, nextPage, CancellationToken.None)).Value!;
        Assert.Equal(new[] { "b", "c" }, oldContinuation.Items.Cast<WordFact.Reading>().Select(r => r.EncounterKey));

        await using var exact = (await index.PinAsync(ProjectId, First.SnapshotId, CancellationToken.None)).Value!;
        Assert.Equal(First, exact.Stamp);
        var continuation = (await exact.WordsAsync(Alternatives, nextPage, CancellationToken.None)).Value!;
        Assert.Equal(First, continuation.Stamp);
        Assert.Equal(3, continuation.Total);
        Assert.Null(continuation.NextCursor);
        Assert.Equal(new[] { "b", "c" }, continuation.Items.Cast<WordFact.Reading>().Select(r => r.EncounterKey));
    }

    [Fact]
    public async Task RankedCursorTraversesTiedRowsUsingTheirFullOrder()
    {
        var index = new InMemoryHandAnalysisIndex();
        index.Publish(First, Alternatives.Wordform,
            Reading("same", "1", "b"), Reading("same", "2", "d"),
            Reading("same", "1", "a"), Reading("last", "1", "c"));
        await using var pin = (await index.PinAsync(ProjectId, First.SnapshotId, CancellationToken.None)).Value!;
        var query = Alternatives with { Order = WordOrder.DefaultRank };
        var first = (await pin.WordsAsync(query, new(2), CancellationToken.None)).Value!;
        var next = (await pin.WordsAsync(query, new(1, first.NextCursor), CancellationToken.None)).Value!;
        var last = (await pin.WordsAsync(query, new(1, next.NextCursor), CancellationToken.None)).Value!;
        Assert.Equal(new[] { "c", "a", "b", "d" }, first.Items.Concat(next.Items).Concat(last.Items)
            .Cast<WordFact.Reading>().Select(r => Assert.IsType<AnalysisRef.Parser>(r.Analysis).ReadingKey));
        Assert.Null(last.NextCursor);
    }

    [Fact]
    public async Task PinRefusalsCarryCodesWithoutSnapshots()
    {
        var index = new InMemoryHandAnalysisIndex();
        var unavailable = await index.PinAsync(ProjectId, null, CancellationToken.None);
        index.Publish(First, Alternatives.Wordform);
        var expired = await index.PinAsync(ProjectId, "another-process", CancellationToken.None);

        Assert.False(unavailable.Succeeded);
        Assert.Equal(RefusalCodes.HandAnalysisIndexUnavailable, unavailable.Refusal?.Code);
        Assert.Null(unavailable.Value);
        Assert.False(expired.Succeeded);
        Assert.Equal(RefusalCodes.HandAnalysisSnapshotExpired, expired.Refusal?.Code);
        Assert.Null(expired.Value);
    }

    [Fact]
    public async Task S1PinServesWordAndReferencePagesWithAnIndependentProjectLimitation()
    {
        var index = new InMemoryHandAnalysisIndex();
        index.Publish(First, Alternatives.Wordform, Reading("native-source"));
        await using var pin = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        var project = Assert.IsType<ProjectRead.Unavailable>(pin.Project);
        Assert.Equal(First, project.Stamp);
        Assert.Equal(RefusalCodes.HandAnalysisIndexUnavailable, project.Limitation.Code);

        var words = (await pin.WordsAsync(Alternatives, new(), CancellationToken.None)).Value!;
        Assert.Equal(First, words.Stamp);
        Assert.Single(words.Items);
        var references = (await pin.ReferencesAsync(new ReferenceQuery.Uses([], false), new(), CancellationToken.None)).Value!;
        Assert.Equal(First, references.Stamp);
        Assert.Empty(references.Rows.Items);
    }

    [Fact]
    public async Task AssessmentAddressIsPreservedInLookupAndOnTheWire()
    {
        var index = new InMemoryHandAnalysisIndex();
        index.Publish(First, Alternatives.Wordform, Reading("observation"));
        await using var pin = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        var reference = new AnalysisRef.Parser(AssessmentId, "observation");
        var result = (await pin.WordsAsync(new WordQuery.Reading(new ReadingKey.Analysis(reference)), new(),
            CancellationToken.None)).Value!;
        Assert.Equal(reference, Assert.IsType<WordFact.Reading>(Assert.Single(result.Items)).Analysis);
        var options = MotifJson.CreateHandAnalysisReadOptions();
        var json = JsonSerializer.Serialize<AnalysisRef>(reference, options);
        Assert.Contains("assessment/", json);
        Assert.Equal(reference, JsonSerializer.Deserialize<AnalysisRef>(json, options));
    }

    [Fact]
    public async Task HistoricalSelectorsUseTheExactBasisAndRefuseIfItIsGoneOrAmbiguous()
    {
        var index = new InMemoryHandAnalysisIndex();
        var historical = First with { HomePoint = "past-home" };
        var current = First with { SnapshotId = "current", HomePoint = "new-home" };
        var competing = historical with { SnapshotId = "other-source", SourceRevision = "another-source" };
        var digest = "sha256:" + new string('1', 64);
        var baseline = new BaselineToken(ProjectId.ToString(), digest, "projection", "2026-01-01T00:00:00Z", digest);
        index.Publish(historical, Alternatives.Wordform, Reading("past"));
        await using var original = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        index.Publish(current, Alternatives.Wordform, Reading("current"));

        var publications = new[] { (historical, baseline.SemanticIdentity), (current, (BaselineSemanticIdentity?)null) };
        await using var byRevision = (await index.PinAtAsync(ProjectId, historical.HomePoint, null, publications)).Value!;
        await using var byBaseline = (await index.PinAtAsync(ProjectId, null, baseline, publications)).Value!;
        Assert.Equal(historical, byRevision.Stamp);
        Assert.Equal(historical, byBaseline.Stamp);
        var page = (await byRevision.WordsAsync(Alternatives, new(), CancellationToken.None)).Value!;
        Assert.Equal("past", Assert.IsType<WordFact.Reading>(Assert.Single(page.Items)).EncounterKey);

        var ambiguous = await index.PinAtAsync(ProjectId, historical.HomePoint, null,
            [.. publications, (competing, baseline.SemanticIdentity)]);
        Assert.Equal(RefusalCodes.HandAnalysisIndexUnavailable, ambiguous.Refusal?.Code);
        Assert.Equal(HandAnalysisRefusalFacts.AmbiguousBasis, ambiguous.Refusal?.Facts[HandAnalysisRefusalFacts.Cause]);
        var missing = await index.PinAtAsync(ProjectId, "missing-home", null, publications);
        Assert.Equal(RefusalCodes.HandAnalysisSnapshotExpired, missing.Refusal?.Code);
        var fresh = new InMemoryHandAnalysisIndex();
        fresh.Publish(current, Alternatives.Wordform, Reading("current"));
        var expired = await fresh.PinAtAsync(ProjectId, null, baseline, publications);
        Assert.Equal(RefusalCodes.HandAnalysisSnapshotExpired, expired.Refusal?.Code);
        Assert.Equal(historical.SnapshotId, expired.Refusal?.Facts[HandAnalysisRefusalFacts.SnapshotId]);
    }

    [Fact]
    public async Task LimitedDeleteClosureRepeatsEvidenceAndBindsTheDeletionKind()
    {
        var index = new InMemoryHandAnalysisIndex();
        var target = new ReferenceResultTarget(ProjectId, "LexEntry");
        var source = new ReferenceResultTarget(ProjectId, "WfiMorphBundle");
        var rows = new Page<ReferenceFact>(First,
            [new(First, target, source, new("Morph", 0, "member#1", null, null), ProjectId, ProjectId, null, [], ReferenceState.Current),
             new(First, target, source, new("Morph", 1, "member#2", null, null), ProjectId, ProjectId, null, [], ReferenceState.Unknown)],
            null, null, false, [new(RefusalCodes.HandAnalysisIncompleteReferences, [ProjectId], "Coverage is incomplete.")]);
        var counts = new ReferenceCounts(1, 1, null, [], "diagnostic", false, null);
        var closure = new ReferenceQuery.DeleteClosure(new(DeletionKind.Entry, ProjectId));
        var uses = new ReferenceQuery.Uses([new(ReferenceKind.Entry, ProjectId)], false);
        var usesPage = new ReferencePage(rows with { Items = [rows.Items[1]] }, counts with { ClosureDigest = null });
        index.Publish(First, Alternatives.Wordform, [], [target], (closure, new ReferencePage(rows, counts)), (uses, usesPage));
        await using var pin = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        var page = (await pin.ReferencesAsync(closure, new(1), CancellationToken.None)).Value!;
        Assert.False(page.Rows.Complete);
        Assert.Null(page.Rows.Total);
        Assert.Null(page.Counts.PlanToken);
        var cursor = Assert.IsType<string>(page.Rows.NextCursor);
        var next = (await pin.ReferencesAsync(closure, new(2, cursor), CancellationToken.None)).Value!;
        Assert.Equal(First, next.Stamp);
        Assert.Equal(counts, next.Counts);
        Assert.Equal(page.Rows.Limitations, next.Rows.Limitations);
        Assert.Equal("member#2", Assert.Single(next.Rows.Items).Location!.MemberId);
        Assert.Null(next.Rows.NextCursor);
        var wrongKind = new ReferenceQuery.DeleteClosure(new(DeletionKind.Sense, ProjectId));
        var wrongFirstPage = await pin.ReferencesAsync(wrongKind, new(1), CancellationToken.None);
        Assert.Equal(RefusalCodes.HandAnalysisTypeMismatch, wrongFirstPage.Refusal?.Code);
        Assert.Null(wrongFirstPage.Value);
        var wrongContinuation = await pin.ReferencesAsync(wrongKind, new(2, cursor), CancellationToken.None);
        Assert.Equal(RefusalCodes.PagingInvalid, wrongContinuation.Refusal?.Code);
        var otherTarget = new ReferenceQuery.DeleteClosure(new(DeletionKind.Entry, Guid.NewGuid()));
        var missing = await pin.ReferencesAsync(otherTarget, new(1), CancellationToken.None);
        Assert.Equal(RefusalCodes.HandAnalysisIncompleteReferences, missing.Refusal?.Code);
        Assert.Null(missing.Value);

        var directUses = (await pin.ReferencesAsync(new ReferenceQuery.Uses([new(ReferenceKind.Entry, ProjectId)], false),
            new(1), CancellationToken.None)).Value!;
        Assert.Equal("member#2", Assert.Single(directUses.Rows.Items).Location!.MemberId);
        Assert.Null(directUses.Counts.ClosureDigest);
        var descendants = (await pin.ReferencesAsync(uses with { IncludeDescendants = true }, new(1),
            CancellationToken.None)).Value!;
        Assert.Empty(descendants.Rows.Items);
    }

    [Fact]
    public async Task LivePinsSurviveEvictionOfAnExternalBasis()
    {
        var index = new InMemoryHandAnalysisIndex();
        index.Publish(First, Alternatives.Wordform, Reading("a"));
        var original = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        for (var i = 0; i < 4; i++)
            index.Publish(First with { SnapshotId = $"basis-{i}", SourceRevision = $"source-{i}" }, Alternatives.Wordform);
        var page = (await original.WordsAsync(Alternatives, new(), CancellationToken.None)).Value!;
        Assert.Equal(First, page.Stamp);
        Assert.Equal("a", Assert.IsType<WordFact.Reading>(Assert.Single(page.Items)).EncounterKey);
        var exact = (await index.PinAsync(ProjectId, First.SnapshotId, CancellationToken.None)).Value!;
        Assert.Equal(First, exact.Stamp);
        await exact.DisposeAsync();
        await original.DisposeAsync();
        var expired = await index.PinAsync(ProjectId, First.SnapshotId, CancellationToken.None);
        Assert.Equal(RefusalCodes.HandAnalysisSnapshotExpired, expired.Refusal?.Code);
    }

    [Fact]
    public async Task DisposedPinRejectsFurtherReads()
    {
        var index = new InMemoryHandAnalysisIndex();
        index.Publish(First, Alternatives.Wordform, Reading("a"));
        var pin = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        await pin.DisposeAsync();
        await pin.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await pin.WordsAsync(Alternatives, new(), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task OutOfRangePageLimitsRefuse(int limit)
    {
        var index = new InMemoryHandAnalysisIndex();
        index.Publish(First, Alternatives.Wordform);
        await using var snapshot = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        var result = await snapshot.WordsAsync(Alternatives, new(limit), CancellationToken.None);
        Assert.Null(result.Value);
        Assert.Equal(RefusalCodes.PagingInvalid, result.Refusal?.Code);
    }

    [Fact]
    public async Task CancelledReadProducesNoPage()
    {
        var index = new InMemoryHandAnalysisIndex();
        index.Publish(First, Alternatives.Wordform, Reading("a"));
        await using var snapshot = (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await snapshot.WordsAsync(Alternatives, new(), new CancellationToken(canceled: true)));
    }

    [Fact]
    public void StaleReadSetRefusalPreservesDriftedIds()
    {
        var result = CommandOutcome<Page<WordFact>>.Refused(new(RefusalCodes.HandAnalysisStaleReadSet,
            FailureReason.Refused, "The read set has changed.", new Dictionary<string, string> { ["driftedIds"] = ProjectId.ToString() }));
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        var restored = JsonSerializer.Deserialize<Refusal>(JsonSerializer.Serialize(result.Refusal))!;
        Assert.Equal(RefusalCodes.HandAnalysisStaleReadSet, restored.Code);
        Assert.Equal(ProjectId.ToString(), restored.Facts["driftedIds"]);
    }

    private static WordFact.Reading Reading(string key, string? rank = null, string? readingKey = null) =>
        new(new AnalysisRef.Parser(AssessmentId, readingKey ?? key), null, null, null, [], [], [], null, null, key, 0, rank ?? key,
            null, [new(RefusalCodes.HandAnalysisIndexUnavailable, [], "Identity is unavailable.")]);
}
