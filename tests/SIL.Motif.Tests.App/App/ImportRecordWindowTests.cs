using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ImportRecordWindowTests(AvaloniaHeadlessFixture avalonia)
{
    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    [Fact]
    public void EveryControlStartsOffUntilItCanDoSomething() => avalonia.Invoke(() =>
    {
        var (model, _, _, _) = ImportRecordDialogModelTests.Make();
        var window = new ImportRecordWindow(model);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.False(Find<Button>(window, "PreviewButton").IsEffectivelyEnabled);
            Assert.False(Find<Button>(window, "OpenDraftButton").IsEffectivelyEnabled);
            Assert.True(Find<Button>(window, "BrowseButton").IsEffectivelyEnabled);
            Assert.True(Find<Button>(window, "CancelButton").IsEffectivelyEnabled);
            Assert.False(model.HasPreview);
            Assert.False(Find<Button>(window, "StageButton").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void PreviewingAFileShowsItsOperationsAndRefusalsAndEnablesStageOnlyWithAName() =>
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, _, _, _) = ImportRecordDialogModelTests.Make();
            var window = new ImportRecordWindow(model);
            try
            {
                window.Show();
                model.Import.FilePath = "edited.md";
                Dispatcher.UIThread.RunJobs();
                Assert.True(Find<Button>(window, "PreviewButton").IsEffectivelyEnabled);

                await model.Import.PreviewAsync();
                PageScreenshots.SettleLayout(window);

                Assert.Equal(2, Find<ItemsControl>(window, "OperationList").ItemCount);
                Assert.Equal(1, Find<ItemsControl>(window, "RefusalList").ItemCount);
                var stage = Find<Button>(window, "StageButton");
                Assert.False(stage.IsEffectivelyEnabled);
                Find<TextBox>(window, "DraftNameBox").Text = "From file";
                PageScreenshots.SettleLayout(window);
                Assert.True(stage.IsEffectivelyEnabled);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(1));

    [Fact]
    public void CancelClosesTheWindowWhileAPreviewIsStillRunning() =>
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var gate = new TaskCompletionSource<CommandOutcome<RecordImportResponse>>();
            var (model, _, _, _) = ImportRecordDialogModelTests.Make((_, token) =>
            {
                token.Register(() => gate.TrySetCanceled(token));
                return gate.Task;
            });
            var window = new ImportRecordWindow(model);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Show();
            model.Import.FilePath = "edited.md";
            var running = model.Import.PreviewAsync();
            PageScreenshots.SettleLayout(window);
            var cancel = Find<Button>(window, "CancelButton");
            Assert.True(model.Import.IsBusy);
            Assert.True(cancel.IsEffectivelyEnabled);

            HeadlessClick.Click(window, cancel, MotifAppStrings.ImportRecordCancelAccessibleName);
            await running;

            Assert.True(closed);
        }, TimeSpan.FromMinutes(1));

    [Fact]
    public void StagingOpensTheDraftAndClosesTheWindow() =>
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, _, _, opened) = ImportRecordDialogModelTests.Make();
            var window = new ImportRecordWindow(model);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Show();
            model.Import.FilePath = "edited.md";
            model.Import.DraftName = "From file";
            await model.Import.PreviewAsync();

            await model.Import.StageAsync();
            await model.OpenTask;

            Assert.Equal("proposal-7", Assert.Single(opened));
            Assert.True(closed);
        }, TimeSpan.FromMinutes(1));

    [Fact]
    public void TheFileMenuOffersOpenRecordFileOnlyWithAProject() => avalonia.Invoke(() =>
    {
        var (_, window) = FakeComposedWindow.Create();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var item = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Name == "OpenRecordFileMenuItem");
            Assert.False(item.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    });
}
