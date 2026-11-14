using Xunit;

namespace SIL.Motif.Tests.App;

[Trait("MotifTestLevel", "System")]
public sealed class AppStartupSmokeSystemTests
{
    [Fact]
    public async Task TheBuiltAppsSmokeRunRendersAWindowAndExitsCleanly()
    {
        using var process = AppSmokeLauncher.Start();
        var error = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The app's --smoke run did not exit within two minutes.");
        }

        Assert.True(process.ExitCode == 0,
            $"The app's --smoke run exited {process.ExitCode}. stderr: {await error}");
    }
}
