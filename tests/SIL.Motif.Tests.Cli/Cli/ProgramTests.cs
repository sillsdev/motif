using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ProgramTests
{
    [Fact]
    public void RunUsesTheSuppliedEnvironmentWithoutChangingTheHostEnvironment()
    {
        var key = CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable;
        var original = Environment.GetEnvironmentVariable(key);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = "1" };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = global::SIL.Motif.Cli.Program.Run(
            ["new"], output, error, environment, static (_, _) => { });

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Usage: motif new", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("1", environment[key]);
        Assert.Equal(original, Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void RunWritesHelpToTheSuppliedOutputWriter()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = global::SIL.Motif.Cli.Program.Run(
            ["help"], output, error, new Dictionary<string, string?>(), static (_, _) => { });

        Assert.Equal(0, exitCode);
        Assert.Contains("Motif", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }
}
