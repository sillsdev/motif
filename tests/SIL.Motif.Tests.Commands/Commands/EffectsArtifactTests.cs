using System.Collections;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class EffectsArtifactTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-effects-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConcurrentPublicationsHaveUniqueCompleteFilesAndLeaveReadersUntouched()
    {
        var first = Write();
        using var reader = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read);
        var paths = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(Write)));
        Assert.Equal(16, paths.Distinct().Count());
        Assert.DoesNotContain(first, paths);
        foreach (var path in paths.Append(first))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(1, json.RootElement.GetArrayLength());
        }
        Assert.Equal(17, Directory.GetFiles(Path.GetDirectoryName(first)!).Length);
    }

    [Fact]
    public void InterruptedPublicationPreservesCompleteArtifactsAndCleansStagingFiles()
    {
        var first = Write();
        var original = File.ReadAllBytes(first);
        Assert.Throws<IOException>(() => EffectsArtifactWriter.WriteApply(_root, Project, "proposal", "digest",
            new InterruptedEffects()));
        Assert.Equal(original, File.ReadAllBytes(first));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(first)!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupAndUserDataSweepsExpireEffectsAndOrphanStagingButKeepRecentFiles(bool startup)
    {
        var ownership = WorkspaceOwnership.Bootstrap(_root);
        var expired = Write();
        var recent = Write();
        var staging = expired + ".tmp";
        File.WriteAllText(staging, "interrupted write");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-8));
        File.SetLastWriteTimeUtc(staging, DateTime.UtcNow.AddDays(-8));
        var projectSegment = new DirectoryInfo(Path.GetDirectoryName(expired)!).Parent!.Parent!.Name;
        if (startup)
            Assert.True(new WorkspaceCleaner(ownership).CleanupStartup(projectSegment).Succeeded);
        else
        {
            using var machine = MachineDatabase.Open(_root);
            Assert.Empty(new UserDataSweep(_root, new KnownProjectRegistry(machine)).Sweep().Failures);
        }
        Assert.False(File.Exists(expired));
        Assert.False(File.Exists(staging));
        Assert.True(File.Exists(recent));
    }

    private string Project => Path.Combine(_root, "project.fwdata");
    private string Write() => EffectsArtifactWriter.WriteApply(_root, Project, "proposal", "digest",
        [new("target", "field", [], null)]);

    private sealed class InterruptedEffects : IReadOnlyList<EffectView>
    {
        public int Count => 1;
        public EffectView this[int index] => throw new IOException("Interrupted effects read.");
        public IEnumerator<EffectView> GetEnumerator() => throw new IOException("Interrupted effects read.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
