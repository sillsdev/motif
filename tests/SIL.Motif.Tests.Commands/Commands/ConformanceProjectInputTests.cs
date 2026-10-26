using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ConformanceProjectInputTests
{
    [Fact]
    public void MissingPinnedInputNamesTheProvisioningCommand()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var exception = Assert.Throws<InvalidDataException>(() => new ConformanceProject(root));

            Assert.Contains("Get-ConformanceFixture.ps1", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModifiedPinnedInputFailsItsChecksum()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "project.fwdata"), new byte[69_873]);

            var exception = Assert.Throws<InvalidDataException>(() => new ConformanceProject(root));

            Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
            Assert.Contains("project.fwdata", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingPinnedOracleNamesTheProvisioningCommand()
    {
        var root = CopyPinnedCacheToTemporaryDirectory();
        try
        {
            File.Delete(Path.Combine(root, "words.yaml"));

            var exception = Assert.Throws<InvalidDataException>(() => new ConformanceProject(root));

            Assert.Contains("words.yaml", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Get-ConformanceFixture.ps1", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModifiedPinnedOracleFailsItsChecksum()
    {
        var root = CopyPinnedCacheToTemporaryDirectory();
        try
        {
            var oraclePath = Path.Combine(root, "words.yaml");
            var bytes = File.ReadAllBytes(oraclePath);
            bytes[0] ^= 1;
            File.WriteAllBytes(oraclePath, bytes);

            var exception = Assert.Throws<InvalidDataException>(() => new ConformanceProject(root));

            Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
            Assert.Contains("words.yaml", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CopyPinnedCacheToTemporaryDirectory()
    {
        var source = Environment.GetEnvironmentVariable("MOTIF_TEST_CONFORMANCE");
        if (string.IsNullOrWhiteSpace(source))
        {
            var repositoryRoot = FindRepositoryRoot();
            source = Path.Combine(
                repositoryRoot, "bin", ".cache", "conformance-oracle-v2",
                "f150e2a005ce639f7d68ef17fb0db25b2f6aaa3c");
        }

        var target = CreateTemporaryDirectory();
        foreach (var sourcePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, sourcePath);
            var targetPath = Path.Combine(target, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath);
        }

        return target;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var gitMarker = Path.Combine(directory.FullName, ".git");
            if (File.Exists(gitMarker) || Directory.Exists(gitMarker))
                return directory.FullName;
        }

        throw new InvalidOperationException("Cannot locate the Motif checkout.");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SIL.Motif.ConformanceInput", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
