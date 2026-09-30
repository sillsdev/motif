using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Help;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class HelpCommandTests
{
    [Fact]
    public void HelpListsReleasedCommandsWithTitles()
    {
        var result = Run("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^Motif \(tech demo\) (?:—|-) report problems at https://github\.com/sillsdev/motif/issues", result.Output);
        Assert.Contains("Open a project", result.Output, StringComparison.Ordinal);
        Assert.Contains("Measure a Selection", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Create a Draft", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AppViewsAndCliHelpContainNoUserVisibleBetaCopy()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var viewsRoot = Path.Combine(repositoryRoot, "src", "SIL.Motif.App", "Views");
        var betaViews = Directory.EnumerateFiles(viewsRoot, "*.axaml", SearchOption.AllDirectories)
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\bbeta\b", RegexOptions.IgnoreCase))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();
        Assert.Empty(betaViews);

        var result = Run("help");
        Assert.Equal(0, result.ExitCode);
        Assert.False(Regex.IsMatch(result.Output, @"\bbeta\b", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void HelpForACommandPrintsItsTitleDescriptionUsageAndUrl()
    {
        var result = Run("help", "assess");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Measure a Selection", result.Output, StringComparison.Ordinal);
        Assert.Contains("PanGloss", result.Output, StringComparison.Ordinal);
        Assert.Contains("Usage: motif assess", result.Output, StringComparison.Ordinal);
        Assert.Contains("https://motif-docs.pages.dev/reference/commands/assess/", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void FullHelpPrintsTheHelpPageAndResolvesCommandLinks()
    {
        var result = Run("help", "assess", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("When to use", result.Output, StringComparison.Ordinal);
        Assert.Contains("motif help overview", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void FullHelpResolvesAQualifiedNestedGuide()
    {
        var result = Run("help", "guide:agents/start-here", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Using Motif from an agent", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Expanded help: motif help guide:agents/start-here --full", result.Output, StringComparison.Ordinal);
        Assert.Contains("Call Motif as", result.Output, StringComparison.Ordinal);
        Assert.Contains("https://motif-docs.pages.dev/guide/agents/start-here/", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonHelpExportsAQualifiedGuideWithItsHierarchicalCodeAndFullPage()
    {
        var result = Run("help", "guide:learn/stems-and-the-lexicon", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var entry = json.RootElement;

        Assert.Equal("guide", entry.GetProperty("kind").GetString());
        Assert.Equal("learn/stems-and-the-lexicon", entry.GetProperty("code").GetString());
        Assert.Equal("learn/stems-and-the-lexicon", entry.GetProperty("slug").GetString());
        Assert.Contains(
            "# Stems and the lexicon", entry.GetProperty("helpPage").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("/learn/stems-and-the-lexicon/", entry.GetProperty("url").GetString());
    }

    [Fact]
    public void GuideQualifierSelectsTheGuideWhenItsCodeMatchesAnotherEntry()
    {
        var result = Run("help", "guide:overview", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("guide", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("overview", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Overview", json.RootElement.GetProperty("title").GetString());
        Assert.EndsWith("/guide/overview/", json.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void FullHelpAcceptsACommandCodeThatContainsAFlagLikePart()
    {
        var result = Run("help", "apply", "--all-pending", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("When to use it", result.Output, StringComparison.Ordinal);
        Assert.Contains("What it prints", result.Output, StringComparison.Ordinal);
        Assert.Contains("motif help overview", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedHelpCallUsesTheInvocationErrorExitCode()
    {
        var result = Run("help", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif help", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownHelpNameKeepsTheUnknownNameExitCode()
    {
        var result = Run("help", "not-a-released-command");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("No released command, glossary term, or Guide", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void HandoffUsageIncludesTheTextsOption()
    {
        var descriptor = Assert.Single(CliVerbCatalog.All, item => item.CommandName == "handoff");

        var usage = Assert.Single(descriptor.UsageLines);
        Assert.Contains("OR motif handoff", usage, StringComparison.Ordinal);
        Assert.Contains("--no-assess [--texts <id,…>]", usage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dry-run --wait")]
    [InlineData("trial --wait")]
    public void JobWaitUsageIncludesTheWaitTimeoutOption(string command)
    {
        var descriptor = Assert.Single(CliVerbCatalog.All, item => item.CommandName == command);

        Assert.Contains("--wait-timeout-ms <ms>", Assert.Single(descriptor.UsageLines), StringComparison.Ordinal);
    }

    [Fact]
    public void CompoundCommandUsageDoesNotRepeatTheExecutableName()
    {
        var result = Run("help", "jobs", "move");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: motif jobs move", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Usage: motif motif", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonHelpEmitsOneCompleteCommandEntry()
    {
        var result = Run("help", "assess", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var entry = json.RootElement;

        Assert.Equal("command", entry.GetProperty("kind").GetString());
        Assert.Equal("assess", entry.GetProperty("code").GetString());
        Assert.Equal("assess", entry.GetProperty("slug").GetString());
        Assert.Equal("Measure a Selection", entry.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("description").GetString()));
        Assert.Contains("PanGloss", entry.GetProperty("helpPage").GetString(), StringComparison.Ordinal);
        Assert.NotEmpty(entry.GetProperty("usage").EnumerateArray());
        Assert.Equal("Released", entry.GetProperty("surface").GetString());
        Assert.EndsWith("/reference/commands/assess/", entry.GetProperty("url").GetString());
    }

    [Fact]
    public void AllJsonExportsEveryHelpEntryInTheSharedShape()
    {
        var result = Run("help", "--all", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal("en", root.GetProperty("locale").GetString());
        Assert.Equal("https://motif-docs.pages.dev", root.GetProperty("siteRoot").GetString());

        var entries = root.GetProperty("entries").EnumerateArray().ToArray();
        var commands = entries.Where(entry => entry.GetProperty("kind").GetString() == "command").ToArray();
        var guides = entries.Where(entry => entry.GetProperty("kind").GetString() == "guide").ToArray();
        Assert.Equal(
            CommandCatalog.All.Where(command => command.Surface == CommandSurface.Released)
                .Select(command => command.Name).Order(StringComparer.Ordinal),
            commands.Select(entry => entry.GetProperty("code").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(entries, entry =>
        {
            Assert.True(entry.TryGetProperty("kind", out _));
            Assert.True(entry.TryGetProperty("code", out _));
            Assert.True(entry.TryGetProperty("slug", out _));
            Assert.True(entry.TryGetProperty("title", out _));
            Assert.True(entry.TryGetProperty("description", out _));
            Assert.True(entry.TryGetProperty("helpPage", out _));
            Assert.True(entry.TryGetProperty("url", out _));
        });
        Assert.All(commands, entry =>
        {
            Assert.True(entry.TryGetProperty("usage", out _));
            Assert.Equal("Released", entry.GetProperty("surface").GetString());
        });
        var catalogGuides = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"))
            .Entries.Where(entry => entry.Kind == HelpEntryKind.Guide)
            .Select(entry => entry.Code).Order(StringComparer.Ordinal);
        Assert.Equal(catalogGuides, guides.Select(entry => entry.GetProperty("code").GetString()!)
            .Order(StringComparer.Ordinal));
        Assert.Contains(entries, entry => entry.GetProperty("kind").GetString() == "guide"
            && entry.GetProperty("code").GetString() == "agents/start-here"
            && entry.GetProperty("url").GetString() == "https://motif-docs.pages.dev/guide/agents/start-here/");
        Assert.Contains(entries, entry => entry.GetProperty("kind").GetString() == "guide"
            && entry.GetProperty("code").GetString() == "learn/index"
            && entry.GetProperty("url").GetString() == "https://motif-docs.pages.dev/learn/");
    }

    private static CliRun Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment.Remove(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable);

        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30000), "The CLI did not exit within its bound.");
        return new CliRun(process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
