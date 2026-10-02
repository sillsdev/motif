using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed class ManyTextWalkthroughProject : IDisposable
{
    private const int TextCountValue = 4;

    public ManyTextWalkthroughProject(PristineProjectFixture pristine)
    {
        var cache = pristine.NewScratch();
        try
        {
            var texts = cache.ServiceLocator.GetInstance<ITextRepository>();
            for (var index = 0; index < TextCountValue; index++)
            {
                var seededText = SeededProject.SeedText(cache, pristine.Seed);
                var text = texts.GetObject(seededText.TextId);
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                    text.Name.set_String(cache.DefaultAnalWs, $"Selection Text {index + 1}"));
            }

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

    public int TextCount => TextCountValue;

    public string FwDataPath { get; }

    public string ManagedRoot { get; }

    public void Dispose()
    {
        try { Directory.Delete(ManagedRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
