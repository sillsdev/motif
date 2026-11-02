using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Runs the worked example in the Help for compose-add-morphosyntax-analysis.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AddMorphoSyntaxAnalysisDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private static string Intent(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void FromInflectionClassIsATopLevelIntentFieldAndNestingItInsideMsaIsRefused()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var verb = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "second", "Second class")).Succeeded);
        var noun = ProposalCommands.ComposeAuthorPartOfSpeech(new(path, Version, "second", Intent(new { name = "Noun", ws })));
        var nounId = Assert.Single(noun.Value!.Operations, item => item.EntityId is not null).EntityId!;
        var classResult = ProposalCommands.ComposeAuthorInflectionClass(new(path, Version, "second",
            Intent(new { category = verb, name = "Class 2", ws })));
        var classId = Assert.Single(classResult.Value!.Operations, item => item.EntityId is not null).EntityId!;
        var entry = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, "second", Intent(new
        {
            form = "pag", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
            gloss = "agentive", analysisWs = ws, msa = new { kind = "derivational", category = verb, toCategory = nounId },
        })));
        var entryId = Assert.Single(entry.Value!.Operations, item => item.Kind == "lexical/lexDb/createEntries").EntityId!;

        var nested = ProposalCommands.ComposeIntent(new(path, Version, "second", "compose-add-morphosyntax-analysis", Intent(new
        {
            entry = entryId, gloss = "agentive (class 2)", analysisWs = ws,
            msa = new { kind = "derivational", category = verb, toCategory = nounId, fromInflectionClass = classId },
        })));
        var topLevel = ProposalCommands.ComposeIntent(new(path, Version, "second", "compose-add-morphosyntax-analysis", Intent(new
        {
            entry = entryId, gloss = "agentive (class 2)", analysisWs = ws, fromInflectionClass = classId,
            msa = new { kind = "derivational", category = verb, toCategory = nounId },
        })));

        Assert.False(nested.Succeeded);
        Assert.Contains("unknown property 'fromInflectionClass'", nested.Refusal?.Message);
        Assert.True(topLevel.Succeeded, topLevel.Refusal?.Message);
        Assert.Contains(topLevel.Value!.Operations, item => item.Kind == "grammar/moDerivAffMsa/setFromInflectionClass");
    }

    [Fact]
    public void AnAnalysisIsStagedOnAnExistingEntryInOneComposeAndOfferedToAgents()
    {
        using var cache = pristine.NewScratch();
        IPartOfSpeech second = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            second = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(second);
            second.Name.set_String(cache.DefaultAnalWs, "Second");
        });
        var category = CanonicalId.FromGuid(second.Guid);
        var entry = CanonicalId.FromGuid(pristine.Seed.FirstEntryId);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "copula", "Add a Copula analysis")).Succeeded);
        var staged = ProposalCommands.ComposeIntent(new(path, Version, "copula", "compose-add-morphosyntax-analysis",
            Intent(new
            {
                entry = entry.Value,
                msa = new { kind = "stem", category = category.Value },
                gloss = "second sense", analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs),
            })));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Contains(staged.Value!.Operations, operation => operation.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        Assert.Contains(staged.Value.Operations, operation => operation.Kind == LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis);
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == "compose-add-morphosyntax-analysis");
        Assert.True(descriptor.IsAgentTool);
    }
}
