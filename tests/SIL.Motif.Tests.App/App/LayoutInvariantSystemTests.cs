using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class LayoutInvariantSystemTests(ITestOutputHelper output)
{
    [Fact]
    public void ResizeAndZoomTransitionsPreserveTimingHitTargets()
    {
        using var run = new LayoutInvariantRun("resize-zoom", output);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var app = Application.Current!;
            var theme = app.RequestedThemeVariant;
            var zoom = (int)Math.Round(WindowZoomPolicy.Transform.ScaleX * 100);
            app.RequestedThemeVariant = ThemeVariant.Light;
            try
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                    capturedWords: new TextWordsResponse([], [], true),
                    advancedAiModeEnabled: true,
                    assistantConnections: null,
                    configure: (client, _) =>
                    {
                        ParsimonyStateScreenshots.Configure(client, "empty");
                        client.StoredGrammarCheckIs(new GrammarCheckResponse([], true));
                    });
                try
                {
                    workspace.CurrentPage = WorkspacePage.Timing;
                    LayoutInvariantEngineTests.Settle(window);
                    LayoutInvariantFixtures.RequirePage(window, workspace, WorkspacePage.Timing);
                    foreach (var cell in new LayoutCell[]
                    {
                        new(960, 780, 150), new(1920, 1080, 100),
                        new(1920, 1080, 150), new(1600, 480, 100),
                    })
                        run.Inspect(window, "Timing/empty", cell, quick: false, workspace);
                }
                finally { window.Close(); await workspace.DisposeAsync(); }
            }
            finally { app.RequestedThemeVariant = theme; WindowZoomPolicy.Apply(app, zoom); }
        }, TimeSpan.FromMinutes(1));
        run.AssertFindings();
    }

    [Fact]
    public void EveryPageAndApplicableStateSatisfiesTheFullLightGrid()
    {
        using var run = new LayoutInvariantRun("full", output);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var app = Application.Current!;
            var theme = app.RequestedThemeVariant;
            var zoom = (int)Math.Round(WindowZoomPolicy.Transform.ScaleX * 100);
            app.RequestedThemeVariant = ThemeVariant.Light;
            try
            {
                foreach (var populated in new[] { false, true })
                {
                    FakeCommandClient fake = null!;
                    var setup = Stopwatch.StartNew();
                    var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: populated,
                        capturedWords: populated ? null : new TextWordsResponse([], [], true),
                        advancedAiModeEnabled: true,
                        assistantConnections: populated ? new AssistantConnectionsPageTests.FakeAssistantConnections() : null,
                        configure: (client, _) =>
                        {
                            fake = client;
                            if (populated) LayoutInvariantFixtures.ConfigurePopulated(client);
                            else
                            {
                                ParsimonyStateScreenshots.Configure(client, "empty");
                                client.StoredGrammarCheckIs(new GrammarCheckResponse([], true));
                            }
                        });
                    try
                    {
                        if (populated) await LayoutInvariantFixtures.PreparePopulated(workspace);
                        run.Setup(populated ? "populated" : "empty", setup.Elapsed.TotalMilliseconds);
                        foreach (var page in PageRegistry.Entries.Select(entry => entry.Page))
                        {
                            workspace.CurrentPage = page;
                            LayoutInvariantEngineTests.Settle(window);
                            LayoutInvariantFixtures.RequirePage(window, workspace, page);
                            InspectGrid($"{page}/{(populated ? "populated" : "empty")}");
                        }
                        if (!populated) continue;
                        workspace.CurrentPage = WorkspacePage.Timing;
                        workspace.Context.OpenInspector(InspectorSubject.Morpheme("a-form", "msa-a", "a-")!);
                        await workspace.Inspector.Loading;
                        InspectGrid("Timing/inspector-open", () =>
                        {
                            ShowObjectInspector();
                            Assert.True(window.FindControl<Inspector>("Inspector")!.IsEffectivelyVisible);
                        });
                        workspace.Inspector.Close();
                        workspace.CurrentPage = WorkspacePage.Overview;
                        InspectGrid("Overview/sidebar-collapsed", () =>
                        {
                            workspace.IsSidebarCollapsed = true;
                            Assert.True(workspace.IsSidebarCollapsed);
                        });
                        var refusal = new Refusal(RefusalCodes.StoreOtherVersion, FailureReason.Refused,
                            "Store belongs to another version.");
                        workspace.CurrentPage = WorkspacePage.Parsimony;
                        ParsimonyStateScreenshots.Configure(fake, "refused");
                        await workspace.PageModel<ParsimonyPageModel>().ReloadAsync();
                        Assert.True(workspace.PageModel<ParsimonyPageModel>().HasMessage);
                        InspectGrid("Parsimony/refused");
                        var pending = new TaskCompletionSource<CommandOutcome<ParsimonyLatestReportResponse>>();
                        fake.OnReadLatestParsimony((_, _) => pending.Task);
                        var loading = workspace.PageModel<ParsimonyPageModel>().ReloadAsync();
                        try
                        {
                            Assert.True(workspace.PageModel<ParsimonyPageModel>().IsLoading);
                            InspectGrid("Parsimony/loading");
                        }
                        finally { pending.SetResult(CommandOutcome<ParsimonyLatestReportResponse>.Refused(refusal)); await loading; }

                        workspace.CurrentPage = WorkspacePage.TryAWord;
                        var tracePending = new TaskCompletionSource<CommandOutcome<WordTraceResponse>>();
                        fake.OnTraceWord((_, _) => tracePending.Task);
                        var tracing = workspace.Assess.Trace.TryCommand.ExecuteAsync(null);
                        try
                        {
                            Assert.True(workspace.Assess.Trace.IsLoading);
                            InspectGrid("TryAWord/loading");
                        }
                        finally { tracePending.SetResult(CommandOutcome<WordTraceResponse>.Refused(refusal)); await tracing; }
                        Assert.NotNull(workspace.Assess.Trace.ShownRefusal);
                        InspectGrid("TryAWord/refused");
                        await LayoutInvariantStateFixtures.VisitAsync(fake, workspace, scene => InspectGrid(scene));
                        workspace.CurrentPage = WorkspacePage.Overview;
                        fake.CurrentBaselineRefusesWith(refusal);
                        fake.CaptureBaselineRefusesWith(refusal);
                        await workspace.RefreshCommand.ExecuteAsync(null);
                        Assert.True(workspace.HasRefreshRefusal);
                        InspectGrid("Overview/refusal");

                    }
                    finally { window.Close(); await workspace.DisposeAsync(); }

                    // A narrow frame leaves an opened object behind the Inspector strip until a person reveals it.
                    void ShowObjectInspector()
                    {
                        LayoutInvariantEngineTests.Settle(window);
                        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        LayoutInvariantEngineTests.Settle(window);
                        window.FindControl<SIL.Motif.App.Views.Shell.WorkspaceLayoutHost>("WorkspaceLayout")!.ShowInspector();
                        LayoutInvariantEngineTests.Settle(window);
                        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        LayoutInvariantEngineTests.Settle(window);
                    }

                    void InspectGrid(string scene, Action? prepare = null)
                    {
                        prepare ??= scene.Contains("/inspector-", StringComparison.Ordinal) ? ShowObjectInspector : null;
                        foreach (var cell in LayoutInvariantFixtures.Cells)
                        {
                            run.Inspect(window, scene, cell, quick: false, workspace, prepare);
                        }
                    }
                }
            }
            finally { app.RequestedThemeVariant = theme; WindowZoomPolicy.Apply(app, zoom); }
        }, TimeSpan.FromMinutes(10));
        run.AssertFindings();
    }
}
