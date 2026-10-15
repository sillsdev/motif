using System.Text;
using System.Text.Json;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class InflectionClassEditorDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string DraftName = "inflection-class-editors";

    [Fact]
    public void EditorsUseSameDraftIdsPreserveCitationsAndAttributeReversedPreviewEffects()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var categoryId = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        cache.Dispose();

        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Edit inflection classes")).Succeeded);
        var category = categoryId.Value;
        var classResult = ProposalCommands.ComposeAuthorInflectionClass(new(path, Version, DraftName,
            JsonSerializer.Serialize(new { category, name = "Class Seven", ws = analysis })));
        Assert.True(classResult.Succeeded, classResult.Refusal?.Message);
        var classCreate = Assert.Single(classResult.Value!.Operations, operation => operation.EntityId is not null);
        var classId = Assert.IsType<string>(classCreate.EntityId);

        var entryResult = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, DraftName,
            JsonSerializer.Serialize(new
            {
                form = "m",
                formWs = vernacular,
                morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
                gloss = "one",
                analysisWs = analysis,
                msa = new { kind = "inflectional", category },
            })));
        Assert.True(entryResult.Succeeded, entryResult.Refusal?.Message);
        var formCreate = Assert.Single(entryResult.Value!.Operations,
            operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        var formId = Assert.IsType<string>(formCreate.EntityId);

        var batchJson = JsonSerializer.Serialize(new
        {
            intents = new object[]
            {
                new
                {
                    verb = "compose-edit-part-of-speech-inflection-class-default",
                    intent = new
                    {
                        target = category,
                        expectedInflectionClass = (string?)null,
                        inflectionClass = classId,
                    },
                    citation = new { source = "Inflectional grammar", locator = "§ 2" },
                },
                new
                {
                    verb = "compose-edit-affix-inflection-classes",
                    intent = new
                    {
                        target = formId,
                        expectedInflectionClasses = Array.Empty<string>(),
                        inflectionClasses = new[] { classId },
                    },
                    citation = new { source = "Inflectional grammar", locator = "§ 4" },
                },
            },
        });
        var batch = ProposalCommands.ComposeBatch(new(path, Version, DraftName, batchJson));
        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var defaultVerb = Assert.Single(batch.Value!.Intents,
            intent => intent.Verb == "compose-edit-part-of-speech-inflection-class-default");
        var affixVerb = Assert.Single(batch.Value.Intents,
            intent => intent.Verb == "compose-edit-affix-inflection-classes");
        var defaultEdit = Assert.Single(defaultVerb.Operations);
        var affixEdit = Assert.Single(affixVerb.Operations);

        var draft = ReadDraft(path, DraftName);
        var savedDefault = Assert.Single(draft.Operations,
            operation => operation.OperationId.Value == defaultEdit.OperationId);
        var savedAffix = Assert.Single(draft.Operations,
            operation => operation.OperationId.Value == affixEdit.OperationId);
        Assert.Contains(classCreate.OperationId, savedDefault.DependsOn
            .Select(dependency => dependency.OperationId.Value));
        Assert.Contains(classCreate.OperationId, savedAffix.DependsOn
            .Select(dependency => dependency.OperationId.Value));
        Assert.Contains(formCreate.OperationId, savedAffix.DependsOn
            .Select(dependency => dependency.OperationId.Value));
        var citation = OperationCitation.FromProvenance(savedAffix.Provenance);
        Assert.Equal(("Inflectional grammar", "§ 4"), (citation!.Source, citation.Locator));
        var defaultCitation = OperationCitation.FromProvenance(savedDefault.Provenance);
        Assert.Equal(("Inflectional grammar", "§ 2"), (defaultCitation!.Source, defaultCitation.Locator));

        var beforeMismatch = DraftBytes(path, DraftName);
        var mismatchDefault = ProposalCommands.ComposeIntent(
            new(path, Version, DraftName, "compose-edit-part-of-speech-inflection-class-default", JsonSerializer.Serialize(new
            {
                target = category,
                expectedInflectionClass = (string?)null,
                inflectionClass = (string?)null,
            })));
        Assert.False(mismatchDefault.Succeeded);
        Assert.Equal(beforeMismatch, DraftBytes(path, DraftName));

        var mismatchAffix = ProposalCommands.ComposeIntent(new(path, Version, DraftName, "compose-edit-affix-inflection-classes",
            JsonSerializer.Serialize(new
            {
                target = formId,
                expectedInflectionClasses = Array.Empty<string>(),
                inflectionClasses = Array.Empty<string>(),
            })));
        Assert.False(mismatchAffix.Succeeded);
        Assert.Equal(beforeMismatch, DraftBytes(path, DraftName));

        var closedRefusal = ProposalCommands.ComposeIntent(new(path, Version, DraftName, "compose-edit-affix-inflection-classes",
            JsonSerializer.Serialize(new
            {
                target = formId,
                expectedInflectionClasses = new[] { classId },
                inflectionClasses = new[] { classId },
                unsupported = true,
            })));
        Assert.False(closedRefusal.Succeeded);
        Assert.Equal(beforeMismatch, DraftBytes(path, DraftName));

        var closedDefaultRefusal = ProposalCommands.ComposeIntent(
            new(path, Version, DraftName, "compose-edit-part-of-speech-inflection-class-default", JsonSerializer.Serialize(new
            {
                target = category,
                expectedInflectionClass = classId,
                inflectionClass = classId,
                unsupported = true,
            })));
        Assert.False(closedDefaultRefusal.Succeeded);
        Assert.Equal(beforeMismatch, DraftBytes(path, DraftName));

        var defaultNoOp = ProposalCommands.ComposeIntent(
            new(path, Version, DraftName, "compose-edit-part-of-speech-inflection-class-default", JsonSerializer.Serialize(new
            {
                target = category,
                expectedInflectionClass = classId,
                inflectionClass = classId,
            })));
        Assert.True(defaultNoOp.Succeeded, defaultNoOp.Refusal?.Message);
        Assert.Empty(defaultNoOp.Value!.Operations);
        var affixNoOp = ProposalCommands.ComposeIntent(new(path, Version, DraftName, "compose-edit-affix-inflection-classes",
            JsonSerializer.Serialize(new
            {
                target = formId,
                expectedInflectionClasses = new[] { classId },
                inflectionClasses = new[] { classId },
            })));
        Assert.True(affixNoOp.Succeeded, affixNoOp.Refusal?.Message);
        Assert.Empty(affixNoOp.Value!.Operations);
        Assert.Equal(beforeMismatch, DraftBytes(path, DraftName));

        Assert.True(ProposalCommands.Label(new(path, Version, DraftName, "Class default and affix restriction")).Succeeded);
        Assert.True(ProposalCommands.Comment(new(path, Version, DraftName,
            "Use one inflection class as a category default and affix restriction.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var loaded = ProjectStoreCommand.Run(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = loaded.Value!;
        var reversed = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            proposal.Operations.Reverse().ToArray(), proposal.Extensions);
        using var scratch = DryRunScratch.Adopt(
            new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path), "inflection class editor preview");
        var dryRun = ProposalDryRunner.Run(scratch, reversed);
        var preview = EffectProjectionBuilder.Build(dryRun.ExpectedEffects, reversed);
        Assert.Contains(preview, effect => effect.OperationIds.Contains(defaultEdit.OperationId));
        Assert.Contains(preview, effect => effect.OperationIds.Contains(affixEdit.OperationId));
    }

    private static Proposal ReadDraft(string path, string draftName)
    {
        var result = ProjectStoreCommand.Run(path, Version, (database, _) =>
        {
            var document = ProposalCommands.DeserializeDraft(new ProposalRepository(database)
                .GetDraft(draftName).ProposalJson!);
            return CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(document)));
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
    }

    private static byte[] DraftBytes(string path, string draftName)
    {
        var result = ProjectStoreCommand.Run(path, Version, (database, _) =>
            CommandOutcome<string>.Success(new ProposalRepository(database).GetDraft(draftName).ProposalJson!));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return Encoding.UTF8.GetBytes(result.Value!);
    }
}
