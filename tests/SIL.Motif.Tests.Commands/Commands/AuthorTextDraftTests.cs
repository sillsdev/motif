using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AuthorTextDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void TextCallsReportCreatedIdsAndALaterCallDependsOnAnEarlierCallsWordform()
    {
        var (path, ws, wordform, analysis) = Prepare();
        const string draft = "authored-text";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Synthetic fixture text")).Succeeded);

        var first = ProposalCommands.ComposeAuthorText(new(path, Version, draft, JsonSerializer.Serialize(new
        {
            title = "Hadithi", ws,
            paragraphs = new[] { new { segments = new[] { new[] { "sita", "kitu" } } } },
        })));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        Assert.Equal([LangProjectWordformsOperationKinds.Create, LangProjectTextsOperationKinds.Create],
            first.Value!.CreatedObjects.Select(created => created.Kind));
        var kitu = first.Value.CreatedObjects[0].Id;

        var second = ProposalCommands.ComposeAuthorText(new(path, Version, draft, JsonSerializer.Serialize(new
        {
            title = "Pili", ws,
            paragraphs = new[] { new { segments = new[] { new[] { "kitu", "sita" } } } },
        })));
        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.Equal([LangProjectTextsOperationKinds.Create], second.Value!.CreatedObjects.Select(created => created.Kind));

        var decision = ProposalCommands.ComposeEditAnalysisDecision(new(path, Version, draft,
            JsonSerializer.Serialize(new { decision = "approve", analysis })));
        Assert.True(decision.Succeeded, decision.Refusal?.Message);
        var batched = ProposalCommands.ComposeBatch(new(path, Version, draft, JsonSerializer.Serialize(new
        {
            intents = new object[]
            {
                new { verb = "compose-edit-analysis-decision", intent = new { decision = "misspelled", wordform } },
            },
        })));
        Assert.True(batched.Succeeded, batched.Refusal?.Message);

        var authored = ReadDraft(path, draft).Operations;
        var createKitu = authored.Single(operation => operation.EntityId == kitu);
        var secondText = authored.Single(operation => operation.EntityId == second.Value.CreatedObjects[0].Id);
        Assert.Contains(createKitu.OperationId, secondText.DependsOn);
        Assert.Single(authored, operation => operation.Kind == LangProjectWordformsOperationKinds.Create);
    }

    [Fact]
    public void AReadingAddedAsUnknownReportsItsAnalysisAndLaterApprovalInTheSameDraftDependsOnIt()
    {
        var (path, _, wordform, _) = Prepare();
        const string draft = "reading-then-approve";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Synthetic reading decision")).Succeeded);
        string form, msa;
        using (var cache = pristine.NewScratch())
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
            form = CanonicalId.FromGuid(entry.LexemeFormOA.Guid).Value;
            msa = CanonicalId.FromGuid(entry.MorphoSyntaxAnalysesOC.First().Guid).Value;
        }

        var added = ProposalCommands.ComposeEditAnalysisDecision(new(path, Version, draft, JsonSerializer.Serialize(
            new { decision = "add-reading", wordform, morphs = new[] { new { form, msa } } })));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var created = Assert.Single(added.Value!.CreatedObjects);
        Assert.Equal(WfiAnalysisOperationKinds.CreateAnalysis, created.Kind);

        var approved = ProposalCommands.ComposeEditAnalysisDecision(new(path, Version, draft,
            JsonSerializer.Serialize(new { decision = "approve", analysis = created.Id })));
        Assert.True(approved.Succeeded, approved.Refusal?.Message);

        var operations = ReadDraft(path, draft).Operations;
        var creation = operations.Single(operation => operation.EntityId == created.Id);
        var approval = operations.Single(operation => operation.Kind == WfiAnalysisOperationKinds.AddRefEvaluations);
        Assert.Equal(created.Id, approval.Target);
        Assert.Contains(creation.OperationId, approval.DependsOn);
    }

    [Fact]
    public void RefusedTextIntentsLeaveTheDraftUnchanged()
    {
        var (path, ws, _, _) = Prepare();
        const string draft = "refused-text";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Synthetic refused text")).Succeeded);
        var before = ReadDraft(path, draft).Operations.Count;

        var unknownProperty = ProposalCommands.ComposeAuthorText(new(path, Version, draft,
            $$"""{"title":"T","ws":"{{ws}}","paragraphs":[{"segments":[["a"]]}],"extra":1}"""));
        var badDecision = ProposalCommands.ComposeEditAnalysisDecision(new(path, Version, draft,
            """{"decision":"approve"}"""));

        Assert.False(unknownProperty.Succeeded);
        Assert.False(badDecision.Succeeded);
        Assert.Equal(before, ReadDraft(path, draft).Operations.Count);
    }

    private (string Path, string Ws, string Wordform, string Analysis) Prepare()
    {
        using var cache = pristine.NewScratch();
        string wordform = null!, analysis = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var created = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("sita", cache.DefaultVernWs));
            var stored = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            created.AnalysesOC.Add(stored);
            wordform = CanonicalId.FromGuid(created.Guid).Value;
            analysis = CanonicalId.FromGuid(stored.Guid).Value;
        });
        new FwDataProjectLoader().Save(cache);
        return (cache.ProjectId.Path, cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id,
            wordform, analysis);
    }

    private static DraftDocument ReadDraft(string path, string name)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(name).ProposalJson!);
    }
}
