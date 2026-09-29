using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// Drives the composed window through its scripted clipboard and diagnostic file dialogs: opening a saved
/// diagnostic from Try a Word, saving and copying its JSON, and copying the AI Handoff's starter prompt.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DesktopSeamWalkthroughTests
{
    private const string DiagnosticJson = """
        {"schemaVersion":"pangloss.trace-details.v2","word":"word",
         "search":{"completed":false,"capped":false,"timedOut":false,"invalidShape":false,"steps":1,"elapsedNs":2},
         "result":{"signature":"-","guessed":false,"analyses":[]},"categories":{},"trace":null}
        """;

    [Fact]
    public void ASavedDiagnosticOpensFromTryAWordAndItsJsonIsSavedAndCopied()
    {
        var clipboard = new RecordingClipboard();
        List<(string SuggestedFileName, string Json)> saved = [];
        var openPrompts = 0;
        List<TopLevel> dialogOwners = [];
        TopLevel? mainWindow = null;
        TopLevel? diagnosticWindow = null;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(Path.GetTempPath(), "unused.fwdata", clipboard: clipboard);
            walkthrough.Show();
            walkthrough.SkipSetup();

            walkthrough.DiagnosticFiles.NextOpenCancels();
            walkthrough.Click("Open a saved diagnostic");
            Assert.Empty(walkthrough.Window.OwnedWindows);

            walkthrough.DiagnosticFiles.NextOpenReads(DiagnosticJson);
            walkthrough.Click("Open a saved diagnostic");
            var diagnostic = Assert.IsType<DiagnosticWindow>(Assert.Single(walkthrough.Window.OwnedWindows));
            mainWindow = walkthrough.Window;
            diagnosticWindow = diagnostic;
            try
            {
                diagnostic.UpdateLayout();
                Pump();
                Assert.Equal(DiagnosticJson, Assert.Single(diagnostic.GetLogicalDescendants().OfType<DiagnosticPanel>())
                    .Tools.Trace.DiagnosticJson);

                ClickTool(diagnostic, "Save full diagnostic JSON");
                walkthrough.DiagnosticFiles.CancelSaves = true;
                ClickTool(diagnostic, "Save full diagnostic JSON");
                ClickTool(diagnostic, "Copy full diagnostic JSON");
            }
            finally
            {
                diagnostic.Close();
            }

            saved.AddRange(walkthrough.DiagnosticFiles.Saved);
            openPrompts = walkthrough.DiagnosticFiles.OpenPrompts;
            dialogOwners.AddRange(walkthrough.DiagnosticFiles.DialogOwners);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));

        Assert.Equal(2, openPrompts);
        Assert.Equal([mainWindow!, diagnosticWindow!], dialogOwners);
        Assert.Equal(("word.trace.json", DiagnosticJson), Assert.Single(saved));
        Assert.Equal(DiagnosticJson, Assert.Single(clipboard.Copied));
    }

    [Fact]
    public void AMalformedSavedDiagnosticOpensAWindowThatSaysWhy()
    {
        WindowRefusal? shown = null;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(Path.GetTempPath(), "unused.fwdata");
            walkthrough.Show();
            walkthrough.SkipSetup();

            walkthrough.DiagnosticFiles.NextOpenReads("{");
            walkthrough.Click("Open a saved diagnostic");
            var diagnostic = Assert.IsType<DiagnosticWindow>(Assert.Single(walkthrough.Window.OwnedWindows));
            try
            {
                diagnostic.UpdateLayout();
                Pump();
                var error = diagnostic.GetLogicalDescendants().OfType<RefusalBlock>().Single(block =>
                    AutomationProperties.GetName(block) == "Diagnostic file error");
                Assert.True(error.IsVisible);
                shown = error.DataContext as WindowRefusal;
            }
            finally
            {
                diagnostic.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));

        Assert.Equal(SIL.Motif.Contract.Commands.RefusalCodes.WordTraceMalformedDiagnostic, shown?.Code);
    }

    [Fact]
    public void TheStarterPromptAndAQuestionAreCopiedExactly()
    {
        var clipboard = new RecordingClipboard();
        const string starterPrompt = "Read handoff.md first, then answer from the files alone.";
        const string question = "Which words did not parse, and what do they have in common?";

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(Path.GetTempPath(), "unused.fwdata", clipboard: clipboard);
            var handoff = walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff;
            handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
            handoff.OutputDirectory = @"C:\handoff";
            handoff.PastedHeader = starterPrompt;
            handoff.State = RunState.Completed;
            walkthrough.Show();
            walkthrough.SkipSetup();

            walkthrough.Click("Copy the starter prompt");
            walkthrough.Click(question);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));

        Assert.Equal([starterPrompt, question], clipboard.Copied);
    }

    // The tools sit in a flyout, which is outside the window's logical tree until it opens.
    private static void ClickTool(DiagnosticWindow window, string accessibleName)
    {
        var tools = window.GetLogicalDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetName(button) == "Diagnostic tools");
        var flyout = Assert.IsType<Flyout>(tools.Flyout);
        flyout.ShowAt(tools);
        Pump();
        var tool = Assert.IsAssignableFrom<Control>(flyout.Content).GetLogicalDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == accessibleName);
        Assert.True(tool.IsEffectivelyEnabled, $"'{accessibleName}' is not effectively enabled.");
        tool.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        flyout.Hide();
        Pump();
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
