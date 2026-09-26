using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Microsoft.Data.Sqlite;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class StoreRefusalWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ARefusedOlderStoreCanBeExplainedKeptOrDeletedFromTheWindow()
    {
        using var project = new WalkthroughProject(pristine);
        var storePath = StorePath(project.FwDataPath);
        var projectBytes = File.ReadAllBytes(project.FwDataPath);
        var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);

            Assert.True(File.Exists(storePath));
            WithStore(storePath, command =>
            {
                command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};";
                return command.ExecuteNonQuery();
            });
            var oldStoreBytes = File.ReadAllBytes(storePath);
            Assert.Equal(MotifSchema.CurrentSchema - 1, UserVersion(storePath));

            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.ShownRefusal?.Code == RefusalCodes.StoreOtherVersion &&
                    !walkthrough.Workspace.RefreshCommand.IsRunning,
                WalkthroughSteps.Remaining(deadline), "refreshing the older store did not show its refusal");

            var refusal = Assert.IsType<SIL.Motif.App.ViewModels.WindowRefusal>(
                walkthrough.Workspace.Baseline.ShownRefusal);
            var block = walkthrough.Find<RefusalBlock>("Refresh refusal");
            Assert.True(block.IsEffectivelyVisible);
            Assert.Contains(storePath, refusal.Sentence, StringComparison.Ordinal);
            var sentence = block.GetLogicalDescendants().OfType<CopyableTextBlock>()
                .Single(text => text.Text == refusal.Sentence);
            Assert.True(sentence.IsEffectivelyVisible);
            Assert.Contains(storePath, refusal.Details, StringComparison.Ordinal);

            var details = walkthrough.FindRefusalDetails();
            HeadlessClick.Click(walkthrough.Window, details, "Refusal details");
            Assert.True(details.IsExpanded);
            var detailsText = block.GetLogicalDescendants().OfType<CopyableTextBlock>()
                .Single(text => text.Text == refusal.Details);
            Assert.True(detailsText.IsEffectivelyVisible);

            walkthrough.ClickDeleteButton();
            var question = walkthrough.Named<StackPanel>("StoreDeletionQuestion");
            Assert.True(question.IsEffectivelyVisible);

            walkthrough.Click("Keep Motif's file");
            Assert.False(question.IsEffectivelyVisible);
            Assert.True(block.FindDeleteButton().IsEffectivelyVisible);
            Assert.Equal(oldStoreBytes, File.ReadAllBytes(storePath));
            Assert.Equal(MotifSchema.CurrentSchema - 1, UserVersion(storePath));

            walkthrough.ClickDeleteButton();
            Assert.True(question.IsEffectivelyVisible);
            walkthrough.Click("Delete Motif's file and reopen the project");
            walkthrough.WaitUntil(
                () => !walkthrough.Workspace.IsConfirmingStoreDeletion &&
                    walkthrough.Workspace.Baseline.ShownRefusal is null &&
                    !walkthrough.Workspace.ConfirmStoreDeletionCommand.IsRunning,
                WalkthroughSteps.Remaining(deadline), "confirming deletion did not reopen the project");

            Assert.True(File.Exists(storePath));
            Assert.Equal(MotifSchema.CurrentSchema, UserVersion(storePath));
            Assert.Equal(projectBytes, File.ReadAllBytes(project.FwDataPath));

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void ACorruptStoreIsShownInTheWindowWithoutADeleteButtonOrByteChanges()
    {
        using var project = new WalkthroughProject(pristine);
        var storePath = StorePath(project.FwDataPath);
        var corruptBytes = Enumerable.Repeat((byte)'x', 4096).ToArray();
        File.WriteAllBytes(storePath, corruptBytes);
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.ShownRefusal?.Code == RefusalCodes.StoreInconsistent,
                WalkthroughSteps.Remaining(deadline), "opening the damaged store did not show its refusal");

            var refusal = Assert.IsType<SIL.Motif.App.ViewModels.WindowRefusal>(
                walkthrough.Workspace.Baseline.ShownRefusal);
            Assert.Equal(RefusalCodes.StoreInconsistent, refusal.Code);
            var block = walkthrough.Find<RefusalBlock>("Refresh refusal");
            Assert.True(block.IsEffectivelyVisible);
            Assert.Contains("damaged", refusal.Sentence, StringComparison.OrdinalIgnoreCase);
            Assert.False(block.FindDeleteButton().IsEffectivelyVisible);
            Assert.Equal(corruptBytes, File.ReadAllBytes(storePath));

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static string StorePath(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        return ProjectDatabaseCatalog.DatabasePathFor(project);
    }

    private static int UserVersion(string databasePath) =>
        WithStore(databasePath, command =>
        {
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(command.ExecuteScalar());
        });

    private static T WithStore<T>(string databasePath, Func<SqliteCommand, T> action)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        return action(command);
    }
}
