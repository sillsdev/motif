using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProposalReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ProposalReviewTests",
        Guid.NewGuid().ToString("N"));

    public ProposalReviewTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void GrammarEntriesComeFirstInDependencyOrder()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var created = Op("lexical/lexSense/setGloss");
        var dependent = Op("lexical/lexSense/setGloss", dependsOn: created.OperationId);
        var word = Op("lexical/lexSense/setGloss", changeId: "change-1");
        var draft = Draft([dependent, created, word],
            Provenance("AuthorB", dependent), Provenance("AuthorA", created),
            WordProvenance("change-1", "cat", word));
        var id = Save(fixture, draft);

        var review = Read(fixture, id);

        Assert.Equal([created.OperationId, dependent.OperationId, word.OperationId],
            review.Entries.Select(entry => entry.OperationIds.Single()).ToArray());
        Assert.IsType<GrammarChangeEntry>(review.Entries[0]);
        Assert.IsType<WordChangeEntry>(review.Entries[2]);
        Assert.Equal([created.OperationId], review.Entries[1].DependsOnEntries);
        Assert.Equal([0, 1, 2], review.Entries.Select(entry => entry.Ordinal).ToArray());
    }

    [Fact]
    public void OneEntryPerComposerProvenance()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var first = Op("lexical/lexSense/setGloss");
        var second = Op("lexical/lexSense/setGloss");
        var third = Op("lexical/lexSense/setGloss");
        var id = Save(fixture, Draft([first, second, third], Provenance("AddPhoneme", first, second),
            Provenance("AddRule", third)));

        var review = Read(fixture, id);

        Assert.Equal(2, review.Entries.Count);
        var phoneme = Assert.IsType<GrammarChangeEntry>(review.Entries[0]);
        Assert.Equal("AddPhoneme", phoneme.Composer);
        Assert.Equal([first.OperationId, second.OperationId], phoneme.OperationIds);
        Assert.Equal("Add phoneme", phoneme.Title);
    }

    [Fact]
    public void AComposerEntryIsTitledInALinguistsWordsWithTheNameItGave()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var phoneme = Op("lexical/lexSense/setGloss");
        var name = Op("lexical/lexSense/setGloss");
        var provenance = JsonSerializer.SerializeToElement(new
        {
            composer = "AuthorPhoneme", input = new { name = "ɸ" },
            operationIds = new[] { phoneme.OperationId, name.OperationId },
        });
        var id = Save(fixture, Draft([phoneme, name], provenance));

        var entry = Assert.IsType<GrammarChangeEntry>(Assert.Single(Read(fixture, id).Entries));

        Assert.Equal("Add phoneme ɸ", entry.Title);
        Assert.Equal("2 changes", entry.Summary);
    }

    [Fact]
    public void AnUnclaimedOperationIsItsOwnEntry()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var claimed = Op("lexical/lexSense/setGloss");
        var loose = Op("lexical/lexSense/setGloss");
        var id = Save(fixture, Draft([claimed, loose], Provenance("AddPhoneme", claimed)));

        var review = Read(fixture, id);

        var entry = Assert.IsType<GrammarChangeEntry>(review.Entries[1]);
        Assert.Equal(loose.OperationId, entry.EntryKey);
        Assert.Equal("Set gloss", entry.Title);
    }

    [Fact]
    public void WordEntriesFollowGrammarEntriesInWordOrder()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var zebra = Op("lexical/lexSense/setGloss", changeId: "z");
        var apple = Op("lexical/lexSense/setGloss", changeId: "a");
        var rule = Op("lexical/lexSense/setGloss");
        var id = Save(fixture, Draft([zebra, apple, rule], WordProvenance("z", "zebra", zebra),
            WordProvenance("a", "apple", apple), Provenance("AddRule", rule)));

        var review = Read(fixture, id);

        Assert.Equal(["AddRule", "apple", "zebra"], review.Entries.Select(entry =>
            entry is WordChangeEntry word ? word.Change.Word : ((GrammarChangeEntry)entry).Composer).ToArray());
    }

    [Fact]
    public void ARetirementWithoutEvidenceIsStillOneEntry()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var one = Op("lexical/lexSense/setGloss");
        var two = Op("lexical/lexSense/setGloss");
        var id = Save(fixture, Draft([one, two], Provenance("RetireAllomorph", one, two)));

        var review = Read(fixture, id);

        var entry = Assert.IsType<GrammarChangeEntry>(Assert.Single(review.Entries));
        Assert.Empty(entry.Parts);
        Assert.Equal(2, entry.OperationIds.Count);
    }

    [Fact]
    public void NullProposalIdReadsThePendingDraft()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var operation = Op("lexical/lexSense/setGloss");
        var draft = Draft([operation], Provenance("AddPhoneme", operation));
        new ProposalRepository(fixture.Database).CreateDraft(PendingChanges.DraftName,
            CanonicalId.Parse(draft.ProposalId), JsonSerializer.Serialize(draft, DraftOptions),
            ProposalRoles.PendingChanges);

        var review = ProposalReview.Read(fixture.Database, fixture.Project, null).Value!;

        Assert.Equal(draft.ProposalId, review.ProposalId);
        Assert.Single(review.Entries);
        Assert.True(review.IsDraft);
        Assert.Empty(ProposalReview.Read(fixture.Database, fixture.Project, null).Value!.Entries.Skip(1));
    }

    [Fact]
    public void EntryCountReadsAStagedRetirementWithoutTheLiveProjectFile()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var first = Op("lexical/lexSense/setGloss");
        var second = Op("lexical/lexSense/setGloss");
        var draft = Draft([first, second], Provenance("RetireAllomorph", first, second));
        new ProposalRepository(fixture.Database).CreateDraft(PendingChanges.DraftName,
            CanonicalId.Parse(draft.ProposalId), JsonSerializer.Serialize(draft, DraftOptions),
            ProposalRoles.PendingChanges);
        File.Delete(fixture.Project.FullFwDataPath);

        var outcome = ProposalReview.ReadEntryCount(fixture.Database, fixture.Project, null);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(1, outcome.Value!.EntryCount);
    }

    [Fact]
    public void EntryCountReadsAStagedParsimonyJudgmentWithoutTheLiveProjectFile()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var judgment = new DraftOperation
        {
            OperationId = CanonicalId.Mint("operation/").Value,
            Kind = HumanJudgmentCustomFieldOperationKinds.Set,
            Target = CanonicalId.Mint("record/").Value,
            After = JsonSerializer.SerializeToElement(new { text = "judgment" }).EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal),
        };
        var draft = Draft([judgment], Provenance("RecordParsimonyDisposition", judgment));
        draft.ContractVersions.Clear();
        draft.ContractVersions["system"] = "1.0";
        new ProposalRepository(fixture.Database).CreateDraft(PendingChanges.DraftName,
            CanonicalId.Parse(draft.ProposalId), JsonSerializer.Serialize(draft, DraftOptions),
            ProposalRoles.PendingChanges);
        File.Delete(fixture.Project.FullFwDataPath);

        var outcome = ProposalReview.ReadEntryCount(fixture.Database, fixture.Project, null);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(1, outcome.Value!.EntryCount);
    }

    [Fact]
    public void WithoutAPendingDraftTheListIsEmpty()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);

        var review = ProposalReview.Read(fixture.Database, fixture.Project, null).Value!;

        Assert.Empty(review.Entries);
        Assert.Equal("", review.ProposalId);
    }

    [Fact]
    public void ContentDigestChangesWhenAnOperationChanges()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var operation = Op("lexical/lexSense/setGloss");
        var draft = Draft([operation], Provenance("AddPhoneme", operation));
        var id = Save(fixture, draft);
        var before = Read(fixture, id).Entries.Single().ContentDigest;

        using var changed = JsonDocument.Parse("{\"ws\":\"en\",\"text\":\"other\"}");
        operation.After = changed.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        new ProposalRepository(fixture.Database).SaveDraft("review", JsonSerializer.Serialize(draft, DraftOptions));
        var after = Read(fixture, id).Entries.Single().ContentDigest;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ADraftWithNoOperationsListsNothingAndIsNotAnError()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var empty = Draft([]);
        empty.ContractVersions.Clear();
        var id = Save(fixture, empty);

        var outcome = ProposalReview.Read(fixture.Database, fixture.Project, id);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Empty(outcome.Value!.Entries);
        Assert.Equal(0, outcome.Value.LookedAtCount);
    }

    [Fact]
    public void AnUnknownProposalIsRefused()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);

        var outcome = ProposalReview.Read(fixture.Database, fixture.Project, CanonicalId.Mint("proposal/").Value);

        Assert.False(outcome.Succeeded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    internal static JsonSerializerOptions DraftOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    internal static string Save(TrialsTests.ProjectFixture fixture, DraftDocument draft)
    {
        new ProposalRepository(fixture.Database).CreateDraft("review", CanonicalId.Parse(draft.ProposalId),
            JsonSerializer.Serialize(draft, DraftOptions));
        return draft.ProposalId;
    }

    private static ProposalReviewResponse Read(TrialsTests.ProjectFixture fixture, string id)
    {
        var outcome = ProposalReview.Read(fixture.Database, fixture.Project, id);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    internal static DraftOperation Op(string kind, string? dependsOn = null, string? changeId = null)
    {
        using var after = JsonDocument.Parse("{\"ws\":\"en\",\"text\":\"x\"}");
        return new DraftOperation
        {
            OperationId = CanonicalId.Mint("operation/").Value,
            Kind = kind,
            Target = CanonicalId.Mint("lexSense/").Value,
            DependsOn = dependsOn is null ? [] : [dependsOn],
            After = after.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone()),
            Extensions = changeId is null ? null
                : JsonSerializer.SerializeToElement(new { changeId }),
        };
    }

    internal static DraftDocument Draft(IReadOnlyList<DraftOperation> operations, params JsonElement[] provenance) =>
        new()
        {
            ProposalId = CanonicalId.Mint("proposal/").Value,
            ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal) { ["lexical"] = "1.0" },
            Label = "A label",
            Operations = operations.ToList(),
            ComposerProvenance = provenance.ToList(),
        };

    internal static JsonElement Provenance(string composer, params DraftOperation[] operations) =>
        JsonSerializer.SerializeToElement(new
        {
            composer, input = new { }, operationIds = operations.Select(operation => operation.OperationId).ToArray(),
        });

    internal static JsonElement WordProvenance(string changeId, string word, params DraftOperation[] operations) =>
        JsonSerializer.SerializeToElement(new
        {
            changeId, kind = "approve", word, wordformId = CanonicalId.Mint("wordform/").Value,
            operationIds = operations.Select(operation => operation.OperationId).ToArray(),
        });
}
