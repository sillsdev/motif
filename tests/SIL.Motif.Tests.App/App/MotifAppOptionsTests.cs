using SIL.Motif.App.Composition;
using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class MotifAppOptionsTests
{
    [Fact]
    public void TheInstalledWindowResolvesItsRootAndParserOnceThroughTheCommandLineDefaults()
    {
        var options = MotifAppOptions.ForInstallation();

        Assert.Equal(RunnerOptions.ResolveRoot(), options.ManagedRoot);
        Assert.Equal(PanGlossExecutable.TryLocate(), options.ParserPath);
        Assert.Equal(options.ManagedRoot, options.RunnerLauncher.Options.Root);
        Assert.Equal(options.ParserPath, options.RunnerLauncher.Options.ParserPath);
        Assert.Same(TimeProvider.System, options.TimeProvider);
        if (Environment.GetEnvironmentVariable(RunnerOptions.RootVariable) is null)
            Assert.Equal(RunnerOptions.DefaultRoot, options.ManagedRoot);
        if (Environment.GetEnvironmentVariable(ProcessRunnerLauncher.SuppressVariable) is null)
            Assert.IsType<ProcessRunnerLauncher>(options.RunnerLauncher);
    }
}
