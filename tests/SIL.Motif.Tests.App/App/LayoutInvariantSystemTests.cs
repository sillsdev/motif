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
    public void ResizeAndZoomTransitionsPreserveDiagnosticsHitTargets()
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
                        client.StoredGrammarCheckIs(new GrammarCheckResponse([], true));
                    });
                try
                {
                    workspace.Context.Diagnostics = true;
                    workspace.Context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);
                    PageScreenshots.SettleLayout(window);
                    LayoutInvariantFixtures.RequirePage(window, workspace, WorkspacePage.Diagnostics);
                    foreach (var cell in new LayoutCell[]
                    {
                        new(960, 780, 150), new(1920, 1080, 100),
                        new(1920, 1080, 150), new(1600, 480, 100),
                    })
                        run.Inspect(window, "Diagnostics/empty", cell, quick: false, workspace);
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
                                client.StoredGrammarCheckIs(new GrammarCheckResponse([], true));
                            }
                        });
                    try
                    {
                        if (populated) await LayoutInvariantFixtures.PreparePopulated(workspace);
                        workspace.Context.Diagnostics = true;
                        run.Setup(populated ? "populated" : "empty", setup.Elapsed.TotalMilliseconds);
                        foreach (var page in LayoutInvariantFixtures.RegisteredPages)
                        {
                            workspace.CurrentPage = page;
                            PageScreenshots.SettleLayout(window);
                            LayoutInvariantFixtures.RequirePage(window, workspace, page);
                            InspectGrid(LayoutInvariantFixtures.Scene(page, populated));
                        }
                        run.AssertInspected(populated ? LayoutInvariantFixtures.PopulatedPages
                            .Select(page => LayoutInvariantFixtures.Scene(page, true)) : LayoutInvariantFixtures.EmptyScenes);
                        if (!populated) continue;
                        workspace.CurrentPage = WorkspacePage.Progress;
                        InspectGrid("Progress/sidebar-collapsed", () =>
                        {
                            workspace.IsSidebarCollapsed = true;
                            Assert.True(workspace.IsSidebarCollapsed);
                        });
                        var refusal = new Refusal(RefusalCodes.StoreOtherVersion, FailureReason.Refused,
                            "Store belongs to another version.");
                        await LayoutInvariantStateFixtures.VisitAsync(fake, workspace, SettingsTestAccess.Model(window), scene => InspectGrid(scene));
                        workspace.CurrentPage = WorkspacePage.Progress;
                        fake.CurrentBaselineRefusesWith(refusal);
                        fake.CaptureBaselineRefusesWith(refusal);
                        await workspace.RefreshCommand.ExecuteAsync(null);
                        Assert.True(workspace.HasRefreshRefusal);
                        InspectGrid("Progress/refusal");

                    }
                    finally { window.Close(); await workspace.DisposeAsync(); }

                    void InspectGrid(string scene, Action? prepare = null)
                    {
                        foreach (var cell in LayoutInvariantFixtures.GridCells)
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
