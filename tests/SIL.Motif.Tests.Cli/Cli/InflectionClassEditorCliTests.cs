using System;
using System.IO;
using System.Reflection;
using SIL.Motif.Commands.Catalog;
using System.Text.Json.Serialization;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class InflectionClassEditorCliTests
{
    [Theory]
    [InlineData("compose-edit-part-of-speech-inflection-class-default")]
    [InlineData("compose-edit-affix-inflection-classes")]
    [InlineData("compose-edit-stem-allomorph-stem-name")]
    [InlineData("compose-edit-stem-inflection-class")]
    public void EditorVerbsRemainDraftCommandsButAreNotAgentToolsAndReachCliUsage(string verb)
    {
        var descriptor = Assert.Single(CommandCatalog.All, command => command.Name == verb);
        Assert.Equal(CommandSurface.AdvancedAi, descriptor.Surface);
        Assert.Equal(AgentClass.Draft, descriptor.Agent);
        Assert.False(descriptor.ExposeAsAgentTool);
        Assert.False(descriptor.IsAgentTool);

        Assert.NotNull(typeof(CommandDescriptor).GetProperty(nameof(CommandDescriptor.ExposeAsAgentTool))?
            .GetCustomAttribute<JsonIgnoreAttribute>());

        var root = Path.Combine(Path.GetTempPath(), "motif-inflection-editor-cli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = CliInProcess.Run(root, null, true, verb);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains($"Usage: motif {verb}", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("compose-edit-compound-rule")]
    [InlineData("compose-set-no-default-compounding")]
    public void EditorVerbsThatAreDraftAgentToolsStillReachCliUsage(string verb)
    {
        var descriptor = Assert.Single(CommandCatalog.All, command => command.Name == verb);
        Assert.Equal(CommandSurface.AdvancedAi, descriptor.Surface);
        Assert.Equal(AgentClass.Draft, descriptor.Agent);
        Assert.True(descriptor.ExposeAsAgentTool);
        Assert.True(descriptor.IsAgentTool);

        var root = Path.Combine(Path.GetTempPath(), "motif-inflection-editor-cli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = CliInProcess.Run(root, null, true, verb);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains($"Usage: motif {verb}", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExistingAdvancedAiDraftCommandsKeepDefaultAgentExposure()
    {
        var descriptor = new CommandDescriptor("existing", typeof(string), typeof(string),
            CommandSurface.AdvancedAi, AgentClass.Draft);

        Assert.True(descriptor.ExposeAsAgentTool);
        Assert.True(descriptor.IsAgentTool);
    }
}
