using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection.Usage;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class UsageRecordingTests
{
    [Fact]
    public void EarlyCliRefusalRecordsOneInvocationWithoutArgumentValues()
    {
        var workerRoot = Path.Combine(Path.GetTempPath(), "motif-usage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerRoot);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RunnerOptions.RootVariable] = workerRoot,
            [CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1",
        };

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = global::SIL.Motif.Cli.Program.Run(
                ["new"], output, error, environment, static (_, _) => { });

            Assert.Equal(1, exitCode);
            Assert.Contains("Usage: motif new", error.ToString(), StringComparison.Ordinal);
            using var machine = MachineDatabase.Open(workerRoot);
            var entry = Assert.Single(new MachineUsageLog(machine).ReadAll());
            Assert.Equal("new", entry.Command);
            Assert.Empty(entry.ArgumentShape);
        }
        finally
        {
            if (Directory.Exists(workerRoot)) Directory.Delete(workerRoot, recursive: true);
        }
    }

    [Fact]
    public void VersionRefusedMachineStoreDoesNotStopCliFromResponding()
    {
        var workerRoot = Path.Combine(Path.GetTempPath(), "motif-usage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerRoot);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RunnerOptions.RootVariable] = workerRoot,
        };

        try
        {
            using (var machine = MachineDatabase.Open(workerRoot))
            using (var connection = machine.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version=999;";
                command.ExecuteNonQuery();
            }

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = global::SIL.Motif.Cli.Program.Run(
                ["help"], output, error, environment, static (_, _) => { });

            Assert.Equal(0, exitCode);
            Assert.NotEmpty(output.ToString());
        }
        finally
        {
            if (Directory.Exists(workerRoot)) Directory.Delete(workerRoot, recursive: true);
        }
    }

    [Fact]
    public void CorruptMachineStoreDoesNotStopHelpFromResponding()
    {
        var workerRoot = Path.Combine(Path.GetTempPath(), "motif-usage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerRoot);
        File.WriteAllText(Path.Combine(workerRoot, "motif.db"), "not a sqlite database");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RunnerOptions.RootVariable] = workerRoot,
        };

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = global::SIL.Motif.Cli.Program.Run(
                ["help"], output, error, environment, static (_, _) => { });

            Assert.Equal(0, exitCode);
            Assert.NotEmpty(output.ToString());
        }
        finally
        {
            if (Directory.Exists(workerRoot)) Directory.Delete(workerRoot, recursive: true);
        }
    }
    [Fact]
    public void UnknownCliRefusalStoresNoUserSuppliedCommandOrFlagText()
    {
        const string privateText = "PRIVATE-CLI-IDENTIFIER-47F2";
        var workerRoot = Path.Combine(Path.GetTempPath(), "motif-usage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerRoot);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RunnerOptions.RootVariable] = workerRoot,
            [CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1",
        };

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = global::SIL.Motif.Cli.Program.Run(
                [$"command-{privateText}", $"--field-{privateText}", privateText],
                output, error, environment, static (_, _) => { });

            Assert.NotEqual(0, exitCode);
            using var machine = MachineDatabase.Open(workerRoot);
            var entry = Assert.Single(new MachineUsageLog(machine).ReadAll());
            Assert.Equal("unknown", entry.Command);
            Assert.DoesNotContain(privateText, entry.Command, StringComparison.Ordinal);
            Assert.DoesNotContain(privateText, string.Join(" ", entry.ArgumentShape), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(workerRoot)) Directory.Delete(workerRoot, recursive: true);
        }
    }
}
