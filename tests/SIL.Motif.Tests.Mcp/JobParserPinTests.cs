using SIL.Motif.Commands;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class JobParserPinTests
{
    [Fact]
    public void TheParserAnMcpJobIsPinnedToIsTheOneTheLauncherStartsRunnersWith()
    {
        var launcher = new NoRunnerLauncher(new JobRunnerLaunchOptions("/tmp/motif-root", "/tmp/launcher-parser"));

        var context = new ServerContext("/tmp/project.fwdata", "1.0", launcher, new ActivityLog(null),
            ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: false, parserPath: "/tmp/tool-parser");

        Assert.Equal(launcher.Options.ParserPath, context.JobParserPath);
        Assert.Equal("/tmp/launcher-parser", context.ForProject("/tmp/other.fwdata").JobParserPath);
    }
}
