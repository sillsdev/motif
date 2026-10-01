using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>The standard input the test script gives each test process, instead of the one it was started with.</summary>
/// <remarks>
/// A test process inherits its standard input from whatever started the suite, and so does every tool it runs.
/// On Windows every process holding one inherited pipe shares it: while any of them has a read pending on it,
/// git and python block at startup when they inspect their standard input, so a suite started from a pipe
/// hung on its first git or python test. The script gives each process a file of its own holding a token it
/// also passes in <see cref="TokenVariable"/>, so no test process shares its standard input with anything.
/// </remarks>
public static class TestScriptStandardInput
{
    /// <summary>Carries the token the test script wrote into this process's standard input file.</summary>
    public const string TokenVariable = "MOTIF_TEST_STDIN_TOKEN";

    /// <summary>The token the test script wrote, or <see langword="null"/> outside a test script run.</summary>
    public static string? ExpectedToken => Environment.GetEnvironmentVariable(TokenVariable);

    /// <summary>
    /// This process's standard input when it is a file, read from its start; <see langword="null"/> when it is a
    /// pipe, a console or absent, none of which is read, since reading one could block.
    /// </summary>
    public static string? ReadIfFile()
    {
        var raw = OperatingSystem.IsWindows() ? GetStdHandle(StdInputHandle) : 0;
        if (raw == -1 || (OperatingSystem.IsWindows() && raw == 0)) return null;
        using var handle = new SafeFileHandle(raw, ownsHandle: false);
        if (OperatingSystem.IsWindows() ? GetFileType(handle) != FileTypeDisk : !IsSeekable(handle)) return null;

        // A positional read leaves the offset every inheriting child shares where it was.
        var buffer = new byte[4096];
        var length = RandomAccess.Read(handle, buffer, fileOffset: 0);
        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static bool IsSeekable(SafeFileHandle handle)
    {
        try
        {
            using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 0);
            return stream.CanSeek;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private const int StdInputHandle = -10;
    private const int FileTypeDisk = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetFileType(SafeFileHandle handle);
}

/// <summary>Skips outside a test script run, which is the only one that gives a process its own standard input.</summary>
public sealed class TestScriptFactAttribute : FactAttribute
{
    public TestScriptFactAttribute()
    {
        if (TestScriptStandardInput.ExpectedToken is null)
            Skip = "Only the test script gives a test process a standard input of its own; this run was started directly.";
    }
}
