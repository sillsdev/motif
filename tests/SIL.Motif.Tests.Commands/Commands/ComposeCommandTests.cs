using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ComposeCommandTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string CreateRuleKind = "grammar/phPhonData/createPhonRules";

    private static ComposeStepRequest Step(string verb, object intent, string? label = null) =>
        new(verb, JsonSerializer.SerializeToElement(intent), label, null);

    [Fact]
    public void OneStepLeavesTheDraftExactlyAsComposeIntentDoes()
    {
        var setup = PrepareFeatures();
        var structures = new List<string>();
        foreach (var draft in new[] { "by-intent", "by-compose" })
        {
            Assert.True(ProposalCommands.New(new(setup.Path, Version, draft, "one step")).Succeeded);
            var structure = ProposalCommands.ComposeAuthorFeatureStructure(new(setup.Path, Version, draft,
                JsonSerializer.Serialize(new { msa = setup.StemMsa })));
            Assert.True(structure.Succeeded, structure.Refusal?.Message);
            structures.Add(structure.Value!.CreatedObjects.Single().Id);
        }
        var intent = new { featStruc = structures[0], feature = setup.Feature };
        var single = ProposalCommands.ComposeIntent(new(setup.Path, Version, "by-intent", "compose-author-feature-value",
            JsonSerializer.Serialize(intent)));
        var composed = ProposalCommands.Compose(new(setup.Path, Version, "by-compose",
            [Step("compose-author-feature-value", new { featStruc = structures[1], feature = setup.Feature })],
            null));

        Assert.True(single.Succeeded, single.Refusal?.Message);
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var only = Assert.Single(composed.Value!.Intents);
        Assert.Equal("compose-author-feature-value", only.Verb);
        Assert.Equal(single.Value!.ComposerName, only.ComposerName);
        Assert.Equal(single.Value.Operations.Select(operation => operation.Kind),
            only.Operations.Select(operation => operation.Kind));
        Assert.Equal(single.Value.OperationCount, composed.Value.OperationCount);
        Assert.Equal(Normalized(setup.Path, "by-intent"), Normalized(setup.Path, "by-compose"));
    }

    [Fact]
    public void ALaterStepNamesAnEarlierRuleByItsLabel()
    {
        var (path, phoneme) = StartDraft("labelled");
        var rule = (string name, object? placement) => new Dictionary<string, object?>
        {
            ["name"] = name,
            ["direction"] = "simultaneous",
            ["input"] = new[] { new { phoneme } },
            ["output"] = new[] { new { phoneme } },
            ["left"] = new[] { new { phoneme } },
            ["right"] = new[] { new { phoneme } },
            ["placement"] = placement,
        }.Where(pair => pair.Value is not null).ToDictionary();

        var composed = ProposalCommands.Compose(new(path, Version, "labelled",
        [
            Step("compose-author-phonological-rule", rule("first", null), "r1"),
            Step("compose-author-phonological-rule", rule("second", new { after = "@r1" }), "r2"),
        ], null));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var creates = composed.Value!.Intents.Select(intent =>
            Assert.Single(intent.Operations, operation => operation.Kind == CreateRuleKind)).ToArray();
        var stored = ReadDraft(path, "labelled").Operations.Where(operation => operation.Kind == CreateRuleKind).ToArray();
        Assert.Contains(creates[0].OperationId, stored[1].DependsOn);
    }

    [Fact]
    public void RemovalAndNewStepsHappenTogetherOrNotAtAll()
    {
        var (path, _) = StartDraft("atomic");
        var extra = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "atomic",
            JsonSerializer.Serialize(new { name = "z", representations = new[] { "z" } })));
        Assert.True(extra.Succeeded, extra.Refusal?.Message);
        var removable = extra.Value!.Operations.Select(operation => operation.OperationId).ToArray();
        var before = ReadDraft(path, "atomic").Operations.Select(operation => operation.OperationId).ToArray();
        var (revisionBefore, jsonBefore) = StoredDraft(path, "atomic");

        var refused = ProposalCommands.Compose(new(path, Version, "atomic",
        [
            Step("compose-author-phoneme", new { name = "y", representations = new[] { "y" } }),
            Step("compose-author-phoneme", new { name = "a", representations = new[] { "a" } }),
        ], removable));

        Assert.False(refused.Succeeded);
        Assert.Contains("Intent 1 (compose-author-phoneme)", refused.Refusal!.Message);
        Assert.Equal(before, ReadDraft(path, "atomic").Operations.Select(operation => operation.OperationId));
        var (revisionAfter, jsonAfter) = StoredDraft(path, "atomic");
        Assert.Equal(revisionBefore, revisionAfter);
        Assert.Equal(jsonBefore, jsonAfter);

        var done = ProposalCommands.Compose(new(path, Version, "atomic",
            [Step("compose-author-phoneme", new { name = "y", representations = new[] { "y" } })], removable));

        Assert.True(done.Succeeded, done.Refusal?.Message);
        var after = ReadDraft(path, "atomic").Operations.Select(operation => operation.OperationId).ToArray();
        Assert.Empty(after.Intersect(removable));
        Assert.Equal(before.Except(removable).Count() + done.Value!.Intents.Single().Operations.Count, after.Length);
        Assert.Equal(after.Length, done.Value.OperationCount);
    }

    [Fact]
    public void ASecondEditOfOneObjectInOneRequestIsRefusedWhateverPrefixSpellsItsId()
    {
        var setup = PrepareSlot();
        foreach (var second in new[] { setup.Slot, CanonicalId.FromGuid(setup.SlotGuid, "other-").Value })
        {
            var draft = "twice-" + second.Length;
            Assert.True(ProposalCommands.New(new(setup.Path, Version, draft, "twice")).Succeeded);
            var (revision, json) = StoredDraft(setup.Path, draft);

            var refused = ProposalCommands.Compose(new(setup.Path, Version, draft,
                [Step("compose-edit-affix-slot", SlotEdit(setup.Slot)), Step("compose-edit-affix-slot", SlotEdit(second))],
                null));

            Assert.False(refused.Succeeded);
            Assert.Contains("already edits the same target", refused.Refusal!.Message);
            Assert.Equal((revision, json), StoredDraft(setup.Path, draft));
        }
    }

    [Fact]
    public void ALaterRequestReplacesTheEarlierEditOfTheSameObjectWhateverPrefixSpellsItsId()
    {
        var setup = PrepareSlot();
        Assert.True(ProposalCommands.New(new(setup.Path, Version, "replace", "replace")).Succeeded);
        var first = ProposalCommands.Compose(new(setup.Path, Version, "replace",
            [Step("compose-edit-affix-slot", SlotEdit(setup.Slot))], null));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var firstIds = first.Value!.Intents.Single().Operations.Select(operation => operation.OperationId).ToArray();

        var second = ProposalCommands.Compose(new(setup.Path, Version, "replace",
            [Step("compose-edit-affix-slot", SlotEdit(CanonicalId.FromGuid(setup.SlotGuid, "other-").Value))], null));

        Assert.True(second.Succeeded, second.Refusal?.Message);
        var document = ReadDraft(setup.Path, "replace");
        Assert.Equal(1, second.Value!.OperationCount);
        Assert.Equal(second.Value.Intents.Single().Operations.Select(operation => operation.OperationId),
            document.Operations.Select(operation => operation.OperationId));
        Assert.Empty(document.Operations.Select(operation => operation.OperationId).Intersect(firstIds));
        Assert.Single(document.ComposerProvenance);
    }

    [Fact]
    public void RemovingAnOperationAndReAddingItsSlotInOneCallIsNotASlotConflict()
    {
        var setup = PrepareSlot();
        Assert.True(ProposalCommands.New(new(setup.Path, Version, "readd", "readd")).Succeeded);
        var first = ProposalCommands.Compose(new(setup.Path, Version, "readd",
            [Step("compose-edit-affix-slot", SlotEdit(setup.Slot))], null));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var removed = first.Value!.Intents.Single().Operations.Select(operation => operation.OperationId).ToArray();

        var again = ProposalCommands.Compose(new(setup.Path, Version, "readd",
            [Step("compose-edit-affix-slot", SlotEdit(setup.Slot))], removed));

        Assert.True(again.Succeeded, again.Refusal?.Message);
        var stored = ReadDraft(setup.Path, "readd").Operations.Select(operation => operation.OperationId).ToArray();
        Assert.Empty(stored.Intersect(removed));
        Assert.Equal(again.Value!.Intents.Single().Operations.Select(operation => operation.OperationId), stored);
    }

    [Fact]
    public void RemovedOperationsLeaveNoComposerProvenanceBehind()
    {
        var (path, _) = StartDraft("provenance");
        var extra = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "provenance",
            JsonSerializer.Serialize(new { name = "z", representations = new[] { "z" } })));
        Assert.True(extra.Succeeded, extra.Refusal?.Message);
        var removable = extra.Value!.Operations.Select(operation => operation.OperationId).ToArray();
        Assert.Equal(2, ReadDraft(path, "provenance").ComposerProvenance.Count);

        var done = ProposalCommands.Compose(new(path, Version, "provenance",
            [Step("compose-author-phoneme", new { name = "y", representations = new[] { "y" } })], removable));

        Assert.True(done.Succeeded, done.Refusal?.Message);
        var provenance = ReadDraft(path, "provenance").ComposerProvenance;
        Assert.Equal(2, provenance.Count);
        Assert.DoesNotContain(provenance, entry => removable.Any(id =>
            entry.GetProperty("operationIds").EnumerateArray().Any(listed => listed.GetString() == id)));
    }

    private sealed record NotebookFixture(string Path, string RecordType, string FirstEntry, string SecondEntry);

    private NotebookFixture PrepareNotebook()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
        {
            var notebook = scratch.LangProject.ResearchNotebookOA;
            var recordTypes = notebook.RecTypesOA ?? scratch.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = recordTypes;
            var recordType = scratch.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            recordTypes.PossibilitiesOS.Add(recordType);
            recordType.Name.set_String(scratch.DefaultAnalWs,
                SIL.LCModel.Core.Text.TsStringUtils.MakeString("Motif human judgment", scratch.DefaultAnalWs));
        });
        new FwDataProjectLoader().Save(scratch);
        scratch.Dispose();
        var workerRoot = Path.Combine(Path.GetTempPath(), "motif-compose-disposition-" + Guid.NewGuid().ToString("N"));
        var initialized = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(path, ProjectInitializationCommand.ConfirmationPhrase), workerRoot);
        Assert.True(initialized.Succeeded, initialized.Refusal?.Message);
        var recordType = Assert.Single(ParsimonyCommands.ListNotebookRecordTypes(
            new ListNotebookRecordTypesRequest(path, Version)).Value!.RecordTypes,
            item => item.Name == "Motif human judgment");
        return new(path, recordType.Id, CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value,
            CanonicalId.FromGuid(pristine.Seed.SecondEntryId).Value);
    }

    private static object Disposition(NotebookFixture setup, string subjectEntry, string disposition, string reason) => new
    {
        recordTypeId = setup.RecordType,
        measureId = "R-nc-excess",
        subject = new { kind = "object", @object = new { @class = "LexEntry", id = subjectEntry } },
        disposition,
        evidenceDigest = "sha256:" + new string('a', 64),
        evidenceContract = "natural-class-excess/v1",
        subjectCaption = "an entry",
        measureCaption = "Class contains extra segments",
        reason,
    };

    private static string[] DispositionRecords(string path, string draft) => ReadDraft(path, draft).ComposerProvenance
        .Where(entry => entry.GetProperty("composer").GetString() == "RecordParsimonyDisposition")
        .Select(entry => entry.GetProperty("input").GetProperty("reason").GetString()!).ToArray();

    [Fact]
    public void ADispositionStepRecordsItsDispositionInTheDraft()
    {
        var setup = PrepareNotebook();
        Assert.True(ProposalCommands.New(new(setup.Path, Version, "disposition", "disposition")).Succeeded);

        var composed = ProposalCommands.Compose(new(setup.Path, Version, "disposition",
            [Step("compose-record-parsimony-disposition", Disposition(setup, setup.FirstEntry, "keep", "first"))], null));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var only = Assert.Single(composed.Value!.Intents);
        Assert.Equal("RecordParsimonyDisposition", only.ComposerName);
        Assert.NotEmpty(only.Operations);
        Assert.Equal(["first"], DispositionRecords(setup.Path, "disposition"));
    }

    [Fact]
    public void TwoDispositionsOfOneFindingInOneRequestAreRefusedAndTwoFindingsAreNot()
    {
        var setup = PrepareNotebook();
        Assert.True(ProposalCommands.New(new(setup.Path, Version, "twice-disposition", "twice")).Succeeded);
        var (revision, json) = StoredDraft(setup.Path, "twice-disposition");

        var refused = ProposalCommands.Compose(new(setup.Path, Version, "twice-disposition",
        [
            Step("compose-record-parsimony-disposition", Disposition(setup, setup.FirstEntry, "keep", "first")),
            Step("compose-record-parsimony-disposition", Disposition(setup, setup.FirstEntry, "fix", "second")),
        ], null));

        Assert.False(refused.Succeeded);
        Assert.Contains("already records a disposition of the same finding", refused.Refusal!.Message);
        Assert.Equal("1", refused.Refusal.Facts["intentIndex"]);
        Assert.Equal((revision, json), StoredDraft(setup.Path, "twice-disposition"));

        var both = ProposalCommands.Compose(new(setup.Path, Version, "twice-disposition",
        [
            Step("compose-record-parsimony-disposition", Disposition(setup, setup.FirstEntry, "keep", "first")),
            Step("compose-record-parsimony-disposition", Disposition(setup, setup.SecondEntry, "fix", "second")),
        ], null));
        Assert.True(both.Succeeded, both.Refusal?.Message);
        Assert.Equal(["first", "second"], DispositionRecords(setup.Path, "twice-disposition"));
    }

    [Fact]
    public void ALaterRequestReplacesTheEarlierDispositionOfTheSameFindingAndLeavesOthers()
    {
        var setup = PrepareNotebook();
        Assert.True(ProposalCommands.New(new(setup.Path, Version, "later-disposition", "later")).Succeeded);
        var first = ProposalCommands.Compose(new(setup.Path, Version, "later-disposition",
        [
            Step("compose-record-parsimony-disposition", Disposition(setup, setup.FirstEntry, "keep", "first")),
            Step("compose-record-parsimony-disposition", Disposition(setup, setup.SecondEntry, "keep", "other")),
        ], null));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var firstIds = first.Value!.Intents[0].Operations.Select(operation => operation.OperationId).ToArray();
        var otherIds = first.Value.Intents[1].Operations.Select(operation => operation.OperationId).ToArray();

        var second = ProposalCommands.Compose(new(setup.Path, Version, "later-disposition",
            [Step("compose-record-parsimony-disposition", Disposition(setup, setup.FirstEntry, "fix", "replaced"))], null));

        Assert.True(second.Succeeded, second.Refusal?.Message);
        var stored = ReadDraft(setup.Path, "later-disposition").Operations.Select(operation => operation.OperationId).ToArray();
        Assert.Empty(stored.Intersect(firstIds));
        Assert.Empty(otherIds.Except(stored));
        Assert.Equal(otherIds.Length + second.Value!.Intents.Single().Operations.Count, stored.Length);
        Assert.Equal(["other", "replaced"], DispositionRecords(setup.Path, "later-disposition"));
    }

    private static object SlotEdit(string target) => new { target, expectedOptional = false, optional = true };

    private sealed record SlotFixture(string Path, string Slot, Guid SlotGuid);

    private SlotFixture PrepareSlot()
    {
        using var cache = pristine.NewScratch();
        var category = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
        IMoInflAffixSlot slot = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, "slot");
        });
        new FwDataProjectLoader().Save(cache);
        return new(cache.ProjectId.Path, CanonicalId.FromGuid(slot.Guid).Value, slot.Guid);
    }

    private static (string Revision, string Json) StoredDraft(string path, string name)
    {
        using var database = ProjectMotifDatabase.Open(path);
        var json = new ProposalRepository(database).GetDraft(name).ProposalJson!;
        return (DraftRevision.Compute(json), json);
    }

    [Fact]
    public void AnUnknownRemovedOperationRefusesTheWholeCall()
    {
        var (path, _) = StartDraft("bad-remove");
        var before = ReadDraft(path, "bad-remove").Operations.Select(operation => operation.OperationId).ToArray();

        var refused = ProposalCommands.Compose(new(path, Version, "bad-remove",
            [Step("compose-author-phoneme", new { name = "y", representations = new[] { "y" } })],
            [CanonicalId.Mint().Value]));

        Assert.Equal("operation.invalid-id", refused.Refusal?.Code);
        Assert.Equal(before, ReadDraft(path, "bad-remove").Operations.Select(operation => operation.OperationId));
    }

    [Theory]
    [InlineData("not-a-verb", "not a verb a batch can run")]
    public void AnUnknownVerbIsRefusedWithItsIndex(string verb, string expected)
    {
        var (path, _) = StartDraft("unknown-verb");

        var refused = ProposalCommands.Compose(new(path, Version, "unknown-verb", [Step(verb, new { })], null));

        Assert.Equal("draft.invalid", refused.Refusal?.Code);
        Assert.Contains(expected, refused.Refusal!.Message);
        Assert.Equal("0", refused.Refusal.Facts["intentIndex"]);
    }

    [Fact]
    public void ACallNeedsAtLeastOneStep()
    {
        var (path, _) = StartDraft("no-steps");

        var refused = ProposalCommands.Compose(new(path, Version, "no-steps", [], null));

        Assert.Equal("draft.invalid", refused.Refusal?.Code);
    }

    [Fact]
    public void StepsParseFromJsonWithLabelsAndCitationsAndRefuseUnknownProperties()
    {
        var steps = ComposeStepRequest.ParseList(
            """[{"verb":"compose-author-phoneme","intent":{"name":"a"},"as":"x","cite":{"source":"Grammar","locator":"3"}}]""");

        var step = Assert.Single(steps);
        Assert.Equal("compose-author-phoneme", step.Verb);
        Assert.Equal("x", step.As);
        Assert.Equal("Grammar", step.Cite!.Source);
        Assert.Throws<InvalidOperationException>(() => ComposeStepRequest.ParseList("""[{"verb":"v","intent":{},"extra":1}]"""));
        Assert.Throws<InvalidOperationException>(() => ComposeStepRequest.ParseList("""{"verb":"v"}"""));
        Assert.Throws<InvalidOperationException>(() => ComposeStepRequest.ParseList("""[{"intent":{}}]"""));
    }

    private (string Path, string Phoneme) StartDraft(string draft)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Compose")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, draft,
            JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        return (path, phoneme.Value!.Operations.Single(operation =>
            operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!);
    }

    private sealed record Fixture(string Path, string StemMsa, string Feature);

    private Fixture PrepareFeatures()
    {
        using var cache = pristine.NewScratch();
        var anal = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var featureOps = AuthorInflectionFeatureComposer.Build(cache, new("synthetic-nc", anal, [new("seven")]));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var operation in featureOps)
                OperationHandlerRegistry.Resolve(operation.Kind, "compose command fixture")
                    .ApplyAndCaptureEffect(cache, operation, []);
        });
        new FwDataProjectLoader().Save(cache);
        return new(cache.ProjectId.Path,
            CanonicalId.FromGuid(cache.ServiceLocator.GetInstance<ILexSenseRepository>()
                .GetObject(pristine.Seed.FirstSenseId).MorphoSyntaxAnalysisRA.Guid).Value,
            featureOps.Single(operation => operation.Kind == FsFeatureSystemFeaturesOperationKinds.Create).EntityId!.Value.Value);
    }

    private static DraftDocument ReadDraft(string path, string name)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(name).ProposalJson!);
    }

    // Minted identities differ between Drafts, so each is replaced by its first-appearance order.
    private static string Normalized(string path, string draft)
    {
        var document = ReadDraft(path, draft);
        var text = JsonSerializer.Serialize(new { document.Operations, document.ComposerProvenance });
        var tokens = document.Operations
            .SelectMany(operation => new[] { operation.OperationId, operation.EntityId, operation.Target })
            .OfType<string>().Distinct().Select((id, index) => (id, token: $"#{index}"));
        foreach (var (id, token) in tokens)
            text = text.Replace(id, token, StringComparison.Ordinal);
        return text;
    }
}
