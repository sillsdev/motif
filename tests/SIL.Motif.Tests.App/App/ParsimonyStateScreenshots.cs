using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves the Parsimony page in the states it can be in with Advanced AI mode on: no stored Report, a listed Report with
/// a selected finding and its evidence, an incomplete Report, a Report from an earlier Baseline, and a refusal. Each
/// state is driven through the page's own read and selection, and the capture waits on the page's state. Like
/// <see cref="PageScreenshots"/>, it runs only when <c>MOTIF_SCREENSHOTS</c> names a folder, and it writes
/// <c>state-parsimony-&lt;state&gt;-&lt;width&gt;-&lt;theme&gt;.png</c> plus <c>parsimony-states.txt</c>.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ParsimonyStateScreenshots(ITestOutputHelper output)
{
    private static readonly (string Name, ThemeVariant Variant)[] Themes =
        [("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark)];

    private static readonly int[] Widths = [1040, 1240];

    [ScreenshotFact]
    public void CaptureParsimonyPageStates()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        var notes = new List<string>();
        foreach (var state in new[] { "empty", "listed-evidence", "incomplete", "earlier-baseline", "refused" })
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                using var culture = new CultureScope(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(advancedAiModeEnabled: true,
                    configure: (fake, _) => Configure(fake, state));
                try
                {
                    workspace.CurrentPage = WorkspacePage.Parsimony;
                    var page = workspace.PageModel<ParsimonyPageModel>();
                    if (state == "listed-evidence")
                    {
                        var link = page.Rows.First(row => !row.IsHeader).Link!;
                        Assert.True(page.SelectFinding(link), page.SelectionMessage);
                    }
                    notes.Add($"parsimony {state}: {Describe(state)}");
                    foreach (var (theme, variant) in Themes)
                    foreach (var width in Widths)
                    {
                        Application.Current!.RequestedThemeVariant = variant;
                        window.Width = width;
                        PageScreenshots.Settle(window);
                        PageScreenshots.Save(window, Path.Combine(folder, $"state-parsimony-{state}-{width}-{theme}.png"));
                    }
                }
                finally
                {
                    window.Close();
                }
            }, TimeSpan.FromMinutes(2));
        }
        File.WriteAllLines(Path.Combine(folder, "parsimony-states.txt"), notes);
    }

    private static void Configure(FakeCommandClient fake, string state)
    {
        var current = PageScreenshots.Token();
        switch (state)
        {
            case "empty":
                fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Refused(
                    new Refusal("parsimony.no-report", FailureReason.NotFound, "No Parsimony Report yet."))));
                break;
            case "refused":
                fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Refused(
                    new Refusal("parsimony.report-damaged", FailureReason.StoreInconsistent,
                        "Parsimony Report is damaged: the stored findings are incomplete."))));
                break;
            default:
                var report = ParsimonySampleReports.Listed(
                    state == "earlier-baseline" ? EarlierToken() : current,
                    incomplete: state == "incomplete");
                fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Success(
                    new ParsimonyLatestReportResponse(report.ReportId, report.Inputs.BundleId))));
                fake.OnReadParsimonyReport((_, _) => Task.FromResult(CommandOutcome<ParsimonyReportResponse>.Success(report)));
                break;
        }
    }

    // The sample Baseline captured on an earlier day, so the page reads it as a different Baseline.
    private static BaselineToken EarlierToken() =>
        new("11111111-1111-1111-1111-111111111111", "sha256:" + new string('a', 64), "1", "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64));

    private static string Describe(string state) => state switch
    {
        "empty" => "no stored Parsimony Report; the page names the next step",
        "listed-evidence" => "two findings listed under their measure, the first selected with its evidence in the detail column",
        "incomplete" => "findings listed; one check is named under Not measured, and its findings are left out",
        "earlier-baseline" => "findings listed from an earlier Baseline, with a banner saying so",
        "refused" => "the stored Report is refused; the command's message is shown",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
