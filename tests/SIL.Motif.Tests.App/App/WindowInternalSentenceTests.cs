using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the owner's ruling that the window never tells a linguist an Assessment "did not record what the project
/// holds": the window asks for another parse in its own words instead.
/// </summary>
public sealed class WindowInternalSentenceTests
{
    [Fact]
    public void NoWindowSourceSaysTheAssessmentDidNotRecordWhatTheProjectHolds()
    {
        var app = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App");
        var offenders = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".axaml", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("did not record what the project holds", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(app, path))
            .ToArray();

        Assert.Empty(offenders);
    }
}
