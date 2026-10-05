using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Details are on screen once expanded, so they keep to the window's words too; and every surface that shows
/// a failure, a refused open and a saved diagnostic included, shows it as a <see cref="WindowRefusal"/>.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WindowRefusalDetailsTests
{
    private const string DryRunMissingMessage =
        "Proposal proposal/1 has no bound DryRun recorded. Run 'dry-run proposal/1 --project <fwdata>' first, then 'apply'.";

    private static readonly string[] CliTerms = ["proposal", "draft", "preflight", "dry run", "dryrun", "dry-run"];
    private static readonly string ParserPath = Path.Combine(Path.GetTempPath(), "Motif",
        OperatingSystem.IsWindows() ? "pangloss.exe" : "pangloss");

    private readonly AvaloniaHeadlessFixture _avalonia;

    public WindowRefusalDetailsTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    public static TheoryData<string> CatalogueCodes()
    {
        var data = new TheoryData<string>();
        foreach (var field in typeof(RefusalCodes).GetFields())
            if (field.IsLiteral) data.Add((string)field.GetRawConstantValue()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogueCodes))]
    public void DetailsNeverCarryTheCliWordsInTheMessageOrTheFacts(string code)
    {
        var refusal = new Refusal(code, FailureReason.Refused,
            DryRunMissingMessage + " The pending Draft moved. Preflight failed. The project file is read-only.",
            new Dictionary<string, string>
            {
                ["proposalId"] = "proposal/1",
                ["draftId"] = "d-1",
                ["jobId"] = "draft/7",
                ["projectPath"] = Path.Combine(Path.GetTempPath(), "projects", "one.fwdata"),
            });

        var details = WindowRefusal.From(refusal).Details ?? string.Empty;

        AssertWindowWords(details);
        Assert.Contains("The project file is read-only.", details);
        Assert.Contains(Path.Combine(Path.GetTempPath(), "projects", "one.fwdata"), details);
    }

    [Fact]
    public void CompileFailureDetailsKeepTheExplanationWithoutTheDebugSerialization()
    {
        const string message = "pangloss batch exited 1: compile /tmp/assessments/source.fwdata: " +
            "Conversion(ConversionError { issues: [ConversionIssue { code: EnvironmentInvalid, " +
            "class: InvalidSource, source: None, fatal: true, message: \"environment string is invalid\" }] })";
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable,
            FailureReason.Refused, message, new Dictionary<string, string> { ["projectPath"] = "one.fwdata" }));

        Assert.Contains("environment string is invalid", shown.Details);
        Assert.Contains("environment string is invalid", ProblemReport.FromRefusal(shown).ToText(true));
        Assert.DoesNotContain("ConversionError", shown.Details);
        Assert.DoesNotContain("ConversionIssue", ProblemReport.FromRefusal(shown).ToText(true));
    }

    [Theory]
    [InlineData("Conversion(ConversionError { issues: [ConversionIssue { code:")]
    [InlineData("pangloss batch: compile one.fwdata: semantic conversion failed")]
    public void UnreadableCompileIssuesUseAPlainFallback(string output)
    {
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable,
            FailureReason.Refused, output, new Dictionary<string, string>
            {
                ["standardError"] = output, ["parserMessage"] = output, ["exitCode"] = "1",
            }));

        Assert.Empty(shown.CompileIssues);
        Assert.Contains("can't use this grammar", shown.Sentence);
        Assert.Contains("exitCode: 1", shown.Details);
        Assert.DoesNotContain(output, shown.Details);
        Assert.DoesNotContain(output, ProblemReport.FromRefusal(shown).ToText(true));
    }

    [Fact]
    public void ExpandedDetailsOfAMissingCheckShowNoCliWords()
    {
        var refusal = new Refusal(RefusalCodes.ApplyDryRunMissing, FailureReason.Refused, DryRunMissingMessage,
            new Dictionary<string, string> { ["proposalId"] = "proposal/1" });

        _avalonia.Invoke(() =>
        {
            var block = new RefusalBlock { DataContext = WindowRefusal.From(refusal) };
            var window = new Window { Content = block, Width = 600, Height = 400 };
            try
            {
                window.Show();
                foreach (var expander in block.GetLogicalDescendants().OfType<Expander>()) expander.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var shown = block.GetLogicalDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible)
                    .Select(text => text.Text ?? string.Empty);
                foreach (var text in shown) AssertWindowWords(text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(RefusalCodes.AssessParserUnavailable)]
    [InlineData(RefusalCodes.StatsParserUnavailable)]
    [InlineData(RefusalCodes.GrammarCheckParserUnavailable)]
    [InlineData(RefusalCodes.WordTraceParserUnavailable)]
    [InlineData(RefusalCodes.HandoffParserUnavailable)]
    public void OnlyAMissingParserIsToldToInstallPanGloss(string code)
    {
        var missing = WindowRefusal.From(new Refusal(code, FailureReason.Refused, "Could not find it.",
            new Dictionary<string, string> { [RefusalFactNames.ParserNotFound] = "true" }));
        var unusable = WindowRefusal.From(new Refusal(code, FailureReason.Refused,
            $"Could not start '{ParserPath}': access is denied."));

        Assert.Equal(
            "Motif could not find PanGloss, so it cannot measure words. Install PanGloss beside Motif, then try again.",
            missing.Sentence);
        Assert.Equal("Motif could not run PanGloss, so it cannot measure words. The details say why.",
            unusable.Sentence);
        Assert.Contains("access is denied", unusable.Details);
    }

    [Fact]
    public void AnEscapedFailureKeepsItsTextOutOfTheSentence()
    {
        var shown = WindowRefusal.Failure(WindowRefusal.OpenFailedCode, "Motif could not open this project.",
            new InvalidOperationException("The Draft table could not be read. The file is locked."));

        Assert.Equal("Motif could not open this project.", shown.Sentence);
        Assert.Equal(WindowRefusal.OpenFailedCode, shown.Code);
        Assert.Equal("The file is locked.", shown.Details);
    }

    [Fact]
    public async Task AFailedOpenIsAWindowRefusalWithTheExceptionFoldedAway()
    {
        var fake = new FakeCommandClient();
        fake.OnGetCurrentBaseline((_, _) => Task.FromException<CommandOutcome<CurrentBaselineResponse>>(
            new InvalidOperationException("The project could not be read.")));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(new ProjectViewModel(fake, new NoProjectPicker()),
            new BaselineViewModel(fake), selection, new AssessViewModel(fake, selection),
            new NoFolderPicker(), new NoDragSource(), fake);

        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(
            Path.Combine(Path.GetTempPath(), "projects", "one.fwdata")));

        var refusal = Assert.IsType<WindowRefusal>(workspace.OpenRefusal);
        Assert.True(workspace.HasOpenRefusal);
        Assert.Equal(WindowRefusal.OpenFailedCode, refusal.Code);
        Assert.Equal("Motif could not open this project.", refusal.Sentence);
        Assert.Equal("The project could not be read.", refusal.Details);
    }

    [Fact]
    public async Task ARefusedSavedDiagnosticIsShownAsItsWindowRefusal()
    {
        WindowRefusal? shown = null;

        await SavedDiagnosticOpener.OpenAsync(
            () => Task.FromResult<string?>("{"),
            _ => throw new InvalidOperationException("A refused diagnostic must not be shown."),
            refusal => shown = refusal);

        Assert.NotNull(shown);
        Assert.Equal(RefusalCodes.WordTraceMalformedDiagnostic, shown.Code);
        Assert.Equal("That file is not a diagnostic Motif can read. Choose a diagnostic Motif saved.", shown.Sentence);
    }

    [Fact]
    public async Task AnUnreadableSavedDiagnosticIsShownAsAWindowRefusal()
    {
        WindowRefusal? shown = null;

        await SavedDiagnosticOpener.OpenAsync(
            () => Task.FromException<string?>(new IOException("The file is in use.")),
            _ => { },
            refusal => shown = refusal);

        Assert.NotNull(shown);
        Assert.Equal("Motif could not read that diagnostic file.", shown.Sentence);
        Assert.Equal("The file is in use.", shown.Details);
    }

    private static void AssertWindowWords(string text)
    {
        foreach (var term in CliTerms)
            Assert.DoesNotContain(term, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("motif ", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" --", text, StringComparison.Ordinal);
    }

    private sealed class NoProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(Avalonia.Input.PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, Avalonia.Input.DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
