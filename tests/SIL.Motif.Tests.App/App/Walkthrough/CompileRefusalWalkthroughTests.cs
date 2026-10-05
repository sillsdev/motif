using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class CompileRefusalWalkthroughTests(PristineProjectFixture pristine)
{
    private static readonly string Failure = File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(),
        "tests", "SIL.Motif.Tests.Commands", "Fixtures", "compile-refusal.txt"), new UTF8Encoding(false, true)).TrimEnd();

    private const string StructuredFailure = """
        importing project
        {"schema_version":1,"status":"compile_error","path":"sample.fwdata","message":"Cannot compile","issues":[{"code":"grammar.environment.unresolved","kind":"invalidSource","object_guid":"00000000-0000-0000-0000-000000000042","object_kind":"MoForm","field":"PhoneEnv","text":"environment does not resolve","advice":"Repair the attachment in FieldWorks.","fatal":true}]}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedGrammarIsExplainedAcrossPagesAndReopeningUntilAParseSucceeds(bool structured)
    {
        var failure = structured ? StructuredFailure : Failure;
        if (!structured)
        {
            Assert.DoesNotContain("\uFFFD", failure);
            Assert.Contains("Allomorph 'á(k)'", failure);
            Assert.Contains("Allomorph 'íé(k)'", failure);
        }
        var prefix = structured ? "compile-refusal-json" : "compile-refusal";
        var explanation = structured ? "environment does not resolve" : "must contain exactly one";
        using var project = new WalkthroughProject(pristine);
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(project.ManagedRoot, "parser"));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(parser)!, "_fake-pangloss-legacy-console"), string.Empty);
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using (var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser))
            {
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(window, deadline);
                FakeParser.BehaveBesideExecutable(parser, new { mode = "fail", standardError = failure });
                window.ConfigureFromProjectMenu();
                SetupWalkthroughActions.ClickSetupButton(window, "Next: texts");
                window.Type("Words to add", "motifa");
                SetupWalkthroughActions.ClickSetupButton(window, "Next: limits");
                SetupWalkthroughActions.ClickSetupButton(window, "Next: first run");
                window.Click("Start first run");
                window.WaitUntil(() => window.Workspace.Assess.State == RunState.Refused &&
                    window.Workspace.Context.EvidencePublication.IsCompleted,
                    WalkthroughSteps.Remaining(deadline), "the compile refusal did not reach the pages");
                window.SkipSetup();
                var refusal = window.Workspace.Assess.ShownRefusal!;
                Assert.Contains(structured ? "1 fatal issue" : "5 environments", refusal.Sentence);
                Assert.Equal(structured ? 1 : 5, refusal.CompileIssues.Count);
                Assert.Contains(explanation, refusal.Details);
                Assert.DoesNotContain("ConversionError", refusal.Details);
                if (structured)
                {
                    Assert.Contains("PhoneEnv", refusal.Details);
                    Assert.Contains("00000000-0000-0000-0000-000000000042", refusal.Details);
                    Assert.Contains("Repair the attachment in FieldWorks.", refusal.Details);
                }
                Assert.Equal(failure, refusal.ParserDiagnostic!.RawText.TrimEnd());
                Assert.Contains(explanation, ProblemReport.FromRefusal(refusal).ToText(true));
                Assert.DoesNotContain("ConversionIssue", ProblemReport.FromRefusal(refusal).ToText(true));

                window.ShowPage(WorkspacePage.Overview);
                Assert.Contains("can't use this grammar", window.Workspace.PageModel<OverviewPageModel>().LastParseRefusal!.Sentence);
                Capture(window, prefix + "-overview");
                window.ShowPage(WorkspacePage.Texts);
                Capture(window, prefix + "-texts");
                window.ShowPage(WorkspacePage.Warnings);
                var grammar = window.Workspace.PageModel<WarningsPageModel>().Grammar;
                Assert.Equal(refusal.Sentence, grammar.ShownRefusal!.Sentence);
                Assert.Equal(grammar.ShownRefusal.Sentence, grammar.SummaryText);
                Capture(window, prefix + "-warnings");

                window.ShowPage(WorkspacePage.AiHandoff);
                var handoff = window.Workspace.PageModel<AiHandoffPageModel>().Handoff;
                Assert.False(handoff.RunCommand.CanExecute(null));
                Assert.Contains("No parse results yet: PanGloss can't use this grammar", handoff.UnavailableReason);
                Assert.DoesNotContain("This folder holds", HandoffViewModel.DataSensitivitySentence);
                Capture(window, prefix + "-handoff");

                var trace = window.Workspace.Assess.Trace;
                trace.SetWord("motifa");
                await trace.TryCommand.ExecuteAsync(null);
                Assert.Contains("can't use this grammar", trace.ShownRefusal!.Sentence);
                Assert.DoesNotContain("could not parse this word", trace.ShownRefusal.Sentence);
                window.ShowPage(WorkspacePage.TryAWord);
                Capture(window, prefix + "-try-word");
            }

            using (var reopened = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser))
            {
                reopened.Show();
                await reopened.Workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(project.FwDataPath));
                Assert.NotNull(reopened.Workspace.Assess.ShownRefusal);
                Assert.NotNull(reopened.Workspace.PageModel<OverviewPageModel>().LastParseRefusal);
                Assert.NotNull(reopened.Workspace.PageModel<WarningsPageModel>().Grammar.ShownRefusal);
                Capture(reopened, prefix + "-reopened");
                FakeParser.BehaveBesideExecutable(parser, new { mode = "success" });
                var result = AssessCommand.Assess(new AssessRequest(project.FwDataPath), project.ManagedRoot, parser, null,
                    CancellationToken.None);
                Assert.True(result.Succeeded, result.Refusal?.Message);
                await reopened.Workspace.Context.ReadStoredEvidenceAsync();
                Assert.Null(reopened.Workspace.Assess.ShownRefusal);
                Assert.Null(reopened.Workspace.PageModel<OverviewPageModel>().LastParseRefusal);
                Assert.True(reopened.Workspace.PageModel<AiHandoffPageModel>().Handoff.RunCommand.CanExecute(null));
                Assert.Null(CurrentEvidenceQuery.ReadCurrentEvidence(project.FwDataPath).Value!.LastParserRefusal);
            }
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static void Capture(WalkthroughWindow window, string name)
    {
        PageScreenshots.Settle(window.Window);
        Assert.Contains(window.Window.GetLogicalDescendants().OfType<TextBlock>(), text =>
            text.IsEffectivelyVisible && text.Text is { } shown && shown.Contains("can't use this grammar", StringComparison.Ordinal));
        if (name.EndsWith("-texts", StringComparison.Ordinal) || name.EndsWith("-try-word", StringComparison.Ordinal))
        {
            var details = Assert.Single(window.Window.GetVisualDescendants().OfType<Expander>(),
                expander => expander.IsEffectivelyVisible && Equals(expander.Header, "Details · grammar issues"));
            var position = details.TranslatePoint(default, window.Window)!.Value;
            Assert.True(position.Y + details.Bounds.Height <= window.Window.ClientSize.Height,
                $"The grammar issue list put Details below the window on {name}.");
            details.IsExpanded = true;
            PageScreenshots.Settle(window.Window);
            Assert.All(window.Window.GetLogicalDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible), text =>
            {
                Assert.DoesNotContain("ConversionError", text.Text ?? string.Empty);
                Assert.DoesNotContain("ConversionIssue", text.Text ?? string.Empty);
            });
        }
        if (Environment.GetEnvironmentVariable("MOTIF_SCREENSHOTS") is { Length: > 0 } root)
        {
            Directory.CreateDirectory(root);
            PageScreenshots.Save(window.Window, Path.Combine(root, name + ".png"));
        }
    }
}
