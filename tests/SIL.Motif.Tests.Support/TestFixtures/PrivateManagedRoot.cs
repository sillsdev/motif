using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Host;
using SIL.Motif.Worker;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Gives a test its own managed root for the flow that calls it. Tests in one process share one machine database,
/// and a Known project registered there is opened by every other test's scan of the registry, which can create
/// <c>-wal</c> and <c>-shm</c> files in the project's folder while its test deletes the folder.
/// </summary>
public static class PrivateManagedRoot
{
    /// <summary>
    /// Makes <c>RunnerOptions.ResolveRoot()</c> return a folder inside <paramref name="directory"/> until the result
    /// is disposed. The folder goes with <paramref name="directory"/> when the test deletes it.
    /// </summary>
    public static IDisposable Use(string directory)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            environment[(string)entry.Key] = (string?)entry.Value;
        environment[RunnerOptions.RootVariable] = Path.Combine(directory, "managed-root");
        return AmbientEnvironment.Push(environment);
    }

    /// <summary>
    /// As <see cref="Use"/> in a new folder under the temporary directory, which disposing the result deletes
    /// after restoring the environment.
    /// </summary>
    public static IDisposable UseTemporary(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new Scope(Use(directory), directory);
    }

    private sealed class Scope(IDisposable environment, string directory) : IDisposable
    {
        public void Dispose()
        {
            environment.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
