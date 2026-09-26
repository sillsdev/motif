using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed class TwoTextWalkthroughProject : IDisposable
{
    public const string SecondTextTitle = "Second Seeded Text";

    public TwoTextWalkthroughProject(PristineProjectFixture pristine)
    {
        var cache = pristine.NewScratch();
        try
        {
            SeededProject.SeedText(cache, pristine.Seed);
            var second = SeededProject.SeedText(cache, pristine.Seed);
            var secondText = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(second.TextId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                secondText.Name.set_String(cache.DefaultAnalWs, SecondTextTitle));
            RealParserProject.PrepareForParsing(
                cache, "m", "o", "t", "i", "f", "a", "n", "l", "y", "s", "e", "d", "u", "b");
            new FwDataProjectLoader().Save(cache);
            FwDataPath = cache.ProjectId.Path;
        }
        finally
        {
            if (!cache.IsDisposed) cache.Dispose();
        }

        ManagedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.SetupWalkthrough", Guid.NewGuid().ToString("N"));
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
