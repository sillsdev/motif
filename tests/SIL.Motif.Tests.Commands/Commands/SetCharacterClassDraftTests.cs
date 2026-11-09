using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class SetCharacterClassDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string DraftName = "curly-quotes";

    [Fact]
    public void TheComposerStagesOneWriteInTheDraftAndRefusesALetterAsPunctuationAndAnUnknownWritingSystem()
    {
        string path;
        string tag;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
            tag = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
        }

        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Curly quotes are punctuation")).Succeeded);

        var letter = Compose(path, new { ws = tag, @class = "punctuation", characters = "a" });
        Assert.False(letter.Succeeded);

        var unknown = Compose(path, new { ws = "zz-unknown", @class = "punctuation", characters = "‘" });
        Assert.False(unknown.Succeeded);

        var staged = Compose(path, new { ws = tag, @class = "punctuation", characters = "‘’" });
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var operation = Assert.Single(staged.Value!.Operations);
        Assert.Equal(LangProjectWritingSystemOperationKinds.SetCharacterClass, operation.Kind);
        var draft = ProjectStoreCommand.Run(path, Version, (database, _) =>
            CommandOutcome<string>.Success(new ProposalRepository(database).GetDraft(DraftName).ProposalJson!));
        Assert.Contains(LangProjectWritingSystemOperationKinds.SetCharacterClass, draft.Value!, StringComparison.Ordinal);
    }

    private static CommandOutcome<ComposedOperationsResponse> Compose(string path, object intent) =>
        ProposalCommands.ComposeIntent(new(path, Version, DraftName, "compose-set-character-class",
            JsonSerializer.Serialize(intent)));
}
