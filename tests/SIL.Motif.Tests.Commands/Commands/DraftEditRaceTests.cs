using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class DraftEditRaceTests(PristineProjectFixture pristine)
{
    private static string Version => MotifProductVersion.CurrentText;

    [Fact]
    public void AComposerWhoseDraftChangedWhileItComposedIsRefusedAndTheOtherEditIsKept()
    {
        var path = NewProject();
        const string draftName = "race";
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, Version, draftName, "Before")).Succeeded);

        var composed = WithRacingEdit(() => Assert.True(ProposalCommands.Label(new LabelRequest(path, Version,
                draftName, "Relabelled while the composer ran")).Succeeded),
            () => ProposalCommands.ComposeAuthorPhoneme(new ComposeAuthorPhonemeRequest(path, Version, draftName,
                JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } }))));

        Assert.Equal("draft.revision-conflict", composed.Refusal?.Code);
        using var draft = JsonDocument.Parse(ReadDraft(path, draftName));
        Assert.Equal("Relabelled while the composer ran", draft.RootElement.GetProperty("label").GetString());
        Assert.Empty(draft.RootElement.GetProperty("operations").EnumerateArray());
    }

    [Fact]
    public void AnUndoThatLandsWhileAComposerRunsWinsAndTheComposerIsRefused()
    {
        var (path, staged) = StagedChange("undo-first");

        var composed = WithRacingEdit(
            () => Assert.True(PendingChanges.Remove(new RemovePendingChangeRequest(path, Version, staged.Revision,
                staged.Changes[0].ChangeId)).Succeeded),
            () => ProposalCommands.ComposeAuthorPhoneme(new ComposeAuthorPhonemeRequest(path, Version,
                PendingChanges.DraftName, JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } }))));

        Assert.Equal("draft.revision-conflict", composed.Refusal?.Code);
        var after = PendingChanges.Load(new PendingChangesRequest(path, Version)).Value!;
        Assert.Empty(after.Changes);
        using var draft = JsonDocument.Parse(ReadDraft(path, PendingChanges.DraftName));
        Assert.Empty(draft.RootElement.GetProperty("operations").EnumerateArray());
    }

    [Fact]
    public void AnUndoReadBeforeAComposerSavedIsRefusedAndTheComposedEditIsKept()
    {
        var (path, staged) = StagedChange("composer-first");

        var composed = ProposalCommands.ComposeAuthorPhoneme(new ComposeAuthorPhonemeRequest(path, Version,
            PendingChanges.DraftName, JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));
        var undone = PendingChanges.Remove(new RemovePendingChangeRequest(path, Version, staged.Revision,
            staged.Changes[0].ChangeId));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        Assert.Equal("change.revision-conflict", undone.Refusal?.Code);
        Assert.Single(PendingChanges.Load(new PendingChangesRequest(path, Version)).Value!.Changes);
        using var draft = JsonDocument.Parse(ReadDraft(path, PendingChanges.DraftName));
        Assert.Contains(draft.RootElement.GetProperty("operations").EnumerateArray(),
            operation => operation.GetProperty("kind").GetString()!.Contains("phoneme", StringComparison.OrdinalIgnoreCase));
    }

    private static T WithRacingEdit<T>(Action race, Func<T> edit)
    {
        ProposalCommands.BeforeDraftSave.Value = _ =>
        {
            ProposalCommands.BeforeDraftSave.Value = null;
            race();
        };
        try { return edit(); }
        finally { ProposalCommands.BeforeDraftSave.Value = null; }
    }

    private string NewProject()
    {
        using var scratch = pristine.NewScratch();
        return scratch.ProjectId.Path;
    }

    private (string Path, PendingChangesSnapshot Staged) StagedChange(string word)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "draft-race-root");
        Directory.CreateDirectory(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var staged = PendingChanges.Put(new PutPendingChangeRequest(path, Version,
            PendingChanges.Load(new PendingChangesRequest(path, Version)).Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling", CanonicalId.FromGuid(wordformId).Value,
                word, OriginPage: "Texts")));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        return (path, staged.Value!);
    }

    private static string ReadDraft(string path, string draftName)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return new ProposalRepository(database).GetDraft(draftName).ProposalJson!;
    }
}
