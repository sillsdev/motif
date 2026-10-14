using System;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Cli;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class MinimalSwahiliSurfaceRedTests(PristineProjectFixture pristine)
{
    [Fact]
    public void LexicalEntryAcceptsTopLevelAffixClassesAndEnvironmentsThroughDraftCommand()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var formWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();

        const string version = "0.1.0";
        const string draft = "swahili-affix-surface";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Author a conditioned prefix")).Succeeded);

        var inflectionClass = ProposalCommands.ComposeAuthorInflectionClass(new(path, version, draft,
            JsonSerializer.Serialize(new { category, name = "1/2", ws = analysisWs })));
        Assert.True(inflectionClass.Succeeded, inflectionClass.Refusal?.Message);
        var classId = inflectionClass.Value!.Operations.Single(operation => operation.EntityId is not null).EntityId!;

        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        var phonemeId = phoneme.Value!.Operations.Single(operation =>
            operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!;
        var environment = ProposalCommands.ComposeAuthorEnvironment(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                name = "after a",
                left = new[] { new { phoneme = phonemeId } },
                right = Array.Empty<object>(),
            })));
        Assert.True(environment.Succeeded, environment.Refusal?.Message);
        var environmentId = environment.Value!.Operations.Single(operation =>
            operation.Kind.EndsWith("/createEnvironments", StringComparison.Ordinal)).EntityId!;

        var entry = ProposalCommands.ComposeAuthorLexicalEntry(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                form = "m",
                formWs,
                morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
                gloss = "class 1/2",
                analysisWs,
                msa = new { kind = "inflectional", category },
                inflectionClasses = new[] { classId },
                environments = new[] { environmentId },
            })));

        Assert.True(entry.Succeeded, entry.Refusal?.Message);
        var operations = entry.Value!.Operations;
        Assert.Contains(operations, operation =>
            operation.Kind == MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses);
        Assert.Contains(operations, operation =>
            operation.Kind == MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv);
    }

    [Fact]
    public void AlternateFormAuthoringIsAvailableInThePublicCommandAndCliCatalogs()
    {
        const string commandName = "compose-author-alternate-form";

        Assert.Contains(CommandCatalog.All, command => command.Name == commandName &&
            command.Surface == CommandSurface.AdvancedAi && command.Agent == AgentClass.Draft);
        Assert.Contains(CliVerbCatalog.All, verb => verb.CommandName == commandName);
    }
}
