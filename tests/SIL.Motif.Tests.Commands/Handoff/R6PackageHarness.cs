using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Handoff;

/// <summary>Skips unless a destination folder and a real parser are both configured.</summary>
public sealed class HandoffPackageRequestedFactAttribute : FactAttribute
{
    public HandoffPackageRequestedFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(R6PackageHarness.OutputVariable)))
            Skip = $"Set {R6PackageHarness.OutputVariable} to the folder this should write.";
        else if (PanGlossExecutable.TryLocate() is null)
            Skip = $"Set {PanGlossExecutable.PathVariable} to a real parser.";
    }
}

/// <summary>
/// Writes one Handoff folder on demand, so a reader given nothing else can be asked whether the package
/// explains itself. Not part of the ordinary suite: it exists to produce a fixture, not to assert one.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class R6PackageHarness(PristineProjectFixture pristine) : IDisposable
{
    internal const string OutputVariable = "MOTIF_R6_OUTPUT";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.R6Harness", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [HandoffPackageRequestedFact]
    public void WriteOneHandoffFolderForAReaderWithNoOtherContext()
    {
        var destination = Environment.GetEnvironmentVariable(OutputVariable)!;
        Directory.CreateDirectory(_root);
        var cache = pristine.NewScratch();
        try
        {
            SeededProject.SeedText(cache, pristine.Seed);
            RealParserProject.PrepareForParsing(
                cache, "m", "o", "t", "i", "f", "a", "n", "l", "y", "s", "e", "d", "u");
            new FwDataProjectLoader().Save(cache);

            var projectPath = cache.ProjectId.Path;
            cache.Dispose();

            var outcome = HandoffCommand.Handoff(
                new HandoffRequest(projectPath, destination, new SelectionRequest(true, [], [], false, null), true),
                onProgress: null);

            Assert.True(outcome.Succeeded, outcome.Refusal?.Message ?? "no refusal");
        }
        finally
        {
            if (!cache.IsDisposed) cache.Dispose();
        }
    }
}
