using System.Diagnostics;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class WorkerStartupTests
{
    [Fact]
    public async Task WorkerStartsBeforeTheBundledIcuPayloadIsNeeded()
    {
        var staging = Path.Combine(Path.GetTempPath(), "motif-worker-no-icu-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(staging, "app");
        var root = Path.Combine(staging, "state");
        Directory.CreateDirectory(app);

        try
        {
            foreach (var file in Directory.EnumerateFiles(BuildOutput.ProductDirectory))
            {
                if (Path.GetFileName(file).StartsWith("libicu", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(app, Path.GetFileName(file)));
            }

            var worker = Path.Combine(app, OperatingSystem.IsWindows() ? "SIL.Motif.Worker.exe" : "SIL.Motif.Worker");
            Assert.True(File.Exists(worker), "The worker apphost was not copied into the isolated output.");

            var start = new ProcessStartInfo(worker)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(RunnerOptions.RootArgument);
            start.ArgumentList.Add(root);
            start.ArgumentList.Add(RunnerOptions.NamespaceArgument);
            start.ArgumentList.Add("motif-no-icu-" + Guid.NewGuid().ToString("N"));
            start.ArgumentList.Add(RunnerOptions.IdleArgument);
            start.ArgumentList.Add("60000");
            start.ArgumentList.Add(RunnerOptions.NoParserArgument);

            using var process = Process.Start(start)!;
            string? readyLine;
            try
            {
                readyLine = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }

            var error = await process.StandardError.ReadToEndAsync();
            Assert.False(string.IsNullOrWhiteSpace(readyLine), "The worker exited before announcing readiness. " + error);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
