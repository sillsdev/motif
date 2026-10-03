using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed class LexiconOnlyWalkthroughProject : IDisposable
{
    public LexiconOnlyWalkthroughProject(PristineProjectFixture pristine)
    {
        var cache = pristine.NewScratch();
        try
        {
            new FwDataProjectLoader().Save(cache);
            FwDataPath = cache.ProjectId.Path;
        }
        finally
        {
            if (!cache.IsDisposed) cache.Dispose();
        }

        ManagedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.RunJourney", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ManagedRoot);
    }

    public string FwDataPath { get; }

    public string ManagedRoot { get; }

    public void Dispose()
    {
        try { Directory.Delete(ManagedRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
