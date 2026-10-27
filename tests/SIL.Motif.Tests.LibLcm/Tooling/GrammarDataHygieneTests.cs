using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Tooling;

public sealed class GrammarDataHygieneTests
{
    private static readonly TimeSpan ChildProcessCleanupTimeout = TimeSpan.FromSeconds(10);
    private const int MaximumChildInputCharacters = 1024 * 1024;
    private const int MaximumCapturedChildOutputCharacters = 64 * 1024;

    [Fact]
    public void RuntimeProjectAndStorePathsAreRejectedIncludingForceAddedIgnoredOutput()
    {
        var temp = NewTemp();
        try
        {
            using (var cache = NewLangProjFixture.CreateCache(Path.Combine(temp, "project")))
            {
                SeededProject.Seed(cache);
                var fwDataPath = NewLangProjFixture.FwDataPath(Path.Combine(temp, "project"));
                var fwDataBytes = File.ReadAllBytes(fwDataPath);
                var findings = GrammarDataHygiene.Inspect(fwDataPath, fwDataBytes);
                Assert.Contains(findings, f => f.Rule == "project-carrier" && f.Severity == FindingSeverity.Reject);
                Assert.Contains(GrammarDataHygiene.Inspect("renamed-project.store", fwDataBytes), f => f.Rule == "project-signature" && f.Severity == FindingSeverity.Reject);
                new FwDataProjectLoader().Save(cache);
                var savedProjectBytes = File.ReadAllBytes(fwDataPath);
                Assert.Contains(GrammarDataHygiene.Inspect("renamed-saved-project.store", savedProjectBytes),
                    f => f.Rule == "project-signature" && f.Severity == FindingSeverity.Reject);
            }

            var nearMiss = new XDocument(new XElement("languageproject", new XElement("rt",
                new XAttribute("class", "Other"), new XAttribute("guid", Guid.Empty))));
            Assert.Empty(GrammarDataHygiene.Inspect("metadata.xml", Encoding.UTF8.GetBytes(nearMiss.ToString(SaveOptions.DisableFormatting))));

            var storeBytes = Encoding.UTF8.GetBytes("runtime generated writing-system store entry");
            Assert.Contains(GrammarDataHygiene.Inspect("private/WritingSystemStore/entry.ldml", storeBytes), f => f.Severity == FindingSeverity.Reject);

            InitGit(temp);
            File.WriteAllText(Path.Combine(temp, ".gitignore"), "*.payload\n");
            var payload = RuntimePayload(temp);
            File.WriteAllBytes(Path.Combine(temp, "private.payload"), payload);
            RunGit(temp, "add", ".gitignore"); RunGit(temp, "commit", "-m", "fixture setup");
            RunGit(temp, "add", "-f", "private.payload");
            Assert.Equal(1, RunGuard("--index", temp));
        }
        finally { DeleteTemp(temp); }
    }

    [Fact]
    public void PopulatedJsonXmlAndTablesFailAfterRenamingWhileSchemaAndIdentityProbesPass()
    {
        var temp = NewTemp();
        try
        {
            var payload = RuntimePayload(temp);
            Assert.Contains(GrammarDataHygiene.Inspect("renamed.bin", payload), f => f.Rule == "populated-grammar-structure" && f.Severity == FindingSeverity.Reject);
            using var data = JsonDocument.Parse(payload);
            var records = data.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            var xml = Encoding.UTF8.GetBytes(new XElement("lexicon", records.Select(record => new XElement("entry",
                new XElement("form", record.GetProperty("form").GetString()),
                new XElement("gloss", record.GetProperty("gloss").GetString())))).ToString(SaveOptions.DisableFormatting));
            Assert.Contains(GrammarDataHygiene.Inspect("renamed.dat", xml), f => f.Severity == FindingSeverity.Reject);
            var tableText = "form\tgloss\n" + string.Join("\n", records.Select(record => record.GetProperty("form").GetString() + "\t" + record.GetProperty("gloss").GetString())) + "\n";
            var table = Encoding.UTF8.GetBytes(tableText);
            Assert.Contains(GrammarDataHygiene.Inspect("unknown.ext", table), f => f.Rule == "populated-grammar-table");

            var schema = Encoding.UTF8.GetBytes("{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"properties\":{\"entries\":{\"type\":\"array\",\"items\":{\"properties\":{\"form\":{\"type\":\"string\"}}}}}}");
            Assert.Empty(GrammarDataHygiene.Inspect("schema.json", schema));
            var probe = Encoding.UTF8.GetBytes("{\"entries\":[],\"error\":\"missing project\"}");
            Assert.Empty(GrammarDataHygiene.Inspect("probe.json", probe));
        }
        finally { DeleteTemp(temp); }
    }

