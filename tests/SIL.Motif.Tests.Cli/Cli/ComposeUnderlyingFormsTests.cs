using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ComposeUnderlyingFormsTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedCliStagesAGuardedFormWriteAndPreservesTheProject(bool citation)
    {
        using var cache = pristine.NewScratch();
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        IMoStemAllomorph form = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            form = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(form);
            form.Form.set_String(cache.DefaultVernWs, "pata");
        });
        var entryId = CanonicalId.FromGuid(entry.Guid).Value;
        var formId = CanonicalId.FromGuid(form.Guid).Value;
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var intent = new Dictionary<string, object> { ["target"] = citation ? entryId : formId,
            ["writingSystem"] = ws, ["expectedForm"] = citation ? "" : "pata", ["newForm"] = citation ? "páta" : "pātaʔ" };
        if (!citation) intent["entry"] = entryId;
        new FwDataProjectLoader().Save(cache);
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, "0.1.0", "underlying", "Restore underlying form")).Succeeded);
        var bytes = File.ReadAllBytes(path);
        var root = Path.Combine(Path.GetTempPath(), "motif-underlying-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = CliInProcess.Run(root, FakeParser.ExecutablePath, true,
                citation ? "compose-edit-citation-form" : "compose-edit-allomorph-form",
                "--project", path, "--draft", "underlying", "--intent", JsonSerializer.Serialize(intent), "--json");
            Assert.True(result.ExitCode == 0, result.Error + result.Output);
            using var database = ProjectMotifDatabase.Open(path);
            var draft = new ProposalRepository(database).GetDraft("underlying");
            var operation = Assert.Single(ProposalCommands.DeserializeDraft(draft.ProposalJson!).Operations);
            Assert.Equal(citation ? LexEntryCitationFormOperationKinds.SetCitationForm : MoFormFormOperationKinds.SetForm,
                operation.Kind);
            Assert.Equal(citation ? entryId : formId, operation.Target);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
