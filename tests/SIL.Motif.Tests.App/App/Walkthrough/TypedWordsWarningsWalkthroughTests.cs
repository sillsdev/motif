using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class TypedWordsWarningsWalkthroughTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public void TypedWordsWithoutTextsKeepAllParserAllomorphsAndHonestWordCounts()
    {
        using var project = new NoTextsWarningProject(pristine);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            SetupWalkthroughActions.SelectProject(walkthrough, project.FwDataPath);
            SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(walkthrough, 0, TimeSpan.FromMinutes(2));
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            walkthrough.Type("Words to add", "trois\nchat\nchats\ntrès\nfenêtre\nfenêtres");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
            walkthrough.Click("Start first run");
            walkthrough.WaitUntil(() => walkthrough.Workspace.Assess.State is RunState.Completed or RunState.Refused,
                TimeSpan.FromMinutes(2), "the typed Selection did not finish parsing");
            Assert.Equal(RunState.Completed, walkthrough.Workspace.Assess.State);
            walkthrough.WaitUntilProjectIsQuiet(TimeSpan.FromMinutes(1), "parse results did not finish publishing");
            walkthrough.ShowPage(WorkspacePage.Warnings);
            var warnings = walkthrough.Workspace.PageModel<WarningsPageModel>().Grammar.Warnings;
            var parseFindings = warnings.Findings.Where(finding => finding.Code == GrammarFindingCodes.ParseAllomorphUnsegmentable).ToArray();
            Assert.Equal(project.Allomorphs.Select(item => item.AllomorphId).Order(),
                parseFindings.Select(finding => Guid.Parse(Assert.Single(finding.Subject).SubjectGuid!)).Order());
            var row = Assert.Single(warnings.Rows.Cast<GrammarWarningRowViewModel>(),
                item => item.GroupCode == GrammarFindingCodes.ParseAllomorphUnsegmentable);
            Assert.Equal(15, row.RepeatCount);
            Assert.Equal(15, row.Details.Count);
            Assert.Equal(WarningDisplayState.SpellingCandidates, row.AttributionState);
            Assert.Equal("None of your words", row.LineSummaryText);
            Assert.Equal("5 spelling matches", row.SpellingCandidatesText);
            Assert.Equal(["chat", "chats", "fenêtre", "fenêtres", "très"],
                row.SpellingCandidateRows.Select(word => word.Word).Order(StringComparer.Ordinal));
            row.ToggleOpenCommand.Execute(null);
            walkthrough.Window.UpdateLayout();
            var visibleText = walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
            Assert.Contains(row.SpellingCandidatesText, visibleText);
            Assert.Contains(row.ReachStateText, visibleText);
            foreach (var finding in parseFindings)
            {
                Assert.Contains(finding.Description, visibleText);
                Assert.Contains("no character definition matches at position", finding.Description);
            }
            foreach (var form in new[] { "chat", "très", "fenêtre" })
            {
                var finding = Assert.Single(parseFindings, item => Assert.Single(item.Subject).Title?.Normalize(System.Text.NormalizationForm.FormC) == form);
                Assert.Equal(WarningAttributionState.SpellingCandidates, finding.AttributionState);
                Assert.Empty(finding.YourWords!.Words);
                Assert.NotEmpty(finding.YourWords.SpellingCandidates);
            }
            row.ToggleOpenCommand.Execute(null);
            var trois = Assert.Single(warnings.Findings, finding => finding.Code == "hc-stem-no-grammatical-category" &&
                finding.Subject.Any(subject => subject.SubjectGuid == project.TroisEntryId.ToString("D")));
            Assert.Equal(WarningAttributionState.ExactUses, trois.AttributionState);
            Assert.Equal("trois", Assert.Single(trois.YourWords!.Words).Row.Word);

            walkthrough.ShowPage(WorkspacePage.Overview);
            var overview = walkthrough.Workspace.PageModel<OverviewPageModel>();
            Assert.Contains(overview.WarningKindRows, row => row.WarningCount.EndsWith("error", StringComparison.Ordinal) ||
                row.WarningCount.EndsWith("errors", StringComparison.Ordinal));
            Assert.DoesNotContain("None of your words", overview.WarningsYourWordsText);
            Assert.Equal(0, overview.Overview!.SelectionTextCount);

            var commands = walkthrough.Workspace.Context.Commands;
            var stored = await commands.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(stored.Succeeded, stored.Refusal?.Message);
            Assert.Equal(warnings.Findings.Count, stored.Value!.Check!.Findings.Count);
        }, TimeSpan.FromMinutes(4));
    }
}
