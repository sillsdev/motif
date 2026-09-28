using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Baselines;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using Xunit;

namespace SIL.Motif.Tests.Worker;

[Collection(LcmCacheTestCollection.Name)]
public sealed class BaselineBundleReceiverLibLcmTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        nameof(BaselineBundleReceiverLibLcmTests), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PublishVerifiedAsync_ReusesThePublicationWhileLibLcmHasItsLockMarker()
    {
        Directory.CreateDirectory(_root);
        var sourceFwData = pristine.CopyProjectFile();
        var sourceFolder = Path.GetDirectoryName(sourceFwData)!;
        var writingSystems = Directory.GetFiles(
            Path.Combine(sourceFolder, "WritingSystemStore"), "*.ldml", SearchOption.TopDirectoryOnly);
        var bundlePath = Path.Combine(_root, "bundle.zip");
        using (var bundle = File.Create(bundlePath))
        {
            await new BaselineBundleWriter().WriteAsync(
                sourceFwData, writingSystems, bundle, CancellationToken.None);
        }

        var first = VerifiedTransfer(bundlePath);
        var retryPath = Path.Combine(_root, "retry.zip");
        File.Copy(bundlePath, retryPath);
        var retry = VerifiedTransfer(retryPath);
        var token = Token(first.Sha256);
        var target = new BaselinePublicationTarget(Path.Combine(_root, "managed"), token.ProjectIdentity);
        var receiver = new BaselineBundleReceiver();
        var publication = await receiver.PublishVerifiedAsync(first, token, target, CancellationToken.None);
        var lockPath = publication.FwDataPath + ".lock";

        using (new BaselineScratchFactory().OpenSingleUse(publication.FwDataPath))
        {
            Assert.True(File.Exists(lockPath));
            using var lockContent = JsonDocument.Parse(File.ReadAllText(lockPath));
            var process = Process.GetCurrentProcess();
            var marker = lockContent.RootElement;
            Assert.Equal("FileLockContent:#Palaso.IO.FileLock", marker.GetProperty("__type").GetString());
            Assert.Equal(process.Id, marker.GetProperty("PID").GetInt32());
            Assert.Equal(process.ProcessName, marker.GetProperty("ProcessName").GetString());
            Assert.InRange(marker.GetProperty("Timestamp").GetInt64(), process.StartTime.Ticks, DateTime.Now.Ticks);

            var reused = await receiver.PublishVerifiedAsync(retry, token, target, CancellationToken.None);

            Assert.Equal(publication, reused);
        }

        Assert.False(File.Exists(lockPath));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static VerifiedBinaryTransfer VerifiedTransfer(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new VerifiedBinaryTransfer(Guid.NewGuid().ToString("N"), path, bytes.Length, digest);
    }

    private static BaselineToken Token(string bundleDigest) => new(
        "project-id", "sha256:" + new string('1', 64), "projection-v1", "2026-08-23T00:00:00Z",
        "sha256:" + bundleDigest);
}
