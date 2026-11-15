using SIL.Motif.App;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the project actions and recorded diagnostic rows at narrow window widths.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class NarrowWindowLayoutTests
{
    [Fact]
    public void ProjectActionsAndDiagnosticsFitTheirNarrowLayouts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            try
            {
                var failures = new List<string>();
                foreach (var width in new[] { 760, 640 })
                    Check($"Project chip and Refresh at {width}", () =>
                    {
                        window.Width = width;
                        PageScreenshots.Settle(window);
                        var topBar = window.FindInWindowOrWorkspace<Border>("TopBar")!;
                        var chip = window.FindInWindowOrWorkspace<Button>("ProjectChip")!;
                        var refresh = window.FindInWindowOrWorkspace<Button>("RefreshIconButton")!;
                        var barRight = topBar.TranslatePoint(new Point(topBar.Bounds.Width - topBar.Padding.Right, 0), window)!.Value.X;
                        var chipRight = chip.TranslatePoint(new Point(chip.Bounds.Width, 0), window)!.Value.X;
                        var refreshLeft = refresh.TranslatePoint(default, window)!.Value.X;
                        var refreshRight = refresh.TranslatePoint(new Point(refresh.Bounds.Width, 0), window)!.Value.X;
                        Assert.True(chipRight <= refreshLeft + 0.5, $"The chip ends at {chipRight}, past Refresh at {refreshLeft}.");
                        Assert.True(refreshRight <= barRight + 0.5, $"Refresh ends at {refreshRight}, past the top bar's content at {barRight}.");
                        Assert.Equal(width >= 760, window.FindInWindowOrWorkspace<TextBlock>("ProjectChipDetail")!.IsEffectivelyVisible);
                    });
                workspace.Context.Diagnostics = true;
                workspace.Context.OpenDiagnostics(SIL.Motif.App.ViewModels.DiagnosticsTab.SpeedByRule);
                await workspace.Context.EvidencePublication;
                Check("Diagnostics at 640", () =>
                {
                    window.Width = 640;
                    window.Height = 600;
                    PageScreenshots.Settle(window);
                    var page = window.PageView<SIL.Motif.App.Views.DiagnosticsPage>();
                    var rows = page.FindControl<ListBox>("DiagnosticRuleRows")!;
                    Assert.True(rows.IsEffectivelyVisible);
                    Assert.True(rows.Bounds.Width > 0 && rows.Bounds.Width <= page.Bounds.Width);
                    LayoutAssertions.AssertCurrent(page);
                });
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

                void Check(string scene, Action inspect)
                {
                    var failure = Record.Exception(inspect);
                    if (failure is not null) failures.Add($"{scene}: {failure}");
                }
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(120));
    }
}
