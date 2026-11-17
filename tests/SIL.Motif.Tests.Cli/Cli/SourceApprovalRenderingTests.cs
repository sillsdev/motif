using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class SourceApprovalRenderingTests
{
    private static SourceApprovalStageResponse Staged(bool addsField) =>
        new("WEB", "pending", 1, 0, 1, 0, 0, 0, [], []) { AddsApprovalsField = addsField };

    [Fact]
    public void TheConfirmationSaysInOneSentenceWhenTheApplyAddsTheApprovalsField()
    {
        var text = SourceTextRenderer.RenderApprovalStaged(Staged(addsField: true));

        Assert.Single(text.Split(Environment.NewLine), line => line == "This Apply also adds the Motif Approvals field to your project.");
    }

    [Fact]
    public void TheConfirmationSaysNothingAboutTheFieldWhenTheProjectHasIt()
    {
        Assert.DoesNotContain("Motif Approvals field", SourceTextRenderer.RenderApprovalStaged(Staged(addsField: false)));
    }
}
