using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class CommandClientOptionsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "motif-client-root");

    [Fact]
    public void ALauncherForAnotherRootOrParserIsRefused()
    {
        var elsewhere = new NoRunnerLauncher(new JobRunnerLaunchOptions(Root + "-elsewhere", null));
        var otherParser = new NoRunnerLauncher(new JobRunnerLaunchOptions(Root,
            OperatingSystem.IsWindows() ? "other-pangloss.exe" : "other-pangloss"));

        Assert.Throws<ArgumentException>(() => new CommandClient(new CommandClientOptions(Root, null, elsewhere)));
        Assert.Throws<ArgumentException>(() => new CommandClient(new CommandClientOptions(Root, null, otherParser)));
        _ = new CommandClient(new CommandClientOptions(Root, null,
            new NoRunnerLauncher(new JobRunnerLaunchOptions(Root, null))));
    }
}
