using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="KnownProjectsQuery"/>: Known projects come back most-recently-seen first, and a
/// project whose <c>.fwdata</c> file is unavailable is omitted from the picker while its registry
/// entry is preserved.
/// </summary>
public sealed class KnownProjectsQueryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.KnownProjectsQueryTests", Guid.NewGuid().ToString("N"));

    private readonly ITestOutputHelper _output;

    public KnownProjectsQueryTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void ListsKnownProjectsMostRecentlySeenFirst()
    {
        var older = NewProjectFile("older.fwdata");
        var newer = NewProjectFile("newer.fwdata");
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            registry.Record("older", older, DateTimeOffset.UtcNow.AddDays(-1));
            registry.Record("newer", newer, DateTimeOffset.UtcNow);
        }

        var projects = KnownProjectsQuery.List(_root);

        Assert.Equal([newer, older], projects.Select(p => p.FullFwDataPath));
    }

    [Fact]
    public void ListsAProjectOnceAtItsLatestSightingWhateverWorkspaceKeysItWasRecordedUnder()
    {
        var project = NewProjectFile("repeated.fwdata");
        var other = NewProjectFile("other.fwdata");
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            registry.Record("first", project, DateTimeOffset.UtcNow.AddDays(-3));
            registry.Record("other", other, DateTimeOffset.UtcNow.AddDays(-2));
            registry.Record("second", project, DateTimeOffset.UtcNow.AddDays(-1));
            registry.Record("third", project, DateTimeOffset.UtcNow.AddDays(-4));
        }

        var projects = KnownProjectsQuery.List(_root);

        Assert.Equal([project, other], projects.Select(p => p.FullFwDataPath));
    }

    [Fact]
    public void CopiesWithOneLangProjectIdentityKeepEveryKnownPath()
    {
        var identity = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var olderCopy = NewProjectFile("older-copy.fwdata", identity);
        var recentCopy = NewProjectFile("recent-copy.fwdata", identity);
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            registry.Record("older-copy", olderCopy, DateTimeOffset.UtcNow.AddDays(-1));
            registry.Record("recent-copy", recentCopy, DateTimeOffset.UtcNow);
        }

        var projects = KnownProjectsQuery.List(_root);

        Assert.Equal([recentCopy, olderCopy], projects.Select(project => project.FullFwDataPath));
        Assert.All(projects, project => Assert.Equal(identity.ToString("D"), project.ProjectIdentity));
        using var reopened = MachineDatabase.Open(_root);
        Assert.Equal([recentCopy, olderCopy], new KnownProjectRegistry(reopened).List()
            .OrderByDescending(project => project.LastSeenUtc).Select(project => project.FullFwDataPath));
    }

    [Fact]
    public void ARefusedProjectStoreLeavesRecentMetadataEmpty()
    {
        var projectPath = NewProjectFile("refused.fwdata", Guid.NewGuid());
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            registry.Record("refused", projectPath, DateTimeOffset.UtcNow);
        }
        var locator = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        File.WriteAllText(ProjectDatabaseCatalog.DatabasePathFor(locator), "not a Motif database");

        var project = Assert.Single(KnownProjectsQuery.List(_root));

        Assert.Equal(projectPath, project.FullFwDataPath);
        Assert.Null(project.WordformCount);
        Assert.Null(project.StateText);
    }

    [Fact]
    public void OmitsAnUnavailableProjectWithoutForgettingIt()
    {
        var missing = Path.Combine(_root, "deleted.fwdata");
        var present = NewProjectFile("present.fwdata");
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            registry.Record("missing", missing, DateTimeOffset.UtcNow.AddDays(-1));
            registry.Record("present", present, DateTimeOffset.UtcNow);
        }

        var projects = KnownProjectsQuery.List(_root);

        Assert.Equal(present, Assert.Single(projects).FullFwDataPath);

        using var reopened = MachineDatabase.Open(_root);
        Assert.Equal(new[] { missing, present }.Order(StringComparer.Ordinal),
            new KnownProjectRegistry(reopened).List().Select(project => project.FullFwDataPath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TestProcessKnownProjectsUseItsPrivateWorkerRoot()
    {
        var processRoot = Path.GetFullPath(RunnerOptions.ResolveRoot());
        var realRoot = Path.GetFullPath(RunnerOptions.DefaultRoot);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var privateParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.WorkerRoots"));

        Assert.NotEqual(realRoot, processRoot, comparer);
        Assert.StartsWith(privateParent + Path.DirectorySeparatorChar, processRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        using var machine = MachineDatabase.Open(processRoot);

        Assert.Equal(Path.Combine(processRoot, "motif.db"), machine.FullPath);
        Assert.NotEqual(Path.Combine(realRoot, "motif.db"), machine.FullPath, comparer);
    }

    [Fact]
    public void ManyDistinctProjectsRemainSeparateAndMissingCopiesStayRegistered()
    {
        var paths = Enumerable.Range(0, 100).Select(index =>
        {
            var folder = Path.Combine(_root, "copy-" + index);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "MotifSyntheticTurkic.fwdata");
            File.WriteAllText(path, $"<languageproject><rt class=\"LangProject\" guid=\"{Guid.NewGuid():D}\" /></languageproject>");
            return path;
        }).ToArray();
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            for (var index = 0; index < paths.Length; index++)
                registry.Record("distinct-" + index, paths[index], DateTimeOffset.UtcNow.AddMinutes(index));
        }
        Assert.Equal(100, KnownProjectsQuery.List(_root).Count);
        foreach (var path in paths.Take(70)) Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);

        var projects = KnownProjectsQuery.List(_root);

        Assert.Equal(30, projects.Count);
        Assert.Equal(30, projects.Select(project => project.ProjectIdentity).Distinct().Count());
        using var reopened = MachineDatabase.Open(_root);
        Assert.Equal(paths.Order(StringComparer.Ordinal),
            new KnownProjectRegistry(reopened).List().Select(project => project.FullFwDataPath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RepeatedIdentityReadsReuseTheUnchangedLargeProject()
    {
        var path = Environment.GetEnvironmentVariable("MOTIF_RECENT_IDENTITY_BENCHMARK_PROJECT");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(_root, "large.fwdata");
            using var writer = new StreamWriter(path);
            writer.Write("<languageproject>");
            var chunk = string.Concat(Enumerable.Repeat("<rt class=\"CmObject\" />", 4096));
            for (var index = 0; index < 1024; index++) writer.Write(chunk);
            writer.Write($"<rt class=\"LangProject\" guid=\"{Guid.NewGuid():D}\" /></languageproject>");
        }
        var clock = Stopwatch.StartNew();
        var identity = ProjectFileIdentityReader.Read(path);
        var cold = clock.Elapsed;
        clock.Restart();
        var repeated = ProjectFileIdentityReader.Read(path);
        var warm = clock.Elapsed;
        _output.WriteLine($"Identity file={path}; bytes={new FileInfo(path).Length}; cold={cold.TotalMilliseconds:F1}ms; warm={warm.TotalMilliseconds:F1}ms");
        Assert.NotNull(identity);
        Assert.Equal(identity, repeated);
        Assert.True(warm < TimeSpan.FromMilliseconds(100), $"The unchanged project was rescanned: {warm}.");
    }

    [Fact]
    public void IdentityCacheChecksBothFileSizeAndLastWriteTime()
    {
        var first = Guid.NewGuid();
        var path = NewProjectFile("changing.fwdata", first);
        var originalTime = File.GetLastWriteTimeUtc(path);
        Assert.Equal(first.ToString("D"), ProjectFileIdentityReader.Read(path));
        var second = Guid.NewGuid();
        File.WriteAllText(path, $"<languageproject><rt class=\"LangProject\" guid=\"{second:D}\" /> </languageproject>");
        File.SetLastWriteTimeUtc(path, originalTime);
        Assert.Equal(second.ToString("D"), ProjectFileIdentityReader.Read(path));
        var third = Guid.NewGuid();
        File.WriteAllText(path, $"<languageproject><rt class=\"LangProject\" guid=\"{third:D}\" /> </languageproject>");
        File.SetLastWriteTimeUtc(path, originalTime.AddSeconds(2));
        Assert.Equal(third.ToString("D"), ProjectFileIdentityReader.Read(path));
    }

    [Fact]
    public void ATemporarilyLockedProjectIsIdentifiedOnTheNextReadWithoutChangingItsFile()
    {
        var identity = Guid.NewGuid();
        var path = NewProjectFile("temporarily-locked.fwdata", identity);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Null(ProjectFileIdentityReader.Read(path));

        Assert.Equal(identity.ToString("D"), ProjectFileIdentityReader.Read(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoresHeldByAnotherProcessDoNotAddPatienceForEveryRecentRow(bool sqliteWriter)
    {
        var paths = Enumerable.Range(0, 30).Select(index => NewProjectFile($"held-{index}.fwdata", Guid.NewGuid())).ToArray();
        var locks = new List<string>();
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            for (var index = 0; index < paths.Length; index++)
            {
                var locator = new ProjectLocator(paths[index], Path.GetFileNameWithoutExtension(paths[index]));
                using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current)
                    .OpenOwned(locator);
                locks.Add(database.FullPath + ".use.lock");
                registry.Record("held-" + index, paths[index], DateTimeOffset.UtcNow.AddMinutes(index));
            }
        }
        var pathsFile = Path.Combine(_root, "locks.json");
        File.WriteAllText(pathsFile, System.Text.Json.JsonSerializer.Serialize(locks));
        var script = Path.Combine(_root, "hold.ps1");
        File.WriteAllText(script, """
            param([string] $PathsFile, [string] $AssemblyDirectory, [string] $Writer)
            $ErrorActionPreference = 'Stop'
            $leases = @()
            $connections = @()
            if ($Writer -eq 'True') {
                foreach ($name in @('SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll',
                        'SQLitePCLRaw.batteries_v2.dll', 'Microsoft.Data.Sqlite.dll')) {
                    [void] [Reflection.Assembly]::LoadFrom((Join-Path $AssemblyDirectory $name))
                }
                [SQLitePCL.Batteries_V2]::Init()
            }
            try {
                foreach ($path in (Get-Content -Raw -LiteralPath $PathsFile | ConvertFrom-Json)) {
                    $sharing = if ($Writer -eq 'True') { [System.IO.FileShare]::ReadWrite -bor
                        [System.IO.FileShare]::Delete } else { [System.IO.FileShare]::None }
                    $leases += [System.IO.FileStream]::new($path, [System.IO.FileMode]::Open,
                        [System.IO.FileAccess]::Read, $sharing)
                    if ($Writer -eq 'True') {
                        $options = [Microsoft.Data.Sqlite.SqliteConnectionStringBuilder]::new()
                        $options.DataSource = $path.Substring(0, $path.Length - '.use.lock'.Length)
                        $options.Pooling = $false
                        $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new($options.ToString())
                        $connection.Open()
                        $connections += $connection
                        $command = $connection.CreateCommand()
                        $command.CommandText = 'PRAGMA journal_mode = DELETE; BEGIN EXCLUSIVE;'
                        [void] $command.ExecuteNonQuery()
                        $command.Dispose()
                    }
                }
                [Console]::WriteLine('ready')
                [void] [Console]::In.ReadLine()
            }
            finally {
                foreach ($connection in $connections) { $connection.Dispose() }
                foreach ($lease in $leases) { $lease.Dispose() }
            }
            """);
        var assemblyDirectory = AppContext.BaseDirectory;
        if (sqliteWriter)
        {
            assemblyDirectory = Path.Combine(_root, "sqlite-child");
            Directory.CreateDirectory(assemblyDirectory);
            foreach (var name in new[] { "SQLitePCLRaw.core.dll", "SQLitePCLRaw.provider.e_sqlite3.dll",
                         "SQLitePCLRaw.batteries_v2.dll", "Microsoft.Data.Sqlite.dll" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(assemblyDirectory, name));
            var nativeName = OperatingSystem.IsWindows() ? "e_sqlite3.dll"
                : OperatingSystem.IsMacOS() ? "libe_sqlite3.dylib" : "libe_sqlite3.so";
            var runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
            File.Copy(Path.Combine(AppContext.BaseDirectory, "runtimes", runtime, "native", nativeName),
                Path.Combine(assemblyDirectory, nativeName));
        }
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // pwsh writes its startup profile to XDG_CACHE_HOME on exit; on a WSL /mnt drive that outlasts 5 s.
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(_root, "pwsh-cache");
        foreach (var argument in new[] { "-NoProfile", "-File", script, pathsFile, assemblyDirectory, sqliteWriter.ToString() }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var killed = false;
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(ready == "ready", ready is null ? await error : ready);
            var clock = Stopwatch.StartNew();
            var projects = KnownProjectsQuery.List(_root);
            var elapsed = clock.Elapsed;
            _output.WriteLine($"30 held stores (SQLite writer={sqliteWriter}): {elapsed.TotalMilliseconds:F1}ms");
            Assert.Equal(30, projects.Count);
            Assert.All(projects, project => Assert.Null(project.StateText));
            Assert.True(elapsed < TimeSpan.FromMilliseconds(1500), $"Each row waited for its store: {elapsed}.");
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(5000)) { killed = true; process.Kill(entireProcessTree: true); }
            await process.WaitForExitAsync();
        }
        Assert.True(process.ExitCode == 0, $"hold.ps1 exited {process.ExitCode} (killed after 5s: {killed}). stderr: {await error}");
    }

    [Fact]
    public void OptionalStoreInspectionRejectsWritesAndNeverCreatesAMissingStore()
    {
        var path = NewProjectFile("read-only.fwdata", Guid.NewGuid());
        var locator = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(locator);
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current)
            .OpenOwned(locator);

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => MotifDatabase.ReadExisting(databasePath, locator,
            (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM MotifMetadata;";
                return command.ExecuteNonQuery();
            }));
        var missingPath = Path.Combine(_root, "missing.motif.db");
        Assert.Throws<FileNotFoundException>(() => MotifDatabase.ReadExisting(missingPath, locator, (_, _) => 0));
        Assert.False(File.Exists(missingPath));
        Assert.False(File.Exists(missingPath + ".use.lock"));
    }

    private string NewProjectFile(string name, Guid? identity = null)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, identity is { } guid
            ? $"<languageproject><rt class=\"LangProject\" guid=\"{guid:D}\" /></languageproject>"
            : string.Empty);
        return path;
    }
}
