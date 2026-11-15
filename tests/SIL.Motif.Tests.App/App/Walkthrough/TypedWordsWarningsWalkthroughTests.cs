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
            SetupWalkthroughActions.ClickSetupButton(walkthrough, SIL.Motif.App.MotifAppStrings.SetupNextTexts);
            walkthrough.Type("Words to add", "trois\nchat\nchats\ntrès\nfenêtre\nfenêtres");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, SIL.Motif.App.MotifAppStrings.SetupNextLimits);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, SIL.Motif.App.MotifAppStrings.SetupNextFirstRun);
            walkthrough.Click(SIL.Motif.App.MotifAppStrings.StartFirstRun);
            walkthrough.WaitUntil(() => walkthrough.Workspace.Assess.State is RunState.Completed or RunState.Refused,
                TimeSpan.FromMinutes(2), "the typed Selection did not finish parsing");
            Assert.Equal(RunState.Completed, walkthrough.Workspace.Assess.State);
            Assert.Contains("0 refused", walkthrough.Workspace.Assess.Result!.CompletionSummary);
            walkthrough.WaitUntilProjectIsQuiet(TimeSpan.FromMinutes(1), "parse results did not finish publishing");
            walkthrough.Workspace.Context.Diagnostics = true;
            walkthrough.ShowPage(WorkspacePage.Diagnostics);
            var diagnostics = walkthrough.Workspace.PageModel<DiagnosticsPageModel>();
            var warnings = diagnostics.Warnings.Grammar.Warnings;
            walkthrough.WaitUntil(() => diagnostics.Warnings.Rows.Count > 0, TimeSpan.FromSeconds(30),
                "the stored warnings did not finish loading");
            Assert.DoesNotContain(warnings.Findings, finding => finding.Code == GrammarFindingCodes.ParseAllomorphUnsegmentable);
            var letterFindings = warnings.Findings.Where(finding => finding.Code == ProvisionalLetter).ToArray();
            Assert.All(letterFindings, finding => Assert.Equal(GrammarDiagnosticLevel.Information, finding.Severity));
            Assert.Equal(NoTextsWarningProject.UndeclaredLetters.Order(StringComparer.Ordinal),
                letterFindings.Select(Letter).Order(StringComparer.Ordinal));
            var row = Assert.Single(diagnostics.Warnings.Rows,
                item => item.GroupCode == ProvisionalLetter);
            Assert.Equal(letterFindings.Length, row.RepeatCount);
            Assert.Equal(row.RepeatCount, row.Details.Count);
            Assert.Equal(warnings.Findings.Count, warnings.TotalCount);
            Assert.Equal(WarningDisplayState.SpellingCandidates, row.AttributionState);
            Assert.Equal("None of your words", row.LineSummaryText);
            Assert.Equal("5 spelling matches", row.SpellingCandidatesText);
            Assert.Equal(["chat", "chats", "fenêtre", "fenêtres", "très"],
                row.SpellingCandidateRows.Select(word => word.Word).Order(StringComparer.Ordinal));
            warnings.OpenDetailsCommand.Execute(row);
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

            walkthrough.ShowPage(WorkspacePage.Progress);
            Assert.Contains(warnings.Findings, finding => finding.Severity == GrammarDiagnosticLevel.Error);
            Assert.Equal(0, walkthrough.Workspace.Context.Health.Overview!.SelectionTextCount);

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
