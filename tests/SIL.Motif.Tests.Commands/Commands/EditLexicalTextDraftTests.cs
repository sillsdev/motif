using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class EditLexicalTextDraftTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData("compose-edit-definition", null, "lexical/lexSense/setDefinition")]
    [InlineData("compose-edit-gloss", null, "lexical/lexSense/setGloss")]
    [InlineData("compose-edit-lexical-note", "note", "lexical/lexEntry/setComment")]
    [InlineData("compose-edit-lexical-note", "grammar-note", "lexical/lexSense/setGrammarNote")]
    public void GuardedTextEditsStageAndAStaleAppendLeavesTheDraftUntouched(string verb, string? field, string kind)
    {
        var path = pristine.CopyProjectFile();
        var target = CanonicalId.FromGuid(field == "note" ? pristine.Seed.FirstEntryId : pristine.Seed.FirstSenseId).Value;
        string expected;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
            expected = verb == "compose-edit-gloss" ? cache.ServiceLocator.GetInstance<SIL.LCModel.ILexSenseRepository>()
                .GetObject(pristine.Seed.FirstSenseId).Gloss.get_String(cache.DefaultAnalWs).Text ?? "" : "";
        Assert.True(ProposalCommands.New(new(path, "0.1.0", "text", "Edit lexical text")).Succeeded);
        string Intent(string guard)
        {
            var value = new Dictionary<string, object>
            {
                ["target"] = target,
                ["alternatives"] = new[] { new { writingSystem = "en", expectedText = guard, text = "replacement" } },
            };
            if (field is not null) value["field"] = field;
            return JsonSerializer.Serialize(value);
        }
        var stale = ProposalCommands.ComposeIntent(new(path, "0.1.0", "text", verb, Intent("wrong")));
        Assert.False(stale.Succeeded);
        Assert.Contains("not the expected", stale.Refusal!.Message, StringComparison.Ordinal);
        var result = ProposalCommands.ComposeIntent(new(path, "0.1.0", "text", verb, Intent(expected)));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(kind, Assert.Single(result.Value!.Operations).Kind);
        Assert.Contains(ProposalCommands.ComposerDescriptors, descriptor => descriptor.Verb == verb && descriptor.IsAgentTool);
    }

    [Fact]
    public void UnknownNoteFieldsRefuseBeforeStaging()
    {
        var path = pristine.CopyProjectFile();
        Assert.True(ProposalCommands.New(new(path, "0.1.0", "text", "Edit lexical text")).Succeeded);
        var result = ProposalCommands.ComposeIntent(new(path, "0.1.0", "text", "compose-edit-lexical-note",
            JsonSerializer.Serialize(new { target = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value,
                field = "made-up-note", alternatives = new[] { new { writingSystem = "en", expectedText = "", text = "new" } } })));
        Assert.False(result.Succeeded);
    }
    [Theory]
    [InlineData("lookup:not-a-reference")]
    [InlineData("@ph")]
    public void BatchKeepsDefinitionGuardLiteralForEditAndClear(string saved)
    {
        var path = pristine.CopyProjectFile();
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(pristine.Seed.FirstSenseId)
                    .Definition.set_String(cache.DefaultAnalWs, saved));
            loader.Save(cache);
        }
        foreach (var (draft, replacement) in new[] { ("batch-edit", "changed"), ("batch-clear", "") })
        {
            Assert.True(ProposalCommands.New(new(path, "0.1.0", draft, "Edit definition")).Succeeded);
            var batch = JsonSerializer.Serialize(new { intents = new object[] {
                new { verb = "compose-author-phoneme", @as = "ph", intent = new { name = "literal test", representations = new[] { "ɸ" } } },
                new { verb = "compose-edit-definition", intent = new { target = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value,
                    alternatives = new[] { new { writingSystem = "en", expectedText = saved, text = replacement } } } },
            } });
            var result = ProposalCommands.ComposeBatch(new(path, "0.1.0", draft, batch));
            Assert.True(result.Succeeded, result.Refusal?.Message);
        }
    }

    [Fact]
    public void LookupThatResolvesToSavedDefinitionIsStillAStaleLiteralGuard()
    {
        var path = pristine.CopyProjectFile();
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(pristine.Seed.FirstSenseId)
                    .Definition.set_String(cache.DefaultAnalWs, CanonicalId.FromGuid(pristine.Seed.SecondSenseId).Value));
            loader.Save(cache);
        }
        Assert.True(ProposalCommands.New(new(path, "0.1.0", "stale-lookup", "Edit definition")).Succeeded);
        var intent = JsonSerializer.Serialize(new { target = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value,
            alternatives = new[] { new { writingSystem = "en", expectedText = "lookup:sense:motifb|second seeded gloss", text = "changed" } } });
        var result = ProposalCommands.ComposeIntent(new(path, "0.1.0", "stale-lookup", "compose-edit-definition", intent));
        Assert.False(result.Succeeded);
        Assert.Contains("not the expected", result.Refusal!.Message, StringComparison.Ordinal);
    }

}
