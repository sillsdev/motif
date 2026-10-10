using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
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
    public void TypedWordsWithoutTextsKeepEveryProvisionalLetterAndHonestWordCounts()
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
            Assert.Contains("0 refused", walkthrough.Workspace.Assess.Result!.CompletionSummary);
            walkthrough.WaitUntilProjectIsQuiet(TimeSpan.FromMinutes(1), "parse results did not finish publishing");
            walkthrough.ShowPage(WorkspacePage.Warnings);
            var warnings = walkthrough.Workspace.PageModel<WarningsPageModel>().Grammar.Warnings;
            Assert.DoesNotContain(warnings.Findings, finding => finding.Code == GrammarFindingCodes.ParseAllomorphUnsegmentable);
            var letterFindings = warnings.Findings.Where(finding => finding.Code == ProvisionalLetter).ToArray();
            Assert.All(letterFindings, finding => Assert.Equal(GrammarDiagnosticLevel.Information, finding.Severity));
            Assert.Equal(NoTextsWarningProject.UndeclaredLetters.Order(StringComparer.Ordinal),
                letterFindings.Select(Letter).Order(StringComparer.Ordinal));
            var row = Assert.Single(warnings.Rows.Cast<GrammarWarningRowViewModel>(),
                item => item.GroupCode == ProvisionalLetter);
            Assert.Equal(letterFindings.Length, row.RepeatCount);
            Assert.Equal(row.RepeatCount, row.Details.Count);
            Assert.Equal(warnings.Findings.Count, warnings.TotalCount);
            Assert.Equal(WarningDisplayState.SpellingCandidates, row.AttributionState);
            Assert.Equal("None of your words", row.LineSummaryText);
            Assert.Equal("5 spelling matches", row.SpellingCandidatesText);
            Assert.Equal(["chat", "chats", "fenêtre", "fenêtres", "très"],
                row.SpellingCandidateRows.Select(word => word.Word).Order(StringComparer.Ordinal));
            row.ToggleOpenCommand.Execute(null);
            PageScreenshots.Settle(walkthrough.Window);
            var visibleText = walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
            Assert.Contains(row.SpellingCandidatesText, visibleText);
            Assert.Contains(row.ReachStateText, visibleText);
            var groups = Assert.Single(walkthrough.Window.GetVisualDescendants().OfType<ItemsControl>(),
                control => control.Name == "GrammarPanelRowsItems");
            var scroll = groups.GetVisualAncestors().OfType<ScrollViewer>().First();
            var descriptions = letterFindings.Select(finding => finding.Description).ToHashSet(StringComparer.Ordinal);
            var expectedLetters = NoTextsWarningProject.UndeclaredLetters.ToHashSet(StringComparer.Ordinal);
            foreach (var height in new[] { walkthrough.Window.Height, walkthrough.Window.MinHeight })
            {
                walkthrough.Window.Height = height;
                scroll.Offset = default;
                PageScreenshots.Settle(walkthrough.Window);
                var readableDescriptions = new HashSet<string>(StringComparer.Ordinal);
                var reachedLetters = new HashSet<string>(StringComparer.Ordinal);
                var direction = 1;
                walkthrough.WaitUntil(() =>
                {
                    foreach (var text in groups.GetVisualDescendants().OfType<TextBlock>()
                        .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("warningRawMessage") &&
                            text.Text is not null && descriptions.Contains(text.Text)))
                    {
                        var origin = text.TranslatePoint(default, scroll)!.Value;
                        if (origin.Y < 0 || origin.Y + text.Bounds.Height > scroll.Viewport.Height) continue;
                        Assert.True(text.Bounds.Width > 0 && text.Bounds.Height > 0);
                        Assert.True(origin.X >= 0 && origin.X + text.Bounds.Width <= scroll.Viewport.Width + 1,
                            $"Warning description extends past the viewport: {text.Text}");
                        var detail = Assert.IsType<GrammarWarningDetailViewModel>(text.DataContext);
                        var letter = Letter(detail.Warning);
                        Assert.Contains(letter, expectedLetters);
                        reachedLetters.Add(letter);
                        readableDescriptions.Add(text.Text!);
                    }
                    if (reachedLetters.SetEquals(expectedLetters) &&
                        readableDescriptions.SetEquals(descriptions)) return true;
                    if (scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height) direction = -1;
                    if (scroll.Offset.Y <= 0) direction = 1;
                    scroll.Offset = scroll.Offset.WithY(scroll.Offset.Y + direction * scroll.Viewport.Height / 4);
                    PageScreenshots.Settle(walkthrough.Window);
                    return false;
                }, TimeSpan.FromMinutes(1), "not every provisional letter became readable by scrolling",
                    () => $"Reached {reachedLetters.Count}/{row.RepeatCount} at height {height}; " +
                        $"missing '{string.Join("; ", descriptions.Except(readableDescriptions))}'; " +
                        $"scroll offset={scroll.Offset.Y}, extent={scroll.Extent.Height}, viewport={scroll.Viewport.Height}");
                Assert.Equal(row.RepeatCount, reachedLetters.Count);
                foreach (var finding in letterFindings)
                {
                    Assert.Contains(finding.Description, readableDescriptions);
                    Assert.Contains($"The letter '{finding.Subject[0].Title}' isn't defined", finding.Description);
                }
            }
            foreach (var (letter, spelled) in new[]
                { ("c", "chat chats"), ("h", "chat chats"), ("è", "très"), ("ê", "fenêtre fenêtres") })
            {
                var finding = Assert.Single(letterFindings, item => Letter(item) == letter);
                Assert.Equal(WarningAttributionState.SpellingCandidates, finding.AttributionState);
                Assert.Equal(WarningWordsMatch.Spelling, finding.YourWords!.Match);
                Assert.Equal(spelled.Split(' '), finding.YourWords.Words.Select(word => Nfc(word.Row.Word)).Order(StringComparer.Ordinal));
            }
            foreach (var letter in new[] { "d", "é", "-" })
                Assert.Equal(WarningAttributionState.NoneInSelection,
                    Assert.Single(letterFindings, item => Letter(item) == letter).AttributionState);
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

    private const string ProvisionalLetter = "provisional.letter";

    private static string Letter(GrammarWarning finding) => Nfc(Assert.Single(finding.Subject).Title!);

    private static string Nfc(string text) => text.Normalize(System.Text.NormalizationForm.FormC);
}
