using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Stages the Help examples of compose-add-sense and compose-set-sense-msa in a Draft.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AddSenseDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private static string Intent(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void BothVerbsAreAgentToolsAndStageInADraft()
    {
        foreach (var verb in new[] { "compose-add-sense", "compose-set-sense-msa" })
        {
            var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == verb);
            Assert.True(descriptor.IsAgentTool);
            Assert.Contains(verb, ProposalCommands.DraftComposers.Keys);
        }
    }

    [Fact]
    public void ASenseIsAddedToTheSeededEntryAndRefusalsReachTheCallerWithTheNextCommand()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var entry = CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value;
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "senses", "Add a sense")).Succeeded);

        var added = ProposalCommands.ComposeIntent(new(path, Version, "senses", "compose-add-sense",
            Intent(new { entry, gloss = "a second meaning", analysisWs = ws, definition = "another sense" })));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.Contains(added.Value!.Operations, item => item.Kind == LexEntrySensesOperationKinds.Create);
        Assert.Contains(added.Value.Operations, item => item.Kind == LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis);

        var unknownKey = ProposalCommands.ComposeIntent(new(path, Version, "senses", "compose-add-sense",
            Intent(new { entry, gloss = "x", analysisWs = ws, msa = new { kind = "stem" } })));
        Assert.False(unknownKey.Succeeded);

        var foreign = ProposalCommands.ComposeIntent(new(path, Version, "senses", "compose-set-sense-msa",
            Intent(new { sense = CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value, analysis = entry })));
        Assert.False(foreign.Succeeded);
        Assert.Contains("must name a sense", foreign.Refusal?.Message);
    }

    [Fact]
    public void ASenseIsStagedOntoAnotherAnalysisOfItsEntry()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var sense = CanonicalId.FromGuid(entry.SensesOS[0].Guid).Value;
        IMoStemMsa other = null!;
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            other = cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(other);
        });
        var analysis = CanonicalId.FromGuid(other.Guid).Value;
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "senses", "Move a sense")).Succeeded);

        var moved = ProposalCommands.ComposeIntent(new(path, Version, "senses", "compose-set-sense-msa",
            Intent(new { sense, analysis })));
        Assert.True(moved.Succeeded, moved.Refusal?.Message);
        var operation = Assert.Single(moved.Value!.Operations);
        Assert.Equal(LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis, operation.Kind);
    }
}
