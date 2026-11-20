using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ProposalShowCurrentTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Version = "1.0";
    private readonly WalkthroughProject _project = new(pristine);

    public void Dispose() => _project.Dispose();

    private string GlossDraft(string name, Guid sense)
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(_project.FwDataPath, Version, name, name)).Succeeded);
        var added = ProposalCommands.AddSetGloss(new AddSetGlossRequest(_project.FwDataPath, Version, name,
            CanonicalId.FromGuid(sense).Value, "en", "changed gloss"));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        return name;
    }

    [Fact]
    public void AGlossChangeCarriesTheProjectsCurrentGlossWithNoTrial()
    {
        var draft = GlossDraft("current", _project.Seed.FirstSenseId);

        var shown = ProposalCommands.Show(new ShowProposalRequest(_project.FwDataPath, Version, draft, WithCurrent: true));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        var current = Assert.Single(Assert.Single(shown.Value!.Operations).Current!);
        Assert.Equal("lexical/sense/gloss", current.Field);
        Assert.Equal(CanonicalId.FromGuid(_project.Seed.FirstSenseId).Value, current.Target);
        Assert.Equal(SeededProject.FirstGloss, current.Value["en"]);
        Assert.Null(shown.Value.CurrentUnavailable);
    }

    [Fact]
    public void ThePlainReadCarriesNoCurrentValue()
    {
        var draft = GlossDraft("plain", _project.Seed.FirstSenseId);

        var shown = ProposalCommands.Show(new ShowProposalRequest(_project.FwDataPath, Version, draft));

        Assert.Null(Assert.Single(shown.Value!.Operations).Current);
    }

    [Fact]
    public void AnOperationOnATargetTheProjectLacksHasNoCurrentValueAndOthersStillRead()
    {
        var draft = GlossDraft("gone", _project.Seed.FirstSenseId);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(_project.FwDataPath, Version, draft,
            CanonicalId.Mint().Value, "en", "elsewhere")).Succeeded);

        var shown = ProposalCommands.Show(new ShowProposalRequest(_project.FwDataPath, Version, draft, WithCurrent: true));

        var operations = shown.Value!.Operations;
        Assert.Equal(1, operations.Count(operation => operation.Current is { Count: > 0 }));
        Assert.Equal(1, operations.Count(operation => operation.Current is null));
    }

    [Fact]
    public void AnUnreadableProjectStillReturnsTheProposalWithTheReason()
    {
        var draft = GlossDraft("unreadable", _project.Seed.FirstSenseId);
        File.WriteAllText(_project.FwDataPath, "not a project");

        var shown = ProposalCommands.Show(new ShowProposalRequest(_project.FwDataPath, Version, draft, WithCurrent: true));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.NotNull(shown.Value!.CurrentUnavailable);
        Assert.Single(shown.Value.Operations);
    }
}
