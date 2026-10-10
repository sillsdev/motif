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
using SIL.Motif.Help;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves the Parsimony page in the states it can be in with Advanced AI mode on: no stored Report, a listed Report with
/// a selected finding and its evidence, an incomplete Report, a Report from an earlier Baseline, a refusal, and the
/// decision states: a staged keep, a Suppressed decision with and without a reason, and a finding whose evidence changed
/// and came back to Active. Each state is driven through the page's own read and actions. Like
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
        foreach (var state in States)
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
                    await DriveAsync(state, page);
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

    private static readonly string[] States =
    [
        "empty", "listed-evidence", "incomplete", "earlier-baseline", "refused",
        "pending-keep", "suppressed-with-reason", "suppressed-without-reason", "resurfaced",
    ];

    // Each decision state is reached through the page's own selection and action, not by setting its fields.
    private static async Task DriveAsync(string state, ParsimonyPageModel page)
    {
        switch (state)
        {
            case "listed-evidence":
                Assert.True(page.SelectFinding(page.Rows.First(row => !row.IsHeader).Link!), page.SelectionMessage);
                break;
            case "pending-keep":
                Assert.True(page.SelectFinding(page.Rows.First(row => !row.IsHeader).Link!), page.SelectionMessage);
                page.ReasonText = "Attested in the field notes.";
                await page.KeepCommand.ExecuteAsync(null);
                break;
            case "suppressed-with-reason":
            case "suppressed-without-reason":
                page.IsShowingSuppressed = true;
                Assert.True(page.SelectSuppressed(page.SuppressedItems[0].Key));
                break;
            case "resurfaced":
                Assert.True(page.SelectFinding(page.Rows.First(row => !row.IsHeader).Link!), page.SelectionMessage);
                break;
        }
    }

    internal static void Configure(FakeCommandClient fake, string state)
    {
        var current = PageScreenshots.Token();
        if (state is "empty" or "refused")
        {
            ConfigureNoReport(fake, state);
            return;
        }
        var report = ParsimonySampleReports.Listed(
            state == "earlier-baseline" ? EarlierToken() : current,
            incomplete: state == "incomplete");
        fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Success(
            new ParsimonyLatestReportResponse(report.ReportId, report.Inputs.BundleId))));
        fake.OnReadParsimonyReport((_, _) => Task.FromResult(CommandOutcome<ParsimonyReportResponse>.Success(report)));
        ServeViews(fake, report, state);
    }

    // The live views the page reads: each Report finding is Active, except the ones a decision state moves.
    private static void ServeViews(FakeCommandClient fake, ParsimonyReportResponse report, string state)
    {
        var findings = report.Findings.ToArray();
        var active = new List<ParsimonyViewRow>();
        var suppressed = new List<ParsimonyViewRow>();
        for (var index = 0; index < findings.Length; index++)
        {
            var finding = findings[index];
            if (index == 0 && state is "suppressed-with-reason")
                suppressed.Add(History(finding, "keep", "Checked against the field notes."));
            else if (index == 0 && state is "suppressed-without-reason")
                suppressed.Add(History(finding, "defer", null));
            else if (index == 0 && state is "resurfaced")
                active.Add(new ParsimonyFindingDispositionViewRow(report.ReportId, report.Inputs.BundleId, "resurfaced",
                    finding, "keep", "Checked against the field notes.", null, "judgment-" + finding.FindingId,
                    "revision-" + finding.FindingId, "sha256:" + new string('9', 64), "evidence-changed", null, null));
            else
                active.Add(new ParsimonyFindingDispositionViewRow(report.ReportId, report.Inputs.BundleId, "active",
                    finding, null, null, null, null, null, null));
        }
        fake.ParsimonyViewHandler = (request, _) => Task.FromResult(CommandOutcome<ParsimonyNamedViewResponse>.Success(
            new ParsimonyNamedViewResponse(report.Inputs.BundleId, request.Query.View, 1,
                ParsimonyMeasureStatus.Computed, [], 0, 0, null, false,
                request.Query.View == "parsimony-suppressed" ? suppressed : active, null, null)));
        fake.RecordDispositionHandler = (request, _) => Task.FromResult(CommandOutcome<ComposedOperationsResponse>.Success(
            new ComposedOperationsResponse(request.DraftName, "RecordParsimonyDisposition", [], 1)));
    }

    private static ParsimonySuppressionHistoryViewRow History(ParsimonyFinding finding, string disposition, string? reason) =>
        new("judgment-" + finding.FindingId, "revision-" + finding.FindingId, "record-" + finding.FindingId,
            "sha256:" + new string('8', 64), finding.MeasureId, "subject-" + finding.FindingId, "the prohibition",
            ParsimonyRecipeCatalog.Load().Find(finding.MeasureId)?.Title ?? "Parsimony check", finding.EvidenceDigest,
            disposition, reason, "suppressed", null, null, null, "project", null, null, null, null, null);

    private static void ConfigureNoReport(FakeCommandClient fake, string state)
    {
        switch (state)
        {
            case "empty":
                fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Refused(
                    new Refusal("parsimony.no-report", FailureReason.NotFound, "No Parsimony Report yet."))));
                break;
            default:
                fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Refused(
                    new Refusal("parsimony.report-damaged", FailureReason.StoreInconsistent,
                        "Parsimony Report is damaged: the stored findings are incomplete."))));
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
        "pending-keep" => "a finding kept with a reason: the row shows the decision as staged, not yet applied",
        "suppressed-with-reason" => "the Suppressed tab lists a kept finding with its reason",
        "suppressed-without-reason" => "the Suppressed tab lists a deferred finding; the page says no reason was given",
        "resurfaced" => "a kept finding whose evidence changed is back in Active with its earlier reason",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
