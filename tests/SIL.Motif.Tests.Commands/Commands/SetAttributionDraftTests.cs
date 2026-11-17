using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class SetAttributionDraftTests(PristineProjectFixture pristine)
{
    private const string Tag = "lang-sme@9ba7be7 GPL-3.0";

    [Fact]
    public void BothCommandsStageIntoOneDraftAndANameNotInTheProjectIsRefusedWithItsLine()
    {
        string path;
        using (var cache = pristine.NewScratch()) path = cache.ProjectId.Path;
        Assert.True(ProposalCommands.New(new(path, "1.0", "attribution", null)).Succeeded);
        var first = CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value;
        var second = CanonicalId.FromGuid(pristine.Seed.SecondEntryId).Value;

        var unknown = Compose("compose-set-entry-attribution", new
        {
            tag = Tag,
            entries = new[] { new { entry = first, at = "line 1" }, new { entry = CanonicalId.Mint().Value, at = "line 2" } },
        });
        Assert.False(unknown.Succeeded);
        Assert.Contains("entry 2 (line 2)", unknown.Refusal!.Message, StringComparison.Ordinal);

        var entries = Compose("compose-set-entry-attribution", new { tag = Tag, entries = new[] { new { entry = first }, new { entry = second } } });
        Assert.True(entries.Succeeded, entries.Refusal?.Message);
        Assert.All(entries.Value!.Operations, operation => Assert.Equal(AttributionOperationKinds.AppendToBibliography, operation.Kind));
        Assert.Equal(2, entries.Value.Operations.Count);

        var project = Compose("compose-set-project-attribution", new { tag = Tag });
        Assert.True(project.Succeeded, project.Refusal?.Message);
        Assert.Equal(AttributionOperationKinds.AppendToDescription, Assert.Single(project.Value!.Operations).Kind);

        using var reopened = new FwDataProjectLoader().LoadCache(path);
        Assert.Equal(string.Empty, reopened.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.FirstEntryId).Bibliography.get_String(reopened.DefaultAnalWs).Text ?? string.Empty);

        SIL.Motif.Contract.Commands.CommandOutcome<SIL.Motif.Contract.Responses.ComposedOperationsResponse> Compose(string verb, object intent) =>
            ProposalCommands.ComposeIntent(new(path, "1.0", "attribution", verb, JsonSerializer.Serialize(intent)));
    }

    [Fact]
    public void TheChangesReadAsAttributionAndMoveNoParse()
    {
        var entry = new OperationEnvelope(CanonicalId.Mint(), AttributionOperationKinds.AppendToBibliography,
            target: CanonicalId.Mint(), after: JsonSerializer.SerializeToElement(new { ws = "en", text = Tag }));
        var project = new OperationEnvelope(CanonicalId.Mint(), AttributionOperationKinds.AppendToDescription,
            target: CanonicalId.Mint(), after: JsonSerializer.SerializeToElement(new { ws = "en", text = "Attribution: " + Tag }));

        Assert.Equal("Record an entry's attribution", ChangeVocabulary.Of(entry).Title);
        Assert.Equal("Record the project's attribution", ChangeVocabulary.Of(project).Title);
        Assert.False(ChangeVocabulary.Of(entry).ChangesGrammar);
        Assert.False(ChangeVocabulary.Of(project).ChangesGrammar);
        Assert.Equal("Record attribution on lang-sme", ChangeVocabulary.ComposerTitle("SetEntryAttribution", "lang-sme"));
        Assert.Equal("Record the source and licence of imported entries", ChangeVocabulary.ComposerTitle("SetEntryAttribution"));
    }

    [Theory]
    [InlineData("compose-set-entry-attribution", "motif_set_entry_attribution")]
    [InlineData("compose-set-project-attribution", "motif_set_project_attribution")]
    public void BothComposersAreReleasedDraftToolsInTheCatalog(string verb, string tool)
    {
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == verb);
        Assert.Equal(SIL.Motif.Commands.Catalog.CommandSurface.Released, descriptor.Surface);
        Assert.Equal(SIL.Motif.Commands.Catalog.AgentClass.Draft, descriptor.Agent);
        Assert.Equal(tool, descriptor.ToolName);
    }
}
