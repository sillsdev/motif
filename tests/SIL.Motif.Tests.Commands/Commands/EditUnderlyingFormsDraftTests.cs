using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class EditUnderlyingFormsDraftTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData("compose-edit-allomorph-form", false)]
    [InlineData("compose-edit-citation-form", false)]
    [InlineData("compose-edit-allomorph-form", true)]
    [InlineData("compose-edit-citation-form", true)]
    public void GuardedEditsStageOnlyAndAStaleEditAddsNothing(string verb, bool clear)
    {
        using var cache = pristine.NewScratch();
        var seed = EmptySenseSeed.Add(cache, "pata");
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.EntryId);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var alternate = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>();
        IMoStemAllomorph form = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            form = alternate.Create();
            entry.AlternateFormsOS.Add(form);
            form.Form.set_String(cache.DefaultVernWs, "pata");
            if (clear) entry.CitationForm.set_String(cache.DefaultVernWs, "pata");
        });
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        var saved = File.ReadAllBytes(path);
        Assert.True(ProposalCommands.New(new(path, "0.1.0", "underlying", "Restore underlying forms")).Succeeded);
        object Intent(string expected) => verb == "compose-edit-allomorph-form"
            ? new { entry = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(entry.Guid).Value,
                target = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(form.Guid).Value,
                writingSystem = ws, expectedForm = expected, newForm = clear ? "" : "pātaʔ" }
            : new { target = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(entry.Guid).Value,
                writingSystem = ws, expectedForm = expected, newForm = clear ? "" : "páta" };

        var stale = ProposalCommands.ComposeIntent(new(path, "0.1.0", "underlying", verb,
            JsonSerializer.Serialize(Intent("wrong"))));
        Assert.False(stale.Succeeded);
        var composed = ProposalCommands.ComposeIntent(new(path, "0.1.0", "underlying", verb,
            JsonSerializer.Serialize(Intent(verb == "compose-edit-allomorph-form" || clear ? "pata" : ""))));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var operation = Assert.Single(composed.Value!.Operations);
        Assert.Equal(verb == "compose-edit-allomorph-form"
            ? (clear ? MoFormFormOperationKinds.ClearForm : MoFormFormOperationKinds.SetForm)
            : (clear ? LexEntryCitationFormOperationKinds.ClearCitationForm : LexEntryCitationFormOperationKinds.SetCitationForm), operation.Kind);
        Assert.Equal(saved, File.ReadAllBytes(path));
    }
}
