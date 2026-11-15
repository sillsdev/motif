using System.Diagnostics;
using SIL.Motif.Worker.Baselines;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

public sealed class ProgressSourceIdentityTests(ITestOutputHelper output)
{
    [Fact]
    public void ContentHashFindsSameLengthChangeWithRestoredTimestamp()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-source-identity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "WritingSystemStore"));
        try
        {
            var path = Path.Combine(root, "project.fwdata");
            var ldml = Path.Combine(root, "WritingSystemStore", "vern.ldml");
            File.WriteAllText(path, "first");
            File.WriteAllText(ldml, "alpha");
            var original = ProgressSourceIdentity.Capture(path)!;
            var time = File.GetLastWriteTimeUtc(path);
            File.WriteAllText(path, "other");
            File.SetLastWriteTimeUtc(path, time);
            Assert.True(original.Current(path));
            var changed = ProgressSourceIdentity.Capture(path)!;
            Assert.NotEqual(original.Digest, changed.Digest);
            File.AppendAllText(ldml, "z");
            Assert.False(changed.Current(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [ProgressExternalSourceFact]
    [Trait("Category", "SlowScale")]
    public void CebSizedSourceFingerprintCost()
    {
        var path = Environment.GetEnvironmentVariable("MOTIF_PROGRESS_BENCHMARK_FWDATA")!;
        var watch = Stopwatch.StartNew();
        var before = ProgressSourceIdentity.Capture(path);
        var after = ProgressSourceIdentity.Capture(path);
        watch.Stop();
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before.Digest, after.Digest);
        output.WriteLine($"Two source fingerprints: {watch.Elapsed.TotalMilliseconds:F1} ms; fwdata {new FileInfo(path).Length} bytes");
        Assert.True(watch.ElapsedMilliseconds < 1000, $"Two fingerprints took {watch.ElapsedMilliseconds} ms");
    }
}

internal sealed class ProgressExternalSourceFactAttribute : FactAttribute
{
    public ProgressExternalSourceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_SLOW_SCALE") != "1" ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MOTIF_PROGRESS_BENCHMARK_FWDATA")))
            Skip = "Set MOTIF_SLOW_SCALE and MOTIF_PROGRESS_BENCHMARK_FWDATA to measure an external project.";
    }
}
