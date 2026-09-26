using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AssessRoundTripArgvTests : IDisposable
{
    private const string DeveloperCommandsVariable = "MOTIF_DEVELOPER_COMMANDS";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-assess-roundtrip-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot;
    private readonly PristineProjectFixture _pristine;

    public AssessRoundTripArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _workerRoot = Path.Combine(_root, "worker");
        Directory.CreateDirectory(_workerRoot);
    }

    [Fact]
    public async Task AssessAsJsonStoresARunThatTimingAndOverviewRead()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var setup = await RunAsync(null, "selection", "set-default", "--project", project,
            "--name", "Default", "--add-words", "motifa", "--json");
        Assert.Equal(0, setup.ExitCode);

        var parser = CopyFakeParser(new { words = new[] { new { word = "motifa", outcome = "complete" } } });
        var assessed = await RunAsync(parser, "assess", project, "--json");

        Assert.Equal(0, assessed.ExitCode);
        var response = ProjectionJson.Deserialize<AssessCommandResponse>(assessed.Output)!;
        Assert.Contains("motifa", response.Selection.Words);
        Assert.NotEmpty(response.AssessmentIds);

        var timingResult = await RunAsync(null, "timing", "--project", project, "--json");
        Assert.Equal(0, timingResult.ExitCode);
        var timing = ProjectionJson.Deserialize<TimingResponse>(timingResult.Output)!;
        Assert.Contains(timing.AssessmentId, response.AssessmentIds);
        Assert.Contains(timing.Words, row => row.Word == "motifa");

        var overviewResult = await RunAsync(null, "overview", "--project", project, "--json");
        Assert.Equal(0, overviewResult.ExitCode);
        var overview = ProjectionJson.Deserialize<OverviewResponse>(overviewResult.Output)!;
        Assert.Equal(timing.AssessmentId, overview.AssessmentId);
    }

    [Fact]
    public async Task AssessWithAWordsFileMeasuresOnlyThoseWords()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var wordsPath = Path.Combine(_root, "words.txt");
        File.WriteAllLines(wordsPath, ["motifa", "motifb"]);
        var parser = CopyFakeParser(new
        {
            words = new[]
            {
                new { word = "motifa", outcome = "complete" },
                new { word = "motifb", outcome = "complete" },
            },
        });

        var assessed = await RunAsync(parser, "assess", project, "--words", wordsPath, "--json");

        Assert.Equal(0, assessed.ExitCode);
        var response = ProjectionJson.Deserialize<AssessCommandResponse>(assessed.Output)!;
        Assert.Equal(new[] { "motifa", "motifb" }, response.Selection.Words);
        Assert.Equal(new[] { "motifa", "motifb" }, response.Words.Select(row => row.Word));
    }

    [Fact]
    public async Task InterruptingAssessStopsTheParserAndStoresNothing()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var wordsPath = Path.Combine(_root, "words.txt");
        File.WriteAllText(wordsPath, "motifa" + Environment.NewLine);
        var heartbeat = Path.Combine(_root, "parser-heartbeat.txt");
        var parser = CopyFakeParser(new { heartbeatPath = heartbeat });
        var start = StartCli(parser, createNoWindow: false,
            "assess", project, "--words", wordsPath, "--json");
        var launched = StartIsolatedCli(start);
        using var process = launched.Process;

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!File.Exists(heartbeat) && !process.HasExited && DateTime.UtcNow < deadline)
                await Task.Delay(25);
            if (!File.Exists(heartbeat))
            {
                var exited = process.HasExited;
                if (!exited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                var startupError = ReadSharedText(launched.ErrorPath);
                var startupOutput = ReadSharedText(launched.OutputPath);
                Assert.Fail($"The fake parser did not start its held batch. Exit={(exited ? process.ExitCode : "running")}; " +
                    $"Parser={parser}; Behavior={File.ReadAllText(Path.Combine(Path.GetDirectoryName(parser)!, "_fake-pangloss.json"))}; " +
                    startupOutput + startupError);
            }
            Assert.False(process.HasExited, "The CLI exited before cancellation was sent.");

            Assert.True(GenerateConsoleCtrlEvent(1, (uint)process.Id),
                $"Could not send Ctrl+Break to the CLI process group: {Marshal.GetLastWin32Error()}.");

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var error = ReadSharedText(launched.ErrorPath);
            var output = ReadSharedText(launched.OutputPath);
            var failure = ProjectionJson.Deserialize<FailureEnvelope>(error)!;
            Assert.Equal(2, process.ExitCode);
            Assert.Equal("assessment.cancelled", failure.Code);
            Assert.Equal(FailureReason.Cancelled, failure.Reason);
            Assert.Empty(output);
            Assert.Equal(0L, ReadAssessmentCount(project));
            Assert.Equal(0L, ReadInvocationCount(project));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task CaptureBaseline(string project)
    {
        var result = await RunAsync(null, "baseline", "capture", project, "--json");
        Assert.Equal(0, result.ExitCode);
    }

    private string CopyFakeParser(object behavior)
    {
        var directory = Path.Combine(_root, "fake-pangloss-" + Guid.NewGuid().ToString("N"));
        var parser = FakeParser.Copy(directory);
        FakeParser.BehaveBesideExecutable(parser, behavior);
        return parser;
    }

    private async Task<CliRun> RunAsync(string? parserPath, params string[] arguments)
    {
        var start = StartCli(parserPath, true, arguments);
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        return new CliRun(process.ExitCode, await outputTask, await errorTask);
    }

    private ProcessStartInfo StartCli(string? parserPath, bool createNoWindow, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = createNoWindow,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        start.Environment[PanGlossExecutable.PathVariable] = parserPath ?? FakeParser.ExecutablePath;
        start.Environment.Remove(DeveloperCommandsVariable);
        start.Environment.Remove("FAKE_PANGLOSS_BEHAVIOUR_PATH");
        return start;
    }

    private static long ReadAssessmentCount(string projectPath) => ReadCount(projectPath, "Assessments");

    private static long ReadInvocationCount(string projectPath) => ReadCount(projectPath, "AssessmentInvocations");

    private static long ReadCount(string projectPath, string table)
    {
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }

    private IsolatedCli StartIsolatedCli(ProcessStartInfo start)
    {
        var outputPath = Path.Combine(_root, "cli.stdout.txt");
        var errorPath = Path.Combine(_root, "cli.stderr.txt");
        var security = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true,
        };
        var input = CreateFile("NUL", 0x80000000, 3, ref security, 3, 0x80, IntPtr.Zero);
        var output = CreateFile(outputPath, 0x40000000, 7, ref security, 2, 0x80, IntPtr.Zero);
        var error = CreateFile(errorPath, 0x40000000, 7, ref security, 2, 0x80, IntPtr.Zero);
        if (input == new IntPtr(-1) || output == new IntPtr(-1) || error == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var startup = new StartupInfo
        {
            Size = Marshal.SizeOf<StartupInfo>(),
            Flags = 0x100,
            StandardInput = input,
            StandardOutput = output,
            StandardError = error,
        };
        var commandLine = new StringBuilder(QuoteArgument(start.FileName));
        foreach (var argument in start.ArgumentList)
        {
            commandLine.Append(' ').Append(QuoteArgument(argument));
        }
        var environment = string.Join('\0', start.Environment.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Key + "=" + entry.Value)) + "\0\0";
        var environmentBlock = Marshal.StringToHGlobalUni(environment);
        try
        {
            if (!CreateProcess(start.FileName, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                0x200 | 0x400, environmentBlock, null, ref startup, out var processInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(processInfo.Thread);
            try
            {
                return new IsolatedCli(Process.GetProcessById((int)processInfo.ProcessId), outputPath, errorPath);
            }
            finally
            {
                CloseHandle(processInfo.Process);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(environmentBlock);
            CloseHandle(input);
            CloseHandle(output);
            CloseHandle(error);
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(char.IsWhiteSpace) && !argument.Contains('"')) return argument;
        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"') builder.Append('\\', backslashes * 2 + 1);
            else builder.Append('\\', backslashes);
            builder.Append(character);
            backslashes = 0;
        }
        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }

    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
        ref SecurityAttributes securityAttributes, uint creationDisposition, uint flags, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Count;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    private sealed record IsolatedCli(Process Process, string OutputPath, string ErrorPath);

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
