using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class NoTextProjectWorkflowTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task AProjectWithoutTextsCanCaptureAndSaveATypedSelection()
    {
        var projectPath = pristine.CopyProjectFile();
        var managedRoot = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.NoTextProjectWorkflow", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(managedRoot);

        try
        {
            var client = RealCommandClient.Create(managedRoot);
            var beforeCapture = await client.ListTextsAsync(
                new TextInventoryRequest(projectPath), CancellationToken.None);
            Assert.True(beforeCapture.Succeeded, beforeCapture.Refusal?.Message);
            Assert.False(beforeCapture.Value!.HasBaseline);
            Assert.Empty(beforeCapture.Value.Texts);

            var captured = await client.CaptureBaselineAsync(
                new BaselineCaptureRequest(projectPath), CancellationToken.None);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);

            var inventory = await client.ListTextsAsync(
                new TextInventoryRequest(projectPath), CancellationToken.None);
            Assert.True(inventory.Succeeded, inventory.Refusal?.Message);
            Assert.True(inventory.Value!.HasBaseline);
            Assert.Empty(inventory.Value.Texts);

            var saved = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                projectPath, "Default", [], [SeededProject.FirstForm]), CancellationToken.None);
            Assert.True(saved.Succeeded, saved.Refusal?.Message);
            Assert.Empty(saved.Value!.Selection!.TextIds);
            Assert.Equal(SeededProject.FirstForm, Assert.Single(saved.Value.Selection.AddedWords));

            var readBack = await client.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(projectPath), CancellationToken.None);
            Assert.True(readBack.Succeeded, readBack.Refusal?.Message);
            Assert.Empty(readBack.Value!.Selection!.TextIds);
            Assert.Equal(SeededProject.FirstForm, Assert.Single(readBack.Value.Selection.AddedWords));
        }
        finally
        {
            try { Directory.Delete(managedRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
