using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ProgramTests
{
    [Fact]
    public void RunUsesTheSuppliedEnvironmentWithoutChangingTheHostEnvironment()
    {
        var key = CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable;
        var original = Environment.GetEnvironmentVariable(key);
        var workerRoot = Path.Combine(Path.GetTempPath(), "motif-program-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerRoot);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [key] = "1",
            [RunnerOptions.RootVariable] = workerRoot,
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            var exitCode = global::SIL.Motif.Cli.Program.Run(
                ["add-delete-lexeme-form"], output, error, environment, static (_, _) => { });

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains("Usage: motif add-delete-lexeme-form", error.ToString(), StringComparison.Ordinal);
            Assert.Equal("1", environment[key]);
            Assert.Equal(original, Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            if (Directory.Exists(workerRoot)) Directory.Delete(workerRoot, recursive: true);
        }
    }

    [Fact]
    public void RunWritesHelpToTheSuppliedOutputWriter()
    {
        var workerRoot = Path.Combine(Path.GetTempPath(), "motif-program-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerRoot);
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [RunnerOptions.RootVariable] = workerRoot,
            };
            var exitCode = global::SIL.Motif.Cli.Program.Run(
                ["help"], output, error, environment, static (_, _) => { });

            Assert.Equal(0, exitCode);
            Assert.Contains("Motif", output.ToString(), StringComparison.Ordinal);
            Assert.Empty(error.ToString());
        }
        finally
        {
            if (Directory.Exists(workerRoot)) Directory.Delete(workerRoot, recursive: true);
        }
    }
}
