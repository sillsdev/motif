using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>The private standard input supplied to each process by the test script.</summary>
/// <remarks>
/// A test process inherits its standard input from whatever started the suite, and so does every tool it runs.
/// On Windows every process holding one inherited pipe shares it: while any of them has a read pending on it,
/// git and python block at startup when they inspect their standard input, so a suite started from a pipe
/// hung on its first git or python test. The script gives each process private input holding a token it
/// also passes in <see cref="TokenVariable"/>. Windows receives a file handle; Unix receives a finite pipe.
/// </remarks>
public static class TestScriptStandardInput
{
    /// <summary>Carries the token the test script wrote into this process's standard input file.</summary>
    public const string TokenVariable = "MOTIF_TEST_STDIN_TOKEN";

    /// <summary>The token the test script wrote, or <see langword="null"/> outside a test script run.</summary>
    public static string? ExpectedToken => Environment.GetEnvironmentVariable(TokenVariable);

    /// <summary>Reads only the private input supplied by the test script, or null outside a script run.</summary>
    /// <remarks>
    /// Windows receives a file handle and reads positionally so inherited child offsets stay unchanged.
    /// Unix receives a finite pipe from PowerShell and reads only the expected token bytes, with cancellation.
    /// </remarks>
    public static async Task<string?> ReadPrivateInputAsync(CancellationToken cancellationToken)
    {
        var expectedToken = ExpectedToken;
        if (expectedToken is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            var raw = GetStdHandle(StdInputHandle);
            if (raw == -1 || raw == 0) return null;
            using var handle = new SafeFileHandle(raw, ownsHandle: false);
            if (GetFileType(handle) != FileTypeDisk) return null;
            var buffer = new byte[4096];
            var length = RandomAccess.Read(handle, buffer, fileOffset: 0);
            return Encoding.UTF8.GetString(buffer, 0, length);
        }

        using var input = Console.OpenStandardInput();
        var tokenBytes = new byte[Encoding.UTF8.GetByteCount(expectedToken)];
        await input.ReadExactlyAsync(tokenBytes, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(tokenBytes);
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
