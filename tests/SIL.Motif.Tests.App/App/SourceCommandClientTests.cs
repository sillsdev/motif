using SIL.Motif.App.Services;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class SourceCommandClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task SourceAddStagesAProposalWithoutCopyingParatextTextIntoFieldWorks()
    {
        using var project = new WalkthroughProject(pristine);
        var localPath = Path.Combine(project.ManagedRoot, "scripture-local.json");
        using var environment = AmbientEnvironment.Push(new Dictionary<string, string?>
            { [ScriptureLocalStore.PathEnvironmentVariable] = localPath });
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        var captured = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var before = File.ReadAllBytes(project.FwDataPath);
        var sourceFolder = Path.Combine(project.ManagedRoot, "external-source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "private-text.txt"), "Disposable external text.");
        var staged = await client.StageSourceAddAsync(new(project.FwDataPath, "TEXT", "External Text", "English", "MAT 1-3",
            Folder: sourceFolder), CancellationToken.None);
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.NotEmpty(staged.Value!.Operations);
        Assert.Equal(sourceFolder, staged.Value.Folder);
        Assert.True(File.Exists(localPath));
        var listed = await client.ListSourcesAsync(new(project.FwDataPath), CancellationToken.None);
        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        Assert.Empty(listed.Value!.Sources);
        Assert.Equal(before, File.ReadAllBytes(project.FwDataPath));
    }

    [Fact]
    public async Task InvalidSourceKindAndRangeAreRefusedThroughTheTypedAdapter()
    {
        using var project = new WalkthroughProject(pristine);
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        Assert.True((await client.CaptureBaselineAsync(new(project.FwDataPath), CancellationToken.None)).Succeeded);
        var kind = await client.StageSourceAddAsync(new(project.FwDataPath, "TEXT", "Text", "English", "MAT 1", Kind: "JSONL"), CancellationToken.None);
        Assert.False(kind.Succeeded);
        var range = await client.StageSourceAddAsync(new(project.FwDataPath, "TEXT", "Text", "English", "invalid range"), CancellationToken.None);
        Assert.False(range.Succeeded);
    }
}
