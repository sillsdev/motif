using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ProjectInitializationRenderingTests
{
    private static readonly string CopyPath = Path.Combine(Path.GetTempPath(), "motif-render", "recovery.fwdata");

    [Theory]
    [InlineData("initialized")]
    [InlineData("already-initialized")]
    public void SuccessNamesARetainedCopyWithoutTheRestoreStep(string status)
    {
        var outcome = CommandOutcome<ProjectInitializationResponse>.Success(
            new ProjectInitializationResponse(status, "MotifHumanJudgment", CopyPath));

        var rendered = CommandTextRenderer.Render(outcome, asJson: false).Output;

        Assert.Contains(CopyPath, rendered);
        Assert.DoesNotContain("Close FieldWorks, then copy", rendered);
    }
}
