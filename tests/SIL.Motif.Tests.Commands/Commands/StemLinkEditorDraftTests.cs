using System.Linq;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class StemLinkEditorDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void StemNameIsAttachedToAStagedAllomorphWithDependenciesAndCitation()
    {
        var (path, vernacular, analysis, category) = Open();
        const string draft = "stem-name-link";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Attach a stem name")).Succeeded);
        var feature = Compose(ProposalCommands.ComposeAuthorInflectionFeature(new(path, Version, draft,
            Json(new { name = "Tam", ws = analysis, values = new[] { new { name = "Tense", abbreviation = "tense" } } }))));
        var featureId = feature.Operations.Single(op => op.Kind == "grammar/fsFeatureSystem/createFeatures").EntityId!;
        var valueId = feature.Operations.Single(op => op.Kind == "grammar/fsClosedFeature/createValues").EntityId!;
        var name = Compose(ProposalCommands.ComposeAuthorStemName(new(path, Version, draft, Json(new
        {
            category, name = "KU", ws = analysis,
            regions = new[] { new[] { new { feature = featureId, value = valueId } } },
        }))));
        var nameCreate = name.Operations.Single(op => op.Kind == PartOfSpeechStemNamesOperationKinds.Create);
        var entry = Compose(ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, draft, Json(new
        {
            form = "kul", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
            gloss = "eat", analysisWs = analysis, msa = new { kind = "stem", category },
        }))));
        var formCreate = entry.Operations.Single(op => op.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        var formId = formCreate.EntityId!;

        var before = DraftBytes(path, draft);
        var mismatch = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-edit-stem-allomorph-stem-name",
            Json(new { target = formId, expectedStemName = nameCreate.EntityId, stemName = nameCreate.EntityId })));
        Assert.False(mismatch.Succeeded);
        Assert.Contains("differs from the expected stem name", mismatch.Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal(before, DraftBytes(path, draft));

        var foreignCategory = Compose(ProposalCommands.ComposeAuthorPartOfSpeech(new(path, Version, draft,
            Json(new { name = "Other", ws = analysis }))));
        var foreignId = foreignCategory.Operations.Single(op => op.EntityId is not null).EntityId!;
        var foreignName = Compose(ProposalCommands.ComposeAuthorStemName(new(path, Version, draft, Json(new
        {
            category = foreignId, name = "ELSEWHERE", ws = analysis,
            regions = new[] { new[] { new { feature = featureId, value = valueId } } },
        }))));
        var foreignNameId = foreignName.Operations.Single(op => op.Kind == PartOfSpeechStemNamesOperationKinds.Create)
            .EntityId!;
        var beforeForeign = DraftBytes(path, draft);
        var foreign = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-edit-stem-allomorph-stem-name",
            Json(new { target = formId, expectedStemName = (string?)null, stemName = foreignNameId })));
        Assert.False(foreign.Succeeded);
        Assert.Contains("must belong to a stem category", foreign.Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal(beforeForeign, DraftBytes(path, draft));

        var batch = ProposalCommands.ComposeBatch(new(path, Version, draft, Json(new
        {
            intents = new object[]
            {
                new
                {
                    verb = "compose-edit-stem-allomorph-stem-name",
                    intent = new { target = formId, expectedStemName = (string?)null, stemName = nameCreate.EntityId },
                    citation = new { source = "Verb grammar", locator = "§ 5" },
                },
            },
        })));
        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var edit = Assert.Single(Assert.Single(batch.Value!.Intents).Operations);
        Assert.Equal(MoStemAllomorphStemNameOperationKinds.SetStemName, edit.Kind);

        var saved = Assert.Single(ReadDraft(path, draft).Operations, op => op.OperationId.Value == edit.OperationId);
        var dependencies = saved.DependsOn.Select(dependency => dependency.OperationId.Value).ToArray();
        Assert.Contains(nameCreate.OperationId, dependencies);
        Assert.Contains(formCreate.OperationId, dependencies);
        var citation = OperationCitation.FromProvenance(saved.Provenance);
        Assert.Equal(("Verb grammar", "§ 5"), (citation!.Source, citation.Locator));

        var again = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-edit-stem-allomorph-stem-name",
            Json(new { target = formId, expectedStemName = (string?)null, stemName = nameCreate.EntityId })));
        Assert.False(again.Succeeded);
    }

    [Fact]
    public void InflectionClassIsAttachedToAStagedStemAnalysisWithDependenciesAndCitation()
    {
        var (path, vernacular, analysis, category) = Open();
        const string draft = "stem-class-link";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Attach a class")).Succeeded);
        var cls = Compose(ProposalCommands.ComposeAuthorInflectionClass(new(path, Version, draft,
            Json(new { category, name = "Class Seven", ws = analysis }))));
        var classCreate = cls.Operations.Single(op => op.Kind == PartOfSpeechInflectionClassesOperationKinds.Create);
        var entry = Compose(ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, draft, Json(new
        {
            form = "tabu", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
            gloss = "book", analysisWs = analysis, msa = new { kind = "stem", category },
        }))));
        var msaCreate = entry.Operations.Single(op => op.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        var before = DraftBytes(path, draft);
        var mismatch = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-edit-stem-inflection-class",
            Json(new { target = msaCreate.EntityId, expectedInflectionClass = classCreate.EntityId, inflectionClass = (string?)null })));
        Assert.False(mismatch.Succeeded);
        Assert.Contains("differs from the expected inflection class", mismatch.Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal(before, DraftBytes(path, draft));

        var foreignCategory = Compose(ProposalCommands.ComposeAuthorPartOfSpeech(new(path, Version, draft,
            Json(new { name = "Other", ws = analysis }))));
        var foreignId = foreignCategory.Operations.Single(op => op.EntityId is not null).EntityId!;
        var foreignClass = Compose(ProposalCommands.ComposeAuthorInflectionClass(new(path, Version, draft,
            Json(new { category = foreignId, name = "Elsewhere", ws = analysis }))));
        var foreignClassId = foreignClass.Operations.Single(op => op.EntityId is not null).EntityId!;
        var beforeForeign = DraftBytes(path, draft);
        var foreign = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-edit-stem-inflection-class",
            Json(new { target = msaCreate.EntityId, expectedInflectionClass = (string?)null, inflectionClass = foreignClassId })));
        Assert.False(foreign.Succeeded);
        Assert.Contains("must belong to the part of speech", foreign.Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal(beforeForeign, DraftBytes(path, draft));

        var batch = ProposalCommands.ComposeBatch(new(path, Version, draft, Json(new
        {
            intents = new object[]
            {
                new
                {
                    verb = "compose-edit-stem-inflection-class",
                    intent = new
                    {
                        target = msaCreate.EntityId, expectedInflectionClass = (string?)null,
                        inflectionClass = classCreate.EntityId,
                    },
                    citation = new { source = "Noun grammar", locator = "§ 3" },
                },
            },
        })));
        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var edit = Assert.Single(Assert.Single(batch.Value!.Intents).Operations);
        Assert.Equal(MoStemMsaInflectionClassOperationKinds.SetInflectionClass, edit.Kind);

        var saved = Assert.Single(ReadDraft(path, draft).Operations, op => op.OperationId.Value == edit.OperationId);
        var dependencies = saved.DependsOn.Select(dependency => dependency.OperationId.Value).ToArray();
        Assert.Contains(classCreate.OperationId, dependencies);
        Assert.Contains(msaCreate.OperationId, dependencies);
        var citation = OperationCitation.FromProvenance(saved.Provenance);
        Assert.Equal(("Noun grammar", "§ 3"), (citation!.Source, citation.Locator));
    }

    [Fact]
    public void TwoEditsOfOneStemAnalysisInOneBatchAreRefusedWhenAdded()
    {
        var (path, vernacular, analysis, category) = Open();
        const string draft = "stem-class-twice";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Attach a class twice")).Succeeded);
        var first = Compose(ProposalCommands.ComposeAuthorInflectionClass(new(path, Version, draft,
            Json(new { category, name = "Class One", ws = analysis }))));
        var firstId = first.Operations.Single(op => op.Kind == PartOfSpeechInflectionClassesOperationKinds.Create).EntityId!;
        var second = Compose(ProposalCommands.ComposeAuthorInflectionClass(new(path, Version, draft,
            Json(new { category, name = "Class Two", ws = analysis }))));
        var secondId = second.Operations.Single(op => op.Kind == PartOfSpeechInflectionClassesOperationKinds.Create).EntityId!;
        var entry = Compose(ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, draft, Json(new
        {
            form = "tabu", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
            gloss = "book", analysisWs = analysis, msa = new { kind = "stem", category },
        }))));
        var msa = entry.Operations.Single(op => op.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create).EntityId!;
        var before = DraftBytes(path, draft);

        var refused = ProposalCommands.ComposeBatch(new(path, Version, draft, Json(new
        {
            intents = new object[]
            {
                new
                {
                    verb = "compose-edit-stem-inflection-class",
                    intent = new { target = msa, expectedInflectionClass = (string?)null, inflectionClass = firstId },
                },
                new
                {
                    verb = "compose-edit-stem-inflection-class",
                    intent = new { target = msa, expectedInflectionClass = firstId, inflectionClass = secondId },
                },
            },
        })));

        Assert.False(refused.Succeeded);
        Assert.Equal("draft.invalid", refused.Refusal!.Code);
        Assert.Contains("same slot", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.True(refused.Refusal.Facts.ContainsKey("existingOperationId"));
        Assert.True(refused.Refusal.Facts.ContainsKey("duplicateOperationId"));
        Assert.Equal(before, DraftBytes(path, draft));
    }

    private (string Path, string Vernacular, string Analysis, string Category) Open()
    {
        using var cache = pristine.NewScratch();
        return (cache.ProjectId.Path, cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs),
            CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value);
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static ComposedOperationsResponse Compose(CommandOutcome<ComposedOperationsResponse> result)
    {
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
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
