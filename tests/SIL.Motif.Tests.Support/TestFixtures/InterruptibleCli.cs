using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SIL.Motif.Tests.TestFixtures;

public sealed class InterruptibleCli : IDisposable
{
    private readonly string _outputPath;
    private readonly string _errorPath;
    private IntPtr _processHandle;
    private readonly Task _streamCopies;

    private InterruptibleCli(Process process, IntPtr processHandle, string outputPath, string errorPath,
        Task? streamCopies = null)
    {
        Process = process;
        _processHandle = processHandle;
        _outputPath = outputPath;
        _errorPath = errorPath;
        _streamCopies = streamCopies ?? Task.CompletedTask;
    }

    public Process Process { get; }

    public int ExitCode
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return Process.ExitCode;
            if (!GetExitCodeProcess(_processHandle, out var exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return unchecked((int)exitCode);
        }
    }

    public static InterruptibleCli Start(ProcessStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "motif-interruptible-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var outputPath = Path.Combine(temporaryRoot, "cli.stdout.txt");
        var errorPath = Path.Combine(temporaryRoot, "cli.stderr.txt");
        if (!OperatingSystem.IsWindows()) return StartUnix(start, outputPath, errorPath);

        var security = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true,
        };
        var input = InvalidHandle;
        var output = InvalidHandle;
        var error = InvalidHandle;
        try
        {
            input = CreateFile("NUL", GenericRead, ShareReadWrite, ref security, OpenExisting, NormalFile, IntPtr.Zero);
            output = CreateFile(outputPath, GenericWrite, ShareReadWriteDelete, ref security, CreateAlways, NormalFile,
                IntPtr.Zero);
            error = CreateFile(errorPath, GenericWrite, ShareReadWriteDelete, ref security, CreateAlways, NormalFile,
                IntPtr.Zero);
            if (input == InvalidHandle || output == InvalidHandle || error == InvalidHandle)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Flags = UseStandardHandles,
                StandardInput = input,
                StandardOutput = output,
                StandardError = error,
            };
            var commandLine = new StringBuilder(QuoteArgument(start.FileName));
            foreach (var argument in start.ArgumentList)
                commandLine.Append(' ').Append(QuoteArgument(argument));
            var environment = string.Join('\0', start.Environment
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Key + "=" + entry.Value)) + "\0\0";
            var environmentBlock = Marshal.StringToHGlobalUni(environment);
            try
            {
                if (!CreateProcess(start.FileName, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                    NewProcessGroup | UnicodeEnvironment, environmentBlock,
                    string.IsNullOrWhiteSpace(start.WorkingDirectory) ? null : start.WorkingDirectory,
                    ref startup, out var processInfo))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                CloseHandle(processInfo.Thread);
                try
                {
                    return new InterruptibleCli(
                        System.Diagnostics.Process.GetProcessById((int)processInfo.ProcessId), processInfo.Process,
                        outputPath, errorPath);
                }
                catch
                {
                    CloseHandle(processInfo.Process);
                    throw;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(environmentBlock);
            }
        }
        catch
        {
            try { Directory.Delete(temporaryRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
        finally
        {
            if (input != InvalidHandle) CloseHandle(input);
            if (output != InvalidHandle) CloseHandle(output);
            if (error != InvalidHandle) CloseHandle(error);
        }
    }

    // SIGINT reaches the CLI's CancelKeyPress handler on Unix, as Ctrl+Break does on Windows.
    public bool Interrupt() => OperatingSystem.IsWindows()
        ? GenerateConsoleCtrlEvent(CtrlBreakEvent, (uint)Process.Id)
        : UnixKill(Process.Id, UnixInterruptSignal) == 0;

    public async Task WaitForExitAsync()
    {
        await Process.WaitForExitAsync().ConfigureAwait(false);
        await _streamCopies.ConfigureAwait(false);
    }

    public string ReadStdout() => ReadSharedText(_outputPath);

    public string ReadStderr() => ReadSharedText(_errorPath);

    private static InterruptibleCli StartUnix(ProcessStartInfo start, string outputPath, string errorPath)
    {
        start.UseShellExecute = false;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        // Created up front, so a read while the CLI is still running never finds the file missing.
        File.Create(outputPath).Dispose();
        File.Create(errorPath).Dispose();
        var process = System.Diagnostics.Process.Start(start)!;
        process.StandardInput.Close();
        var copies = Task.WhenAll(CopyToFileAsync(process.StandardOutput.BaseStream, outputPath),
            CopyToFileAsync(process.StandardError.BaseStream, errorPath));
        return new InterruptibleCli(process, IntPtr.Zero, outputPath, errorPath, copies);
    }

    private static async Task CopyToFileAsync(Stream source, string path)
    {
        await using var target = new FileStream(path, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        await source.CopyToAsync(target).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExitAsync().Wait(TimeSpan.FromSeconds(10));
            }
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
        Process.Dispose();
        if (_processHandle != IntPtr.Zero)
        {
            CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
        try { Directory.Delete(Path.GetDirectoryName(_outputPath)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

    private const int UseStandardHandles = 0x100;
    private const uint NewProcessGroup = 0x200;
    private const uint UnicodeEnvironment = 0x400;
    private const uint CtrlBreakEvent = 1;
    private const int UnixInterruptSignal = 2;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 3;
    private const uint ShareReadWriteDelete = 7;
    private const uint OpenExisting = 3;
    private const uint CreateAlways = 2;
    private const uint NormalFile = 0x80;
    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
        ref SecurityAttributes securityAttributes, uint creationDisposition, uint flags, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int UnixKill(int processId, int signal);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

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
}
