using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ComposeAllomorphRequiredFeaturesTests(PristineProjectFixture pristine)
{
    [Fact]
    public void TheTypedCliVerbStagesRequiredFeaturesAndPreservesTheSavedProject()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var operations = AuthorLexicalEntryComposer.Build(cache, new("pa",
            cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix), "synthetic",
            cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs),
            new(LexicalEntryMsaKind.Derivational, category, category)));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var operation in operations)
                OperationHandlerRegistry.Resolve(operation.Kind, "allomorph CLI fixture").ApplyAndCaptureEffect(cache, operation, []);
        });
        new FwDataProjectLoader().Save(cache);
        var form = operations.Single(operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm).EntityId!.Value;
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, "0.1.0", "required-features", "Select prefix forms")).Succeeded);
        var bytes = File.ReadAllBytes(path);
        var root = Path.Combine(Path.GetTempPath(), "motif-allomorph-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = CliInProcess.Run(root, FakeParser.ExecutablePath, true,
                "compose-author-allomorph-required-features", "--project", path, "--draft", "required-features",
                "--intent", JsonSerializer.Serialize(new { allomorph = form.Value }), "--json");
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Contains("Draft now has 1 operation(s).", result.Output, StringComparison.Ordinal);
            using var database = SIL.Motif.Tests.TestFixtures.ProjectMotifDatabase.Open(path);
            var draft = new SIL.Motif.Worker.Store.ProposalRepository(database).GetDraft("required-features");
            var document = ProposalCommands.DeserializeDraft(draft.ProposalJson!);
            var operation = Assert.Single(document.Operations);
            Assert.Equal(MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures, operation.Kind);
            Assert.Equal(form.Value, operation.Target);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