    [Fact]
    public void SourceLiteralsEncodingAndContainersUseTheSameBoundedClassifier()
    {
        var temp = NewTemp();
        try
        {
            var payload = RuntimePayload(temp);
            var json = Encoding.UTF8.GetString(payload);
            var split = json.Length / 2;
            var source = "var records = " + JsonSerializer.Serialize(json[..split]) + " + " + JsonSerializer.Serialize(json[split..]) + ";";
            Assert.Contains(GrammarDataHygiene.Inspect("source.cs", Encoding.UTF8.GetBytes(source)), f => f.Severity == FindingSeverity.Reject);
            var base64 = Convert.ToBase64String(payload);
            Assert.Contains(GrammarDataHygiene.Inspect("encoded.cs", Encoding.UTF8.GetBytes("const string blob = \"" + base64 + "\";")), f => f.Severity == FindingSeverity.Reject);

            using var zipped = new MemoryStream();
            using (var archive = new ZipArchive(zipped, ZipArchiveMode.Create, true))
            using (var entry = new StreamWriter(archive.CreateEntry("records.txt").Open())) entry.Write(json);
            Assert.Contains(GrammarDataHygiene.Inspect("records.zip", zipped.ToArray()), f => f.Severity == FindingSeverity.Reject);

            var nested = payload;
            for (var i = 0; i < 3; i++) nested = Gzip(nested);
            Assert.Contains(GrammarDataHygiene.Inspect("opaque.gz", nested), f => f.Severity == FindingSeverity.Review);
            Assert.Contains(GrammarDataHygiene.Inspect("large.gz", Gzip(new byte[32 * 1024 * 1024 + 1])), f => f.Severity == FindingSeverity.Review);

            using var largeArchive = new MemoryStream();
            using (var archive = new ZipArchive(largeArchive, ZipArchiveMode.Create, true))
                for (var i = 0; i < 257; i++) archive.CreateEntry($"member-{i}.txt");
            Assert.Contains(GrammarDataHygiene.Inspect("many.zip", largeArchive.ToArray()), f => f.Rule == "opaque-wrapper" && f.Severity == FindingSeverity.Review);
        }
        finally { DeleteTemp(temp); }
    }

