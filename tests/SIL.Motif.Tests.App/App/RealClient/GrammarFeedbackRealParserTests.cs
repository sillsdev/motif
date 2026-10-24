using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.App;
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
    public void CheckingGrammarFromWarningsStoresAndPublishesTheRealFinding()
    {
        using var project = ProjectWithAnEmptyRequiredSlot(pristine);
        var parserPath = PanGlossExecutable.TryLocate()!;
        var deadline = Stopwatch.GetTimestamp() + 3 * 60 * Stopwatch.Frequency;
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
            Assert.Equal(WorkspacePage.Overview, workspace.CurrentPage);
            var page = workspace.PageModel<WarningsPageModel>();
            walkthrough.WaitUntil(() => page.Grammar.HasChecked && !page.Grammar.IsLoading,
                WalkthroughSteps.Remaining(deadline), "the automatic grammar check did not complete");
            Assert.Contains(page.Grammar.Warnings.Findings,
                finding => finding.Code == GrammarFindingCodes.RequiredSlotEmpty);
            Assert.Null(workspace.Context.Evidence.Assessment);
            walkthrough.SkipSetup();
            walkthrough.ShowPage(WorkspacePage.Warnings);
            var button = Assert.Single(walkthrough.Window.GetVisualDescendants().OfType<Button>(), candidate =>
                Avalonia.Automation.AutomationProperties.GetName(candidate) ==
                    MotifAppStrings.CheckTheGrammarAgainAccessibleName && candidate.IsEffectivelyVisible);
            var retry = page.Grammar.CheckCommand;
            Assert.True(button.IsEffectivelyVisible);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.Same(retry, button.Command);
            Assert.True(retry.CanExecute(button.CommandParameter));
            var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
                walkthrough.Window)!.Value;
            var hit = walkthrough.Window.InputHitTest(centre) as Visual;
            output.WriteLine($"Before click: point={centre}; target={button.GetType().Name} bounds={button.Bounds}; " +
                $"hit path={string.Join(" -> ", hit?.GetSelfAndVisualAncestors().Select(item => item.GetType().FullName) ?? [])}");
            var previousExecution = retry.ExecutionTask;

            HeadlessClick.Click(walkthrough.Window, button, MotifAppStrings.CheckTheGrammarAgainAccessibleName);

            var execution = retry.ExecutionTask;
            Assert.NotNull(execution);
            Assert.NotSame(previousExecution, execution);
            await execution!;

            var refusal = page.Grammar.Refusal;
            Assert.True(refusal is null, refusal is null ? null : $"{refusal.Code}: {refusal.Message}");
            Assert.True(page.Grammar.HasChecked);
            Assert.False(page.Grammar.IsLoading);
            Assert.True(page.Grammar.HasBaseline);
            Assert.Contains(page.Grammar.Warnings.Findings,
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
            var overview = workspace.PageModel<OverviewPageModel>().Overview;
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
