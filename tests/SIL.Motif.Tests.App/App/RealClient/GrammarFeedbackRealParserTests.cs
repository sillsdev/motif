using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class GrammarFeedbackRealParserTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public void CheckingGrammarStoresAndPublishesTheRealFinding()
    {
        using var project = ProjectWithAnEmptyRequiredSlot(pristine);
        var parserPath = PanGlossExecutable.TryLocate()!;
        output.WriteLine($"Parser: {parserPath}");
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var client = RealCommandClient.Create(project.ManagedRoot, parserPath);
            var request = new GrammarCheckRequest(project.FwDataPath);
            var captured = await client.CaptureBaselineAsync(
                new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            var before = await client.ReadStoredGrammarCheckAsync(request, CancellationToken.None);
            Assert.True(before.Succeeded, before.Refusal?.Message);
            Assert.Null(before.Value!.Check);

            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: parserPath);
            walkthrough.Show();
            var workspace = walkthrough.Workspace;
            await workspace.SetProjectAsync(project.FwDataPath);
            Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
            var grammar = workspace.Context.GrammarCheck;
            Assert.False(grammar.HasChecked);
            Assert.Null(workspace.Context.Evidence.Assessment);
            walkthrough.SkipSetup();
            Assert.True(grammar.CheckCommand.CanExecute(null));
            await grammar.CheckCommand.ExecuteAsync(null);

            var refusal = grammar.Refusal;
            Assert.True(refusal is null, refusal is null ? null : $"{refusal.Code}: {refusal.Message}");
            Assert.True(grammar.HasChecked);
            Assert.False(grammar.IsLoading);
            Assert.True(grammar.HasBaseline);
            Assert.Contains(grammar.Warnings.Findings,
                finding => finding.Code == GrammarFindingCodes.RequiredSlotEmpty);
            Assert.Contains(workspace.Context.GrammarSummary!.Findings,
                finding => finding.Code == GrammarFindingCodes.RequiredSlotEmpty);

            var reopened = RealCommandClient.Create(project.ManagedRoot, parserPath);
            var stored = await reopened.ReadStoredGrammarCheckAsync(request, CancellationToken.None);
            Assert.True(stored.Succeeded, stored.Refusal?.Message);
            Assert.NotNull(stored.Value!.Check);
            Assert.Contains(stored.Value.Check!.Findings,
                finding => finding.Code == GrammarFindingCodes.RequiredSlotEmpty);
            var warnings = WarningsCommand.Warnings(new WarningsRequest(project.FwDataPath));
            Assert.True(warnings.Succeeded, warnings.Refusal?.Message);
            Assert.Contains(warnings.Value!.Findings,
                finding => finding.Code == GrammarFindingCodes.RequiredSlotEmpty);
            var overview = workspace.Context.Health.Overview;
            Assert.NotNull(overview?.Warnings);
            Assert.Equal(stored.Value.Check.Findings.Count, overview!.Warnings!.Count);
            Assert.Equal(project.SourceSha256,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project.FwDataPath))));
        }, TimeSpan.FromMinutes(3));
    }

    private static WalkthroughProject ProjectWithAnEmptyRequiredSlot(PristineProjectFixture pristine) =>
        new(pristine, configure: (cache, _, _) =>
        {
            var services = cache.ServiceLocator;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var category = services.GetInstance<IPartOfSpeechFactory>().Create();
                cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
                category.Name.set_String(cache.DefaultAnalWs, "Verb");
                var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
                category.AffixTemplatesOS.Add(template);
                template.Name.set_String(cache.DefaultAnalWs, "Conjugation");
                var slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
                category.AffixSlotsOC.Add(slot);
                slot.Name.set_String(cache.DefaultAnalWs, "Tense");
                slot.Optional = false;
                template.SuffixSlotsRS.Add(slot);
            });
        });
}
