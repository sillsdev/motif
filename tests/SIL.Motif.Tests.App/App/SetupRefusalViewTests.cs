using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// The Setup dialog shows a command's refusal in the window's words: the default-Selection refusal reaches it
/// with a CLI remedy in its message, and no text the dialog holds, folded Details included, is a command line.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SetupRefusalViewTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    private readonly AvaloniaHeadlessFixture _avalonia;

    public SetupRefusalViewTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void TheDefaultSelectionRefusalShowsNoCliCommandLine()
    {
        var refusal = new Refusal(RefusalCodes.SelectionDefaultMissing, FailureReason.Refused,
            "No default Selection is saved for this project. Save one with `motif selection set-default` first.");
        WorkspaceShellViewModel? workspace = null;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var projectPicker = new FakeProjectPicker();
            var selection = new SelectionViewModel(fake);
            workspace = new WorkspaceShellViewModel(
                new ProjectViewModel(fake, projectPicker),
                new BaselineViewModel(fake),
                selection,
                new AssessViewModel(fake, selection),
                new FakeFolderPicker(), new FakeDragSource(),
                fake);
            fake.DefaultSelectionHandler = (_, _) =>
                Task.FromResult(CommandOutcome<DefaultSelectionResponse>.Refused(refusal));
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
            {
                ProjectLastWriteUtc = Saved,
            });
            fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
            projectPicker.PathToReturn = ProjectPath;
            await workspace.Project.BrowseCommand.ExecuteAsync(null);
        }, TimeSpan.FromSeconds(10));

        _avalonia.Invoke(() =>
        {
            var setup = workspace!.Context.Setup;
            Assert.NotNull(setup);
            Assert.NotNull(setup.ShownRefusal);
            setup.IsOpen = true;
            var dialog = new SetupDialog { DataContext = setup };
            var window = new Window { Content = dialog, Width = 1000, Height = 800 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var texts = dialog.GetLogicalDescendants().OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty)
                    .Where(text => text.Length > 0)
                    .ToList();
                Assert.Contains(setup.ShownRefusal!.Sentence, texts);
                Assert.DoesNotContain(texts, text => text.Contains("motif ", StringComparison.Ordinal));
                Assert.DoesNotContain(texts, text => text.Contains("set-default", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheLimitsStepShowsAnEstimateAndNoTimeLimitInput()
    {
        WorkspaceShellViewModel? workspace = null;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var projectPicker = new FakeProjectPicker();
            var selection = new SelectionViewModel(fake);
            workspace = new WorkspaceShellViewModel(
                new ProjectViewModel(fake, projectPicker), new BaselineViewModel(fake), selection,
                new AssessViewModel(fake, selection), new FakeFolderPicker(), new FakeDragSource(), fake);
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
            {
                ProjectLastWriteUtc = Saved,
            });
            fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
            projectPicker.PathToReturn = ProjectPath;
            await workspace.Project.BrowseCommand.ExecuteAsync(null);
        }, TimeSpan.FromSeconds(10));

        _avalonia.Invoke(() =>
        {
            var setup = workspace!.Context.Setup!;
            setup.Step = 2;
            var dialog = new SetupDialog { DataContext = setup };
            var window = new Window { Content = dialog, Width = 1000, Height = 800 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var texts = dialog.GetLogicalDescendants().OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty).ToArray();
                var numbers = dialog.GetLogicalDescendants().OfType<NumericUpDown>().ToArray();
                Assert.Contains(setup.StepLimitEstimateText, texts);
                Assert.Contains(texts, text => text.Contains("1,000,000 steps is the default", StringComparison.Ordinal));
                Assert.DoesNotContain(numbers, number =>
                    Avalonia.Automation.AutomationProperties.GetName(number) == "Time limit per word, in seconds");
                Assert.Single(numbers);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public string? PathToReturn { get; set; }

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PathToReturn);
    }

    private sealed class FakeFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
