using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Requests;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class AssessCommandOverloadTests
{
    private static readonly string MissingProject =
        Path.Combine(Path.GetTempPath(), "motif-missing-" + Guid.NewGuid().ToString("N"), "absent.fwdata");

    private static readonly string Root =
        Path.Combine(Path.GetTempPath(), "motif-overload-root-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ANullThirdArgumentIsTheProgressCallbackNotAParser() =>
        Assert.False(AssessCommand.Assess(new AssessRequest(MissingProject), Root, null).Succeeded);
}
