using System.Text.RegularExpressions;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ComposeVerbDispatchTests
{
    [Fact]
    public void EveryVerbOfTheSoundSystemComposeBlockHasItsOwnArmAndNoComposerIsTheDefault()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SIL.Motif.Cli", "Program.cs"));
        var split = source.IndexOf("var soundOutcome", StringComparison.Ordinal);
        Assert.True(split > 0, "The sound-system compose block was not found.");
        var start = source.LastIndexOf("break;", split, StringComparison.Ordinal);
        var end = source.IndexOf("break;", split, StringComparison.Ordinal);
        var block = source[start..end];
        split -= start;
        var labels = Regex.Matches(block[..split], "case \"(compose-[a-z-]+)\":").Select(m => m.Groups[1].Value).ToList();
        var arms = block[split..];

        Assert.NotEmpty(labels);
        foreach (var verb in labels)
            Assert.Matches("\"" + Regex.Escape(verb) + "\"\\s*=>", arms);
        Assert.DoesNotMatch("_\\s*=>\\s*ProposalCommands\\.Compose", arms);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not find Motif.sln.");
    }
}
