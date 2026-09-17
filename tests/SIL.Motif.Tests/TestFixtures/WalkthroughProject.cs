using System.Security.Cryptography;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

public sealed class WalkthroughProject : IDisposable
{
    public WalkthroughProject(PristineProjectFixture pristine)
    {
        ArgumentNullException.ThrowIfNull(pristine);

        var cache = pristine.NewScratch();
        try
        {
            SeededProject.SeedText(cache, pristine.Seed);
            RealParserProject.PrepareForParsing(
                cache, "m", "o", "t", "i", "f", "a", "n", "l", "y", "s", "e", "d", "u", "b");
            new FwDataProjectLoader().Save(cache);
            FwDataPath = cache.ProjectId.Path;
        }
        finally
        {
            if (!cache.IsDisposed) cache.Dispose();
        }

        ManagedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.Walkthrough", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ManagedRoot);
        SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FwDataPath)));
    }

    public string FwDataPath { get; }

    public string ManagedRoot { get; }

    public string SourceSha256 { get; }

    public void Dispose()
    {
        try { Directory.Delete(ManagedRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
