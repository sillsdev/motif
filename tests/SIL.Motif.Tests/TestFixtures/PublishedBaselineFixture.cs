using System.IO.Compression;
using SIL.LCModel;
using SIL.Motif.LiveHost.Baselines;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Builds the on-disk layout expected by tests that open a published Baseline.</summary>
internal static class PublishedBaselineFixture
{
    /// <summary>Publishes a loaded project and returns the published <c>.fwdata</c> path.</summary>
    public static Task<string> PublishAsync(LcmCache cache, string publishedRoot) =>
        PublishAsync(publishedRoot, bundle =>
            new BaselineBundleWriter().WriteAsync(cache, bundle, CancellationToken.None));

    /// <summary>Publishes saved project files and returns the published <c>.fwdata</c> path.</summary>
    public static Task<string> PublishAsync(
        string sourceFwDataPath,
        IReadOnlyList<string> sourceWritingSystemPaths,
        string publishedRoot) =>
        PublishAsync(publishedRoot, bundle =>
            new BaselineBundleWriter().WriteAsync(
                sourceFwDataPath, sourceWritingSystemPaths, bundle, CancellationToken.None));

    private static async Task<string> PublishAsync(
        string publishedRoot,
        Func<MemoryStream, Task> writeBundle)
    {
        Directory.CreateDirectory(publishedRoot);
        using var bundle = new MemoryStream();
        await writeBundle(bundle).ConfigureAwait(false);
        using var archive = new ZipArchive(new MemoryStream(bundle.ToArray()), ZipArchiveMode.Read);
        archive.ExtractToDirectory(publishedRoot);

        Directory.CreateDirectory(Path.Combine(publishedRoot, "WritingSystemStore"));
        Directory.CreateDirectory(Path.Combine(publishedRoot, "SharedSettings"));
        return Path.Combine(publishedRoot, NewLangProjFixture.ProjectName + ".fwdata");
    }
}
