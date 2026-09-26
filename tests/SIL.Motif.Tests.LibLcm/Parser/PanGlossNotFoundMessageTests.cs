using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// When no parser is found, the refusal names every place discovery looked, in the order it looked, so a
/// person can see where to put PanGloss.
/// </summary>
public sealed class PanGlossNotFoundMessageTests
{
    private static readonly string ApplicationDirectory = Path.Combine(Path.GetTempPath(), "motif-app");
    private static readonly string RepositoryRoot = Path.Combine(Path.GetTempPath(), "motif-repo");

    [Fact]
    public void AShippedMotifLooksOnlyBesideItself()
    {
        var places = PanGlossExecutable.PlacesSearched(
            configuredPath: null, ApplicationDirectory, "pangloss.exe", repositoryRoot: null);

        Assert.Equal([Path.GetFullPath(Path.Combine(ApplicationDirectory, "pangloss.exe"))], places);
    }

    [Fact]
    public void ACheckoutLooksAtTheSiblingBuildsBeforeBesideItself()
    {
        var places = PanGlossExecutable.PlacesSearched(
            configuredPath: null, ApplicationDirectory, "pangloss.exe", RepositoryRoot);

        var sibling = Path.GetFullPath(Path.Combine(RepositoryRoot, "..", "PanGloss"));
        Assert.Equal(
            [
                Path.Combine(sibling, "dist", "<version>", "pangloss.exe"),
                Path.Combine(sibling, "rust", "target", "release", "pangloss.exe"),
                Path.GetFullPath(Path.Combine(ApplicationDirectory, "pangloss.exe")),
            ],
            places);
    }

    [Fact]
    public void AConfiguredPathIsTheOnlyPlaceLooked()
    {
        var configured = Path.Combine(Path.GetTempPath(), "elsewhere", "pangloss.exe");

        var places = PanGlossExecutable.PlacesSearched(configured, ApplicationDirectory, "pangloss.exe", RepositoryRoot);

        Assert.Equal([Path.GetFullPath(configured)], places);
    }

    [Fact]
    public void TheNotFoundMessageNamesThePlacesLooked()
    {
        var message = PanGlossExecutable.NotFoundMessageFor(
            [@"C:\Motif\pangloss.exe", @"C:\Other\pangloss.exe"]);

        Assert.StartsWith("Could not find the pangloss executable.", message, StringComparison.Ordinal);
        Assert.Contains(@"Looked for it at: C:\Motif\pangloss.exe; C:\Other\pangloss.exe.", message,
            StringComparison.Ordinal);
    }
}