    [Fact]
    public void EverySupportedEncodedRunIsInspectedUntilTheByteBudgetRequiresReview()
    {
        var temp = NewTemp();
        try
        {
            var payload = RuntimePayload(temp);
            var base64Decoy = Convert.ToBase64String(Enumerable.Repeat((byte)0xff, 40).ToArray());
            var base64Text = string.Join("\n", Enumerable.Repeat(base64Decoy, 33).Append(Convert.ToBase64String(payload)));
            Assert.Contains(GrammarDataHygiene.Inspect("many-base64.txt", Encoding.UTF8.GetBytes(base64Text)),
                f => f.Severity == FindingSeverity.Reject);

            if (payload.Length % 2 == 0) payload = payload.Append((byte)' ').ToArray();
            var hexDecoy = Convert.ToHexString(Enumerable.Repeat((byte)0xff, 33).ToArray());
            var hexText = string.Join("\n", Enumerable.Repeat(hexDecoy, 33).Append(Convert.ToHexString(payload)));
            Assert.Contains(GrammarDataHygiene.Inspect("many-hex.txt", Encoding.UTF8.GetBytes(hexText)),
                f => f.Severity == FindingSeverity.Reject);

            using var compressed = new MemoryStream();
            var oversizedEncodedRun = Convert.ToBase64String(Enumerable.Repeat((byte)0xff, 18 * 1024 * 1024).ToArray());
            using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, true))
            using (var writer = new StreamWriter(gzip, Encoding.ASCII, 8192, true)) writer.Write(oversizedEncodedRun);
            Assert.Contains(GrammarDataHygiene.Inspect("encoded-budget.gz", compressed.ToArray()),
                f => f.Rule == "opaque-wrapper" && f.Severity == FindingSeverity.Review);
        }
        finally { DeleteTemp(temp); }
    }

    [Fact]
    public void LegitimateSchemaDiagnosticsCodePinsImagesAndUnknownExtensionsPass()
    {
        var schema = Encoding.UTF8.GetBytes("<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:element name=\"entries\" type=\"xs:string\"/></xs:schema>");
        Assert.Empty(GrammarDataHygiene.Inspect("schema.xml", schema));
        Assert.Empty(GrammarDataHygiene.Inspect("diagnostic.json", Encoding.UTF8.GetBytes("{\"error\":\"no project\",\"details\":{\"entries\":[]}}")));
        Assert.Empty(GrammarDataHygiene.Inspect("example.cs", Encoding.UTF8.GetBytes("if (name == \"entries\") return false; // API example only")));
        Assert.Empty(GrammarDataHygiene.Inspect("pins.lock", Encoding.UTF8.GetBytes("sha256:0123456789abcdef0123456789abcdef")));
        Assert.Empty(GrammarDataHygiene.Inspect("image.png", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0 }));
        Assert.Empty(GrammarDataHygiene.Inspect("opaque.weird", Enumerable.Range(0, 64).Select(i => (byte)(i * 37)).ToArray()));
        var repository = FindRepository();
        foreach (var relative in new[] { "site/fixtures/help-export.json", "tests/conformance/proposal-digest/014-create-text/canonical.json" })
            Assert.DoesNotContain(GrammarDataHygiene.Inspect(relative, File.ReadAllBytes(Path.Combine(repository, relative))), f => f.Severity == FindingSeverity.Reject);
    }

    [Fact]
    public void IndexWorktreeAndAlternateIndexInspectTheirOwnBytesAndConflictsFailOperationally()
    {
        var temp = NewTemp();
        try
        {
            InitGit(temp);
            File.WriteAllText(Path.Combine(temp, "candidate.txt"), "{}");
            RunGit(temp, "add", "candidate.txt"); RunGit(temp, "commit", "-m", "initial");
            File.WriteAllBytes(Path.Combine(temp, "candidate.txt"), RuntimePayload(temp)); RunGit(temp, "add", "candidate.txt");
            File.WriteAllText(Path.Combine(temp, "candidate.txt"), "{}");
            Assert.Equal(1, RunGuard("--index", temp));
            Assert.Equal(0, RunGuard("--worktree", temp));
            File.WriteAllBytes(Path.Combine(temp, "untracked.txt"), RuntimePayload(temp));
            Assert.Equal(1, RunGuard("--worktree", temp));
            File.Delete(Path.Combine(temp, "untracked.txt"));

            var alt = Path.Combine(temp, "alternate.index");
            RunGit(temp, ["read-tree", "--empty"], environment: new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = alt }, allowFailure: false);
            File.WriteAllBytes(Path.Combine(temp, "candidate.txt"), RuntimePayload(temp));
            RunGit(temp, ["add", "-f", "candidate.txt"], environment: new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = alt }, allowFailure: false);
            Assert.Equal(1, RunGuard("--index", temp, environment: new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = alt }));

            var conflict = Path.Combine(temp, "conflict"); InitGit(conflict); BuildConflict(conflict);
            Assert.Equal(2, RunGuard("--index", conflict));
        }
        finally { DeleteTemp(temp); }
    }

    [Fact]
    public void RangeFindsAddThenDeleteAndReusedHistoricalBlobButExcludesPreBaseCleanup()
    {
        var temp = NewTemp();
        try
        {
            InitGit(temp);
            var payload = RuntimePayload(temp);
            File.WriteAllBytes(Path.Combine(temp, "old.txt"), payload); RunGit(temp, "add", "old.txt"); RunGit(temp, "commit", "-m", "historical payload");
            File.Delete(Path.Combine(temp, "old.txt")); RunGit(temp, "add", "-u"); RunGit(temp, "commit", "-m", "cleanup baseline");
            var baseSha = GitOutput(temp, "rev-parse", "HEAD");
            File.WriteAllText(Path.Combine(temp, "clean.txt"), "clean"); RunGit(temp, "add", "clean.txt"); RunGit(temp, "commit", "-m", "clean change");
            var cleanHead = GitOutput(temp, "rev-parse", "HEAD");
            Assert.Equal(0, RunGuard("--range", temp, baseSha, cleanHead));
            Assert.Equal(0, RunGuard("--tip", temp, cleanHead));

            File.WriteAllBytes(Path.Combine(temp, "reintroduced.txt"), payload); RunGit(temp, "add", "reintroduced.txt"); RunGit(temp, "commit", "-m", "reintroduce old blob");
            File.Delete(Path.Combine(temp, "reintroduced.txt")); RunGit(temp, "add", "-u"); RunGit(temp, "commit", "-m", "remove again");
            var head = GitOutput(temp, "rev-parse", "HEAD");
            Assert.Equal(0, RunGuard("--tip", temp, head));
            Assert.Equal(1, RunGuard("--range", temp, baseSha, head));
            Assert.Equal(2, RunGuard("--range", temp, "not-a-commit", head));
        }
        finally { DeleteTemp(temp); }
    }

    [Fact]
    public void NulPathsAndSymlinksDoNotCausePrivateTargetsToBeFollowed()
    {
        var temp = NewTemp();
        string? outside = null;
        try
        {
            InitGit(temp);
            var oddName = "space and-ñ\nline.txt";
            File.WriteAllBytes(Path.Combine(temp, oddName), RuntimePayload(temp));
            RunGit(temp, "add", "--", oddName);
            Assert.Equal(1, RunGuard("--index", temp));
            Assert.Equal(1, RunGuard("--worktree", temp));

            outside = Path.Combine(Path.GetDirectoryName(temp)!, "outside-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(outside, RuntimePayload(temp));
            var link = Path.Combine(temp, "external-link.txt");
            File.CreateSymbolicLink(link, outside);
            RunGit(temp, "add", "external-link.txt");
            Assert.DoesNotContain(GrammarDataHygiene.Inspect("external-link.txt", Encoding.UTF8.GetBytes(outside)), f => f.Severity == FindingSeverity.Reject);
            RunGit(temp, ["rm", "--cached", "--", oddName], environment: null, allowFailure: false);
            File.Delete(Path.Combine(temp, oddName));
            Assert.Equal(0, RunGuard("--worktree", temp));
        }
        finally
        {
            if (outside is not null) File.Delete(outside);
            DeleteTemp(temp);
        }
    }

    [Fact]
    public void WorktreeScanDoesNotReadTrackedChildrenThroughSymlinkedParents()
    {
        if (OperatingSystem.IsWindows()) return;
        var temp = NewTemp();
        var outside = NewTemp();
        try
        {
            InitGit(temp);
            var parent = Path.Combine(temp, "linked-parent");
            Directory.CreateDirectory(parent);
            File.WriteAllText(Path.Combine(parent, "child.json"), "{}");
            RunGit(temp, "add", "linked-parent/child.json"); RunGit(temp, "commit", "-m", "track nested child");

            var externalFile = Path.Combine(outside, "child.json");
            File.WriteAllBytes(externalFile, RuntimePayload(temp));
            var outsideBefore = SnapshotFixtureTree(outside);
            Directory.Delete(parent, true);
            Directory.CreateSymbolicLink(parent, outside);

            Assert.Equal(0, RunGuard("--worktree", temp));
            Assert.Equal(outsideBefore, SnapshotFixtureTree(outside));
        }
        finally
        {
            DeleteTemp(temp);
            DeleteTemp(outside);
        }
    }

    [Fact]
    public async Task HookInstallerIsWorktreeLocalIdempotentAndDelegatesOriginalPreCommit()
    {
        if (OperatingSystem.IsWindows()) return;
        var temp = NewTemp();
        try
        {
            CopyGuardTools(temp);
            InitGit(temp);
            var hookDir = Path.Combine(temp, ".git", "hooks"); Directory.CreateDirectory(hookDir);
            var trace = Path.Combine(temp, "hook-trace.txt");
            var original = Path.Combine(hookDir, "pre-commit");
            File.WriteAllText(original, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$HOOK_TRACE.args\"\ncat > \"$HOOK_TRACE.stdin\"\n");
            File.SetUnixFileMode(original, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var groupOnly = Path.Combine(hookDir, "group-only");
            File.WriteAllText(groupOnly, "#!/bin/sh\nexit 99\n");
            File.SetUnixFileMode(groupOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead);
            Assert.True(await NativeExecutableAccessAsync(original, temp));
            var groupOnlyExecutable = await NativeExecutableAccessAsync(groupOnly, temp);
            var versionedHook = Path.Combine(temp, "tools/hooks/pre-commit");
            File.SetUnixFileMode(versionedHook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            Assert.False((File.GetUnixFileMode(versionedHook) & UnixFileMode.UserExecute) != 0);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(original)));
            RunPowerShell(temp, Path.Combine(temp, "tools/Install-GrammarDataHook.ps1"), "-RepositoryRoot", temp);
            var configured = GitOutput(temp, "config", "--worktree", "--get", "core.hooksPath");
            RunPowerShell(temp, Path.Combine(temp, "tools/Install-GrammarDataHook.ps1"), "-RepositoryRoot", temp);
            Assert.Equal(configured, GitOutput(temp, "config", "--worktree", "--get", "core.hooksPath"));
            Assert.Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(original))));
            var groupOnlyInstalled = File.Exists(Path.Combine(configured, "group-only"));
            Assert.Equal(groupOnlyExecutable, groupOnlyInstalled);

            var adapterHook = Path.Combine(configured, "pre-commit");
            Assert.True((File.GetUnixFileMode(adapterHook) & UnixFileMode.UserExecute) != 0, $"installed adapter mode: {File.GetUnixFileMode(adapterHook)}");
            var result = await RunProcessAsync("sh", [adapterHook, "first", "second"], temp, "private stdin marker\n", new Dictionary<string, string?> { ["HOOK_TRACE"] = trace }, TimeSpan.FromSeconds(30));
            Assert.True(!result.TimedOut && result.ChildExited && result.ExitCode == 0, $"timed out: {result.TimedOut}; exited: {result.ChildExited}; exit: {result.ExitCode}\nstdout: {result.Output}\nstderr: {result.Error}");
            Assert.Equal("first\nsecond\n", File.ReadAllText(trace + ".args"));
            Assert.Equal("private stdin marker\n", File.ReadAllText(trace + ".stdin"));
        }
        finally { DeleteTemp(temp); }
    }

    [Fact]
    public async Task ProcessHelperBoundsAndReapsANonExitingChild()
    {
        var temp = NewTemp();
        Process? child = null;
        var childId = 0;
        var childStartedAtUtcTicks = 0L;
        Task<(int ExitCode, string Output, string Error, bool TimedOut, bool ChildExited)>? run = null;
        try
        {
            var script = "$stdinText = [Console]::In.ReadToEnd(); [Console]::Out.WriteLine('child-ready:' + $stdinText.Trim()); [Console]::Error.WriteLine('child-error-ready'); [System.Threading.ManualResetEventSlim]::new($false).Wait()";
            run = RunProcessAsync("pwsh", ["-NoProfile", "-Command", script], temp, "private input marker\n", new Dictionary<string, string?>(), TimeSpan.FromSeconds(3), process =>
            {
                child = process;
                childId = process.Id;
                childStartedAtUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            });
            var result = await run.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(result.TimedOut, $"non-exiting child was not timed out; exit: {result.ExitCode}\nstdout: {result.Output}\nstderr: {result.Error}");
            Assert.True(result.ChildExited, $"timed-out child was not reaped; stdout: {result.Output}\nstderr: {result.Error}");
            Assert.Contains("child-ready:private input marker", result.Output);
            Assert.Contains("child-error-ready", result.Error);
        }
        finally
        {
            Exception? cleanupError = null;
            try { await StopChildTreeAsync(child, childId, childStartedAtUtcTicks); }
            catch (Exception exception) { cleanupError = exception; }
            try
            {
                if (run is not null) await run.WaitAsync(ChildProcessCleanupTimeout);
            }
            catch (Exception exception) { cleanupError ??= exception; }
            finally { DeleteTemp(temp); }
            if (cleanupError is not null) throw new InvalidOperationException("The bounded child-process regression could not finish cleanup.", cleanupError);
        }
    }

    private static byte[] RuntimePayload(string temp)
    {
        var projectRoot = NewTemp();
        try
        {
            using var cache = NewLangProjFixture.CreateCache(projectRoot);
            var seed = SeededProject.Seed(cache);
            var repository = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
            var entries = new[] { seed.FirstEntryId, seed.SecondEntryId }.Select(id =>
            {
                var entry = repository.GetObject(id);
                return new
                {
                    form = entry.LexemeFormOA!.Form.get_String(cache.DefaultVernWs).Text,
                    gloss = entry.SensesOS[0].Gloss.get_String(cache.DefaultAnalWs).Text,
                };
            }).ToArray();
            return JsonSerializer.SerializeToUtf8Bytes(new { entries });
        }
        finally { DeleteTemp(projectRoot); }
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream(); using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true)) gzip.Write(bytes);
        return output.ToArray();
    }

    private static int RunGuard(string mode, string root, params string[] revisions) => RunGuard(mode, root, revisions, null);
    private static int RunGuard(string mode, string root, IDictionary<string, string?> environment) => RunGuard(mode, root, Array.Empty<string>(), environment);

    private static int RunGuard(string mode, string root, string[] revisions, IDictionary<string, string?>? environment)
    {
        var args = mode switch
        {
            "--index" or "--worktree" => new[] { mode, "--repo", root },
            "--tip" => new[] { "--tip", revisions.Single(), "--repo", root },
            "--range" => new[] { "--range", revisions[0], revisions[1], "--repo", root },
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        var prior = environment?.ToDictionary(p => p.Key, p => Environment.GetEnvironmentVariable(p.Key));
        try
        {
            if (environment is not null) foreach (var pair in environment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            return GrammarDataHygiene.Run(args);
        }
        finally
        {
            if (prior is not null) foreach (var pair in prior) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private static void InitGit(string root)
    {
        Directory.CreateDirectory(root);
        RunGit(root, "init", "--initial-branch=main");
        RunGit(root, "config", "user.name", "Grammar guard test");
        RunGit(root, "config", "user.email", "grammar-guard@example.invalid");
    }

    private static void BuildConflict(string root)
    {
        File.WriteAllText(Path.Combine(root, "conflict.txt"), "base"); RunGit(root, "add", "conflict.txt"); RunGit(root, "commit", "-m", "conflict base");
        RunGit(root, "checkout", "-b", "other"); File.WriteAllText(Path.Combine(root, "conflict.txt"), "other"); RunGit(root, "commit", "-am", "other side");
        RunGit(root, "checkout", "main"); File.WriteAllText(Path.Combine(root, "conflict.txt"), "main"); RunGit(root, "commit", "-am", "main side");
        RunGit(root, ["merge", "other"], environment: null, allowFailure: true);
    }

    private static void CopyGuardTools(string root)
    {
        var source = FindRepository();
        foreach (var relative in new[] { "tools/Check-GrammarData.ps1", "tools/Install-GrammarDataHook.ps1", "tools/MotifToolEnvironment.psm1", "tools/hooks/pre-commit", "tools/GrammarDataHygiene/grammar-data-hygiene.cs", "tools/GrammarDataHygiene/Directory.Build.props", "tools/GrammarDataHygiene/Directory.Build.targets" })
        {
            var destination = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(Path.Combine(source, relative), destination);
        }
    }

    private static void RunGit(string root, params string[] args) => RunGit(root, args, null, false);

    private static void RunGit(string root, string[] args, IDictionary<string, string?>? environment, bool allowFailure)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        if (environment is not null) foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        var result = ToolProcess.Run(start);
        if (result.ExitCode != 0 && !allowFailure) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {result.Error}");
    }

    private static string GitOutput(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        var result = ToolProcess.Run(start);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Error);
        return result.Output.Trim();
    }

    private static void RunPowerShell(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = cwd };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-File");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var result = ToolProcess.Run(start);
        Assert.True(result.ExitCode == 0, result.Error + result.Output);
    }

    private static async Task<bool> NativeExecutableAccessAsync(string path, string cwd)
    {
        var result = await RunProcessAsync("sh", ["-c", "test -x \"$1\"", "motif-hook-access", path], cwd, "",
            new Dictionary<string, string?>(), TimeSpan.FromSeconds(10));
        Assert.True(!result.TimedOut && result.ChildExited && result.ExitCode is 0 or 1,
            $"native executable check failed; timed out: {result.TimedOut}; exited: {result.ChildExited}; exit: {result.ExitCode}\nstdout: {result.Output}\nstderr: {result.Error}");
        return result.ExitCode == 0;
    }

    private static string[] SnapshotFixtureTree(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal)
        .Select(path =>
        {
            var relative = Path.GetRelativePath(root, path);
            if (Directory.Exists(path)) return $"{relative}|directory|{Directory.GetLastWriteTimeUtc(path).Ticks}";
            var info = new FileInfo(path);
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            return $"{relative}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{digest}";
        }).ToArray();

    private static async Task<(int ExitCode, string Output, string Error, bool TimedOut, bool ChildExited)> RunProcessAsync(
        string file,
        string[] args,
        string cwd,
        string input,
        IDictionary<string, string?> environment,
        TimeSpan timeout,
        Action<Process>? processStarted = null)
    {
        if (input.Length > MaximumChildInputCharacters) throw new ArgumentOutOfRangeException(nameof(input), "Child-process input exceeds the bounded test-helper limit.");
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var start = new ProcessStartInfo(file) { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start child process '{file}'.");
        processStarted?.Invoke(process);
        using var ioCancellation = new CancellationTokenSource();
        var outputTask = CaptureChildOutputAsync(process.StandardOutput, ioCancellation.Token);
        var errorTask = CaptureChildOutputAsync(process.StandardError, ioCancellation.Token);
        var inputTask = WriteChildInputAsync(process.StandardInput, input, ioCancellation.Token);
        var exitTask = process.WaitForExitAsync();
        var timedOut = false;
        Exception? processIoError = null;
        try { await Task.WhenAll(inputTask, exitTask, outputTask, errorTask).WaitAsync(timeout); }
        catch (TimeoutException) { timedOut = true; }
        catch (Exception exception) { processIoError = exception.GetBaseException(); }

        if (timedOut)
        {
            Exception? terminationError = null;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited) { }
            catch (InvalidOperationException exception) { terminationError = exception; }
            catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
            catch (System.ComponentModel.Win32Exception exception) when (!process.HasExited) { terminationError = exception; }

            try { await process.WaitForExitAsync().WaitAsync(ChildProcessCleanupTimeout); }
            catch (TimeoutException exception)
            {
                ioCancellation.Cancel();
                process.StandardOutput.Dispose();
                process.StandardError.Dispose();
                process.StandardInput.Dispose();
                try { await Task.WhenAll(inputTask, outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (Exception) { }
                var output = CompletedOutputOrDiagnostic(outputTask, "stdout");
                var error = CompletedOutputOrDiagnostic(errorTask, "stderr");
                throw new TimeoutException($"Child '{file}' did not exit within {timeout} or cleanup within {ChildProcessCleanupTimeout}. Termination error: {terminationError?.Message ?? "none"}.\nstdout: {output}\nstderr: {error}", exception);
            }
            processIoError ??= terminationError;

            try { await Task.WhenAll(inputTask, outputTask, errorTask).WaitAsync(ChildProcessCleanupTimeout); }
            catch (TimeoutException)
            {
                ioCancellation.Cancel();
                process.StandardOutput.Dispose();
                process.StandardError.Dispose();
                process.StandardInput.Dispose();
                try { await Task.WhenAll(inputTask, outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (Exception) { }
            }
            catch (Exception exception) { processIoError ??= exception.GetBaseException(); }
        }

        var childExited = process.HasExited;
        if (!childExited)
        {
            ioCancellation.Cancel();
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            process.StandardInput.Dispose();
            throw new TimeoutException($"Child '{file}' has not exited; stdout: {CompletedOutputOrDiagnostic(outputTask, "stdout")}\nstderr: {CompletedOutputOrDiagnostic(errorTask, "stderr")}");
        }

        var capturedOutput = CompletedOutputOrDiagnostic(outputTask, "stdout");
        var capturedError = CompletedOutputOrDiagnostic(errorTask, "stderr");
        if (timedOut && processIoError is not null) capturedError += $"\n[child I/O or termination diagnostic: {processIoError.Message}]";
        if (processIoError is not null && !timedOut)
            throw new InvalidOperationException($"Child '{file}' I/O failed.\nstdout: {capturedOutput}\nstderr: {capturedError}", processIoError);
        return (process.ExitCode, capturedOutput, capturedError, timedOut, childExited);
    }

    private static async Task WriteChildInputAsync(StreamWriter input, string text, CancellationToken cancellationToken)
    {
        await input.WriteAsync(text.AsMemory(), cancellationToken);
        await input.FlushAsync(cancellationToken);
        input.Close();
    }

    private static async Task<string> CaptureChildOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;
                var take = Math.Min(read, MaximumCapturedChildOutputCharacters - output.Length);
                if (take > 0) output.Append(buffer, 0, take);
                if (take < read) truncated = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { truncated = true; }
        if (truncated) output.AppendLine().Append("[captured output truncated after ").Append(MaximumCapturedChildOutputCharacters).Append(" characters]");
        return output.ToString();
    }

    private static string CompletedOutputOrDiagnostic(Task<string> capture, string stream)
    {
        if (!capture.IsCompleted) return $"<{stream} capture did not finish before the cleanup deadline>";
        try { return capture.GetAwaiter().GetResult(); }
        catch (Exception exception) { return $"<{stream} capture failed: {exception.GetBaseException().Message}>"; }
    }

    private static async Task StopChildTreeAsync(Process? child, int childId, long childStartedAtUtcTicks)
    {
        if (child is not null)
        {
            try
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(ChildProcessCleanupTimeout);
                return;
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            catch (TimeoutException) { }
        }
        if (childId <= 0 || childStartedAtUtcTicks <= 0) return;
        try
        {
            using var process = Process.GetProcessById(childId);
            if (process.StartTime.ToUniversalTime().Ticks != childStartedAtUtcTicks) return;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync().WaitAsync(ChildProcessCleanupTimeout);
        }
        catch (ArgumentException) { }
    }

    private static string FindRepository()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Motif.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Motif repository root was not found.");
    }

    private static string NewTemp()
    {
        var path = Path.Combine(Path.GetTempPath(), "MotifGrammarGuard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }

    private static void DeleteTemp(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
