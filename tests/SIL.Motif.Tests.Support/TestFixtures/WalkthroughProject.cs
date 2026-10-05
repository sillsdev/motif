using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

public sealed class WalkthroughProject : IDisposable
{
    public WalkthroughProject(
        PristineProjectFixture pristine, string? managedRoot = null, DateTime? sourceLastWriteUtc = null,
        Action<LcmCache, SeededProject, SeededText>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(pristine);

        var cache = pristine.NewScratch();
        try
        {
            Seed = pristine.Seed;
            Text = SeededProject.SeedText(cache, Seed);
            configure?.Invoke(cache, Seed, Text);
            TextId = Text.TextId;
            FirstMsaId = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                .GetObject(Seed.FirstEntryId).MorphoSyntaxAnalysesOC.Single().Guid;
            RealParserProject.PrepareForParsing(
                cache, "m", "o", "t", "i", "f", "a", "n", "l", "y", "s", "e", "d", "u", "b");
            new FwDataProjectLoader().Save(cache);
            FwDataPath = cache.ProjectId.Path;
        }
        finally
        {
            if (!cache.IsDisposed) cache.Dispose();
        }

        if (sourceLastWriteUtc is { } lastWriteUtc)
            File.SetLastWriteTimeUtc(FwDataPath, DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc));
        ManagedRoot = managedRoot ?? Path.Combine(Path.GetTempPath(), "SIL.Motif.Walkthrough", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ManagedRoot);
        SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FwDataPath)));
    }

    public string FwDataPath { get; }

    public SeededProject Seed { get; }

    public SeededText Text { get; }

    public Guid FirstMsaId { get; }

    public Guid TextId { get; }

    public string ManagedRoot { get; }

    public string SourceSha256 { get; }

    public void Dispose()
    {
        try { Directory.Delete(ManagedRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
