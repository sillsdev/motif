using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AffixProcessDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void ComposerReplaysEarlierPhonemeAndNaturalClassReferences()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "process", "Add a copying affix")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "process",
            JsonSerializer.Serialize(new { name = "process-i", representations = new[] { "i" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        var phonemeId = Assert.Single(phoneme.Value!.Operations,
            operation => operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!;
        var naturalClass = ProposalCommands.ComposeAuthorNaturalClass(new(path, Version, "process",
            JsonSerializer.Serialize(new { name = "process vowels", abbreviation = "PV", members = new[] { phonemeId } })));
        Assert.True(naturalClass.Succeeded, naturalClass.Refusal?.Message);
        var naturalClassId = Assert.Single(naturalClass.Value!.Operations,
            operation => operation.Kind.EndsWith("/createNaturalClasses", StringComparison.Ordinal)).EntityId!;

        var composed = ProposalCommands.ComposeIntent(new(path, Version, "process", "compose-author-affix-process",
            ProcessIntent(phonemeId, naturalClassId, analysisWs,
                CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value)));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        Assert.True(composed.Value!.OperationCount > 0);
        var draft = ReadDraft(path, "process");
        Assert.Contains(draft.ComposerProvenance, item =>
            item.GetProperty("composer").GetString() == "AuthorAffixProcess");
        var processForm = Assert.Single(draft.Operations,
            operation => operation.Kind == "lexical/lexEntry/createLexemeForm");
        Assert.Equal("MoAffixProcess", processForm.After["class"].GetString());
        Assert.DoesNotContain("text", processForm.After.Keys);
        var inputs = draft.Operations.Where(operation => operation.Kind == "grammar/moAffixProcess/createInput").ToArray();
        Assert.Equal(2, inputs.Length);
        Assert.Contains(inputs, operation => operation.After["class"].GetString() == "PhVariable");
        Assert.Contains(inputs, operation => operation.After["class"].GetString() == "PhSimpleContextNC");
        Assert.Contains(draft.Operations, operation => operation.Kind == "grammar/phSimpleContextNC/setFeatureStructure" &&
            operation.After.Values.Any(value => value.ValueKind == System.Text.Json.JsonValueKind.String &&
                value.GetString() == naturalClassId));
        var outputs = draft.Operations.Where(operation => operation.Kind == "grammar/moAffixProcess/createOutput").ToArray();
        Assert.Equal(4, outputs.Length);
        Assert.Equal(3, outputs.Count(operation => operation.After["class"].GetString() == "MoCopyFromInput"));
        Assert.Single(outputs, operation => operation.After["class"].GetString() == "MoInsertPhones");
        Assert.Equal(3, draft.Operations.Count(operation => operation.Kind == "grammar/moCopyFromInput/setContent"));
        Assert.Contains(draft.Operations, operation => operation.Kind == "grammar/moInsertPhones/addRefContent" &&
            operation.After.Values.Any(value => value.ValueKind == System.Text.Json.JsonValueKind.String &&
                value.GetString() == phonemeId));
    }

    [Fact]
    public void AnAffixProcessCanCarryAHeadwordAndOtherwiseStaysNameless()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var vernacularWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "headword", "Add an infix")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "headword",
            JsonSerializer.Serialize(new { name = "headword-i", representations = new[] { "i" } })));
        var phonemeId = Assert.Single(phoneme.Value!.Operations,
            operation => operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!;
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        var intent = new Dictionary<string, object?>
        {
            ["morphType"] = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphInfix).Value,
            ["gloss"] = "perfective", ["analysisWs"] = analysisWs, ["form"] = "in", ["formWs"] = vernacularWs,
            ["msa"] = new { kind = "unclassified", category },
            ["input"] = new object[] { new { name = "c", phoneme = phonemeId }, new { name = "stem", remaining = true } },
            ["output"] = new object[] { new { copy = "c" }, new { insert = new[] { phonemeId } }, new { copy = "stem" } },
        };
        var composed = ProposalCommands.ComposeIntent(new(path, Version, "headword", "compose-author-affix-process",
            JsonSerializer.Serialize(intent)));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var draft = ReadDraft(path, "headword");
        var create = Assert.Single(draft.Operations, operation => operation.Kind == "lexical/lexEntry/createLexemeForm");
        var setForm = Assert.Single(draft.Operations, operation => operation.Kind == "grammar/moForm/setForm");
        Assert.Equal(create.EntityId, setForm.Target);
        Assert.Equal("in", setForm.After["text"].GetString());
        Assert.Equal(vernacularWs, setForm.After["ws"].GetString());
        Assert.NotEmpty(setForm.DependsOn);

        intent.Remove("form");
        var refused = ProposalCommands.ComposeIntent(new(path, Version, "headword", "compose-author-affix-process",
            JsonSerializer.Serialize(intent)));
        Assert.False(refused.Succeeded);
        Assert.Contains("form", refused.Refusal!.Message);
    }

    [Fact]
    public void BatchReplaysEarlierReferencesWithCitationsAndAllOrNothingAppendRules()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "batch", "Add a copying affix")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "batch",
            JsonSerializer.Serialize(new { name = "process-i", representations = new[] { "i" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        var phonemeId = Assert.Single(phoneme.Value!.Operations,
            operation => operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!;
        var naturalClass = ProposalCommands.ComposeAuthorNaturalClass(new(path, Version, "batch",
            JsonSerializer.Serialize(new { name = "batch vowels", abbreviation = "BV", members = new[] { phonemeId } })));
        Assert.True(naturalClass.Succeeded, naturalClass.Refusal?.Message);
        var naturalClassId = Assert.Single(naturalClass.Value!.Operations,
            operation => operation.Kind.EndsWith("/createNaturalClasses", StringComparison.Ordinal)).EntityId!;
        using var intentDocument = JsonDocument.Parse(ProcessIntent(phonemeId, naturalClassId, analysisWs,
            CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value));
        var intent = intentDocument.RootElement.Clone();
        var batchJson = JsonSerializer.Serialize(new
        {
            intents = new[]
            {
                new
                {
                    verb = "compose-author-affix-process",
                    intent,
                    citation = new { source = "Field notes", locator = "p. 12" },
                },
            },
        });

        var batch = ProposalCommands.ComposeBatch(new(path, Version, "batch", batchJson));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var afterBatch = ReadDraft(path, "batch");
        var processOperations = afterBatch.Operations.Where(operation =>
            operation.Kind == "lexical/lexEntry/createLexemeForm" ||
            operation.Kind.StartsWith("grammar/moAffixProcess/", StringComparison.Ordinal) ||
            operation.Kind is "grammar/moCopyFromInput/setContent" or "grammar/moInsertPhones/addRefContent").ToArray();
        Assert.NotEmpty(processOperations);
        Assert.All(processOperations, operation => Assert.Contains(operation.Provenance, item =>
            item.GetProperty("source").GetString() == "Field notes" && item.GetProperty("locator").GetString() == "p. 12"));

        var beforeFailure = afterBatch.Operations.Select(operation => operation.OperationId).ToArray();
        var invalidBatch = JsonSerializer.Serialize(new
        {
            intents = new object[]
            {
                new { verb = "compose-author-phoneme", intent = new { name = "unused", representations = new[] { "u" } } },
                new
                {
                    verb = "compose-author-affix-process",
                    intent = JsonSerializer.Deserialize<JsonElement>(ProcessIntent("not-a-canonical-id", naturalClassId, analysisWs,
                        CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value)),
                },
            },
        });

        var refused = ProposalCommands.ComposeBatch(new(path, Version, "batch", invalidBatch));

        Assert.False(refused.Succeeded);
        Assert.Equal(beforeFailure, ReadDraft(path, "batch").Operations.Select(operation => operation.OperationId));
    }

    [Fact]
    public void AllomorphComposerReplaysAStagedProcessEntryAndAddsAProcessAlternate()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "allomorph", "Add a second process form")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "allomorph",
            JsonSerializer.Serialize(new { name = "allomorph-i", representations = new[] { "i" } })));
        var phonemeId = Assert.Single(phoneme.Value!.Operations,
            operation => operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!;
        var naturalClass = ProposalCommands.ComposeAuthorNaturalClass(new(path, Version, "allomorph",
            JsonSerializer.Serialize(new { name = "allomorph vowels", abbreviation = "AV", members = new[] { phonemeId } })));
        var naturalClassId = Assert.Single(naturalClass.Value!.Operations,
            operation => operation.Kind.EndsWith("/createNaturalClasses", StringComparison.Ordinal)).EntityId!;
        var entry = ProposalCommands.ComposeIntent(new(path, Version, "allomorph", "compose-author-affix-process",
            ProcessIntent(phonemeId, naturalClassId, analysisWs, CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value)));
        Assert.True(entry.Succeeded, entry.Refusal?.Message);
        var entryId = Assert.Single(ReadDraft(path, "allomorph").Operations,
            operation => operation.Kind == "lexical/lexDb/createEntries").EntityId!;

        var composed = ProposalCommands.ComposeIntent(new(path, Version, "allomorph",
            "compose-author-affix-process-allomorph", JsonSerializer.Serialize(new
            {
                entry = entryId,
                input = new object[] { new { name = "stem", remaining = true } },
                output = new object[] { new { insert = new[] { phonemeId } }, new { copy = "stem" } },
            })));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var draft = ReadDraft(path, "allomorph");
        Assert.Contains(draft.ComposerProvenance, item =>
            item.GetProperty("composer").GetString() == "AuthorAffixProcessAllomorph");
        var alternate = Assert.Single(draft.Operations, operation => operation.Kind == "lexical/lexEntry/createAlternateForms");
        Assert.Equal("MoAffixProcess", alternate.After["class"].GetString());
        Assert.Equal(entryId, alternate.Target);
    }

    [Fact]
    public void DerivationalRestrictionComposerReplaysAStagedClassAndDerivationalEntry()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "restrict", "Restrict a derivational prefix")).Succeeded);
        var classResult = ProposalCommands.ComposeAuthorInflectionClass(new(path, Version, "restrict",
            JsonSerializer.Serialize(new { category, name = "Class Seven", ws = analysisWs })));
        Assert.True(classResult.Succeeded, classResult.Refusal?.Message);
        var classId = Assert.Single(classResult.Value!.Operations, operation => operation.EntityId is not null).EntityId!;
        var derivedResult = ProposalCommands.ComposeAuthorPartOfSpeech(new(path, Version, "restrict",
            JsonSerializer.Serialize(new { name = "Derived", ws = analysisWs })));
        Assert.True(derivedResult.Succeeded, derivedResult.Refusal?.Message);
        var derived = Assert.Single(derivedResult.Value!.Operations, operation => operation.EntityId is not null).EntityId!;
        var entry = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, "restrict", JsonSerializer.Serialize(new
        {
            form = "pag", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
            gloss = "agentive", analysisWs, msa = new { kind = "derivational", category, toCategory = derived },
        })));
        Assert.True(entry.Succeeded, entry.Refusal?.Message);
        var msaId = Assert.Single(entry.Value!.Operations,
            operation => operation.Kind == "lexical/lexEntry/createMorphoSyntaxAnalyses").EntityId!;

        var composed = ProposalCommands.ComposeIntent(new(path, Version, "restrict",
            "compose-edit-derivational-affix-restriction",
            JsonSerializer.Serialize(new { msa = msaId, fromInflectionClass = classId })));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var draft = ReadDraft(path, "restrict");
        Assert.Contains(draft.ComposerProvenance, item =>
            item.GetProperty("composer").GetString() == "EditDerivationalAffixRestriction");
        var set = Assert.Single(draft.Operations, operation => operation.Kind == "grammar/moDerivAffMsa/setFromInflectionClass");
        Assert.Equal(msaId, set.Target);
        Assert.Equal(classId, set.After["ref"].GetString());
        Assert.NotEmpty(set.DependsOn);
    }

    private static string ProcessIntent(string phonemeId, string naturalClassId, string analysisWs, string categoryId) => JsonSerializer.Serialize(new
    {
        morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
        gloss = "repeat",
        analysisWs,
        msa = new
        {
            kind = "unclassified",
            category = categoryId,
        },
        input = new object[]
        {
            new { name = "vowel", naturalClass = naturalClassId },
            new { name = "stem", remaining = true },
        },
        output = new object[]
        {
            new { copy = "vowel" },
            new { copy = "stem" },
            new { insert = new[] { phonemeId } },
            new { copy = "stem" },
        },
    });

    private static DraftDocument ReadDraft(string path, string draftName)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(draftName).ProposalJson!);
    }
}
