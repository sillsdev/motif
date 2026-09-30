using System.Diagnostics;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

public sealed class RequiresUnixFactAttribute : FactAttribute
{
    public RequiresUnixFactAttribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Skip = "Unix capture permissions are only available on Linux and macOS.";
    }
}

public sealed class UnixPanGlossCaptureTests
{
    [RequiresUnixFact]
    public async Task LiveCaptureFilesArePrivateAndRemovedWhenTheChildExits()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        var captureDirectory = Path.Combine(Path.GetTempPath(), "motif-pangloss-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(captureDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var ready = Path.Combine(captureDirectory, "ready");
        var outputToken = "motif-out-" + Guid.NewGuid().ToString("N");
        var errorToken = "motif-err-" + Guid.NewGuid().ToString("N");
        string[] captures = [];
        string? standardOutput = null;
        string? standardError = null;
        try
        {
            var startInfo = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"printf '%s' {ShellQuote(outputToken)}; " +
                $"printf '%s' {ShellQuote(errorToken)} >&2; : > {ShellQuote(ready)}; sleep 60");
            using var containment = new UnixPanGlossJob(4UL * 1024 * 1024 * 1024,
                OperatingSystem.IsLinux(), captureDirectory);
            var process = containment.Start(startInfo);
            try
            {
                await WaitUntilAsync(() => File.Exists(ready), TimeSpan.FromSeconds(15));
                captures = Directory.GetFiles(captureDirectory, "motif-pangloss-*.out");

                Assert.Equal(2, captures.Length);
                foreach (var path in captures)
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }
            finally
            {
                try
                {
                    containment.Terminate(process);
                    await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
                    standardOutput = await process.ReadStandardOutputAsync();
                    standardError = await process.ReadStandardErrorAsync();
                }
                finally
                {
                    process.Dispose();
                }
            }

            Assert.Equal(outputToken, standardOutput);
            Assert.Equal(errorToken, standardError);
            Assert.All(captures, path => Assert.False(File.Exists(path)));
        }
        finally
        {
            try
            {
                Directory.Delete(captureDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The parser condition was not met.");
            await Task.Delay(20);
        }
    }
}
