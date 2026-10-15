using SIL.Motif.Commands;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class RunnerWarningTests
{
    [Fact]
    public void AWarningFromAFailedStartDoesNotOutliveALaterSuccessfulStart()
    {
        var launcher = new ScriptedLauncher();
        var context = new ServerContext("unused.fwdata", "1.0", launcher, new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);

        launcher.Warning = "no runner";
        context.StartRunner();
        Assert.Equal("no runner", context.RunnerWarning);

        launcher.Warning = null;
        context.StartRunner();
        Assert.Null(context.RunnerWarning);
    }

    private sealed class ScriptedLauncher : IJobRunnerLauncher
    {
        public string? Warning { get; set; }

        public JobRunnerLaunchOptions Options { get; } = new("unused-root", null);

        public void Start(string projectPath, Action<string>? reportWarning = null)
        {
            if (Warning is not null) reportWarning?.Invoke(Warning);
        }
    }
}
