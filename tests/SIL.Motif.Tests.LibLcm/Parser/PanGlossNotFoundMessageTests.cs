using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// When no parser is found, the refusal names every place discovery looked, in the order it looked, so a
/// person can see where to put PanGloss.
/// </summary>
public sealed class PanGlossNotFoundMessageTests
{
    private static readonly string ParserFileName = OperatingSystem.IsWindows() ? "pangloss.exe" : "pangloss";
    private static readonly string ApplicationDirectory = Path.Combine(Path.GetTempPath(), "motif-app");
    private static readonly string RepositoryRoot = Path.Combine(Path.GetTempPath(), "motif-repo");

    [Fact]
    public void AShippedMotifLooksOnlyBesideItself()
    {
        var places = PanGlossExecutable.PlacesSearched(
            configuredPath: null, ApplicationDirectory, ParserFileName, repositoryRoot: null);

        Assert.Equal([Path.GetFullPath(Path.Combine(ApplicationDirectory, ParserFileName))], places);
    }

    [Fact]
    public void ACheckoutLooksAtTheSiblingBuildsBeforeBesideItself()
    {
        var places = PanGlossExecutable.PlacesSearched(
            configuredPath: null, ApplicationDirectory, ParserFileName, RepositoryRoot);

        var sibling = Path.GetFullPath(Path.Combine(RepositoryRoot, "..", "PanGloss"));
        Assert.Equal(
            [
                Path.Combine(sibling, "dist", "<version>", ParserFileName),
                Path.Combine(sibling, "rust", "target", "release", ParserFileName),
                Path.GetFullPath(Path.Combine(ApplicationDirectory, ParserFileName)),
            ],
            places);
    }

    [Fact]
    public void AConfiguredPathIsTheOnlyPlaceLooked()
    {
        var configured = Path.Combine(Path.GetTempPath(), "elsewhere", ParserFileName);

        var places = PanGlossExecutable.PlacesSearched(configured, ApplicationDirectory, ParserFileName, RepositoryRoot);

        Assert.Equal([Path.GetFullPath(configured)], places);
    }

    [Fact]
    public void TheNotFoundMessageNamesThePlacesLooked()
    {
        var first = Path.Combine(Path.GetTempPath(), "Motif", ParserFileName);
        var second = Path.Combine(Path.GetTempPath(), "Other", ParserFileName);
        var message = PanGlossExecutable.NotFoundMessageFor([first, second]);

        Assert.StartsWith("Could not find the pangloss executable.", message, StringComparison.Ordinal);
        Assert.Contains($"Looked for it at: {first}; {second}.", message, StringComparison.Ordinal);
    }
}
