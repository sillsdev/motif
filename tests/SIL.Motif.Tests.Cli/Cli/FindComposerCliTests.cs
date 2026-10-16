using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Pins the <c>find-composer</c> verb through the in-process CLI.</summary>
public sealed class FindComposerCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-find-composer-" + Guid.NewGuid().ToString("N"));

    public FindComposerCliTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private (int ExitCode, string Output, string Error) Run(string arguments, bool advancedAi = true)
    {
        var result = CliInProcess.RunCommandLine(Path.Combine(_root, "machine"), FakeParser.ExecutablePath, advancedAi, arguments);
        return (result.ExitCode, result.Output, result.Error);
    }

    [Fact]
    public void AGoalSearchPrintsRankedComposersAsJson()
    {
        var result = Run("find-composer --goal \"restrict an affix to certain noun classes\" --json");

        Assert.Equal(0, result.ExitCode);
        var json = JsonNode.Parse(result.Output)!;
        Assert.Equal("goal", json["mode"]!.GetValue<string>());
        Assert.Equal("compose-edit-affix-inflection-classes", json["results"]![0]!["verb"]!.GetValue<string>());
        Assert.Equal("shape", json["results"]![0]!["schemaSource"]!.GetValue<string>());
        Assert.NotNull(json["results"]![0]!["intentSchema"]);
    }

    [Fact]
    public void ASubjectSearchPrintsText()
    {
        var result = Run("find-composer --subject sound-inventory --detail detailed");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("compose-author-phoneme - Author a phoneme", result.Output, StringComparison.Ordinal);
        Assert.Contains("Sound inventory:", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("find-composer")]
    [InlineData("find-composer --goal plural --subject sound-inventory")]
    public void AnInvocationWithoutExactlyOneOfGoalAndSubjectPrintsUsage(string arguments)
    {
        var result = Run(arguments);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Usage: motif find-composer", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownSubjectIsRefusedWithItsRegisteredCode()
    {
        var result = Run("find-composer --subject nothing-here --json");

        Assert.Equal("composer.unknown-subject", JsonNode.Parse(result.Error)!["code"]!.GetValue<string>());
    }

    [Fact]
    public void TheVerbNeedsAdvancedAiMode()
    {
        var result = Run("find-composer --goal plural --json", advancedAi: false);

        Assert.Equal("command.advanced-ai-mode-required", JsonNode.Parse(result.Error)!["code"]!.GetValue<string>());
    }
}
