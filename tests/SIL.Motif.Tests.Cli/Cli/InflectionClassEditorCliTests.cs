using System;
using System.IO;
using SIL.Motif.Commands.Catalog;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class InflectionClassEditorCliTests
{
    [Theory]
    [InlineData("compose-edit-part-of-speech-inflection-class-default")]
    [InlineData("compose-edit-affix-inflection-classes")]
    public void EditorVerbsAreAdvancedAiDraftCommandsAndReachTheirUsage(string verb)
    {
        var descriptor = Assert.Single(CommandCatalog.All, command => command.Name == verb);
        Assert.Equal(CommandSurface.AdvancedAi, descriptor.Surface);
        Assert.Equal(AgentClass.Draft, descriptor.Agent);

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
}
