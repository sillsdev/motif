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
        foreach (var spec in RefusalCatalog.Specs.Values.Where(spec => spec.ShownInWindow))
            data.Add(spec.Code);
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
        Assert.Contains("projectPath: one.fwdata", details);
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

    [Fact]
    public void StructuredCompilePathsKeepProseInBothWindowSurfacesAndFullPathsInReport()
    {
        const string text = "Cannot read /home/alice/private/project.fwdata.";
        const string advice = @"Check C:\Users\alice\private\rules.txt and \\server\private\source.xml.";
        var output = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = "compile_error", schema_version = 1,
            issues = new[] { new { fatal = true, code = "Invalid", kind = "Grammar", text, advice } },
        });
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable,
            FailureReason.Refused, output));

        var issue = Assert.Single(shown.CompileIssues);
        Assert.Equal("Cannot read project.fwdata.", issue.Text);
        Assert.Equal("Check rules.txt and source.xml.", issue.Advice);
        Assert.Contains(issue.Text, shown.Details);
        Assert.Contains(issue.Advice, shown.Details);
        Assert.DoesNotContain("/home/alice", shown.Details);
        Assert.DoesNotContain(@"C:\Users", shown.Details);
        Assert.DoesNotContain(@"\\server", shown.Details);
        var report = ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true);
        Assert.Contains(text, report);
        Assert.Contains(advice, report);
    }

    [Theory]
    [InlineData("See 'relative/private/project.fwdata'.", "See 'project.fwdata'.")]
    [InlineData("See '/home/alice/Secret Folder/private/project.fwdata'.", "See 'project.fwdata'.")]
    [InlineData(@"See 'C:\Users\alice\Secret Folder\private\project.fwdata'.", "See 'project.fwdata'.")]
    [InlineData(@"See [\\server\Secret Folder\private\project.fwdata].", "See [Secret project.fwdata].")]
    [InlineData(@"See '\\server\Secret Folder\private\project.fwdata'.", "See 'project.fwdata'.")]
    [InlineData("See '/home/alice/language's.fwdata'.", "See 'language's.fwdata'.")]
    [InlineData("See \"/home/alice/a\"b.fwdata\".", "See \"a\"b.fwdata\".")]
    [InlineData("See (C:/Users/alice/project.fwdata).", "See (project.fwdata).")]
    [InlineData("File path='/home/alice/private/project.fwdata'.", "File path='project.fwdata'.")]
    [InlineData(@"Path:'C:\Users\Alice Secret\project.fwdata'.", "Path:'project.fwdata'.")]
    [InlineData("Path:\"/home/Alice Secret/project.fwdata\".", "Path:\"project.fwdata\".")]
    [InlineData(@"Don't change Alice's word. Path:'C:\Users\Alice Secret\project.fwdata'.",
        "Don't change Alice's word. Path:'project.fwdata'.")]
    [InlineData(@"Read private\alice\project.fwdata now.", "Read project.fwdata now.")]
    [InlineData(@"Read C:private\alice\project.fwdata now.", "Read project.fwdata now.")]
    [InlineData("Read ./private/file, ../private/file, and ~/private/file.", "Read file, file, and file.")]
    [InlineData("Read relative/private/folder and relative/123/file.fwdata.", "Read relative/private/folder and relative/123/file.fwdata.")]
    [InlineData("Could not read private/project.fwdata.", "Could not read project.fwdata.")]
    [InlineData("Nasal assimilation before /p/ is unexpected.", "Nasal assimilation before /p/ is unexpected.")]
    [InlineData("The rule /p/ -> [b] is unexpected.", "The rule /p/ -> [b] is unexpected.")]
    [InlineData(@"Keep /private.fwdata and \private.fwdata.", @"Keep /private.fwdata and \private.fwdata.")]
    [InlineData("Keep relative/123/folder and https://example.org/a/b?x=1/2.",
        "Keep relative/123/folder and https://example.org/a/b?x=1/2.")]
    [InlineData("See ftp://example.org/files/example and http://example.org/a/b.",
        "See ftp://example.org/files/example and http://example.org/a/b.")]
    [InlineData("Expected 1/2 forms on 10/30/2026; see https://example.org/grammar/guide.",
        "Expected 1/2 forms on 10/30/2026; see https://example.org/grammar/guide.")]
    public void StructuredCompileTextKeepsProseAndShortensPathTokens(string text, string expected)
    {
        var diagnostic = new ParserCompileDiagnostic("Cannot compile.",
            [new ParserCompileIssue("Invalid", "Grammar", null, null, text, text)], text);
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable,
            FailureReason.Refused, text, parserDiagnostic: diagnostic));

        var issue = Assert.Single(shown.CompileIssues);
        Assert.Equal(expected, issue.Text);
        Assert.Equal(expected, issue.Advice);
        Assert.Contains(expected, shown.Details);
        Assert.Contains(text, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Theory]
    [InlineData("/private/Alice's Secret Folder/project.fwdata", "/private/Alice's Secret Folder")]
    [InlineData(@"C:\private\Alice's Secret Folder\project.fwdata", @"C:\private\Alice's Secret Folder")]
    public void StructuredCompileTextReplacesKnownPathsAndTheirDirectories(string path, string directory)
    {
        var text = $"Cannot read '{path}'. Check directory '{directory}'.";
        var diagnostic = new ParserCompileDiagnostic("Cannot compile.",
            [new ParserCompileIssue("Invalid", "Grammar", null, null, text, text)], text);
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable,
            FailureReason.Refused, text, new Dictionary<string, string> { ["projectPath"] = path }, diagnostic));

        var issue = Assert.Single(shown.CompileIssues);
        Assert.Equal("Cannot read 'project.fwdata'. Check directory 'project.fwdata'.", issue.Text);
        Assert.Equal(issue.Text, issue.Advice);
        Assert.Contains(issue.Text, shown.Details);
        Assert.DoesNotContain("Secret", shown.Details);
        Assert.Contains(text, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Theory]
    [InlineData("//private.fwdata", "The root is /; 1/2 remains.")]
    [InlineData(@"C:\\private.fwdata", @"The C:\ drive remains available.")]
    public void RepeatedSeparatorsDoNotTurnARootIntoAReplaceableDirectory(string path, string text)
    {
        var diagnostic = new ParserCompileDiagnostic("Cannot compile.",
            [new ParserCompileIssue("Invalid", "Grammar", null, null, text, text)], text);
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable,
            FailureReason.Refused, text, new Dictionary<string, string> { ["projectPath"] = path }, diagnostic));

        var issue = Assert.Single(shown.CompileIssues);
        Assert.Equal(text, issue.Text);
        Assert.Equal(text, issue.Advice);
        Assert.Contains(text, shown.Details);
        Assert.Contains(path, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Fact]
    public void StructuredCompileTextWithoutPathsIsUnchanged()
    {
        const string text = "The environment's expression [a b] cannot be read.";
        var diagnostic = new ParserCompileDiagnostic("Cannot compile.",
            [new ParserCompileIssue("Invalid", "Grammar", null, null, text, text)], text);
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable,
            FailureReason.Refused, text, parserDiagnostic: diagnostic));

        var issue = Assert.Single(shown.CompileIssues);
        Assert.Equal(text, issue.Text);
        Assert.Equal(text, issue.Advice);
        Assert.Contains(text, shown.Details);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongStructuredCompileTextIsProcessedWithin100Milliseconds(bool hasPath)
    {
        var text = new string('(', 100_000) + (hasPath ? "/private/file" : "x");
        var diagnostic = new ParserCompileDiagnostic("Cannot compile.",
            [new ParserCompileIssue("Invalid", "Grammar", null, null, text, text)], text);
        var refusal = new Refusal(RefusalCodes.AssessParserUnavailable, FailureReason.Refused, text,
            parserDiagnostic: diagnostic);
        _ = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable, FailureReason.Refused, "warmup"));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var shown = WindowRefusal.From(refusal);
        var issue = Assert.Single(shown.CompileIssues);
        clock.Stop();

        Assert.Equal(hasPath ? new string('(', 100_000) + "file" : text, issue.Text);
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(100), $"Took {clock.Elapsed.TotalMilliseconds:F1} ms.");
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
            var window = new MotifTestWindow { Content = block, Width = 600, Height = 400 };
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
        Assert.Equal("Motif could not run PanGloss, so it cannot measure words. Review a problem report for technical details.",
            unusable.Sentence);
        Assert.Null(unusable.Details);
        Assert.Contains("access is denied", ProblemReport.FromRefusal(unusable).ToText(includeLocalDetails: true));
    }

    [Fact]
    public void AnEscapedFailureKeepsItsTextOutOfTheSentence()
    {
        var shown = WindowRefusal.Failure(WindowRefusal.OpenFailedCode, "Motif could not open this project.",
            new InvalidOperationException("The Draft table could not be read. The file is locked."));

        Assert.Equal("Motif could not open this project.", shown.Sentence);
        Assert.Equal(WindowRefusal.OpenFailedCode, shown.Code);
        Assert.Null(shown.Details);
        Assert.Contains("The file is locked.", ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
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
        Assert.Null(refusal.Details);
        Assert.Contains("The project could not be read.", ProblemReport.FromRefusal(refusal).ToText(includeLocalDetails: true));
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
        Assert.Null(shown.Details);
        Assert.Contains("The file is in use.", ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
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
