using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class WarningRowPanGlossTextTests
{
    private const string Message = "PanGloss's own warning message.";
    private const string Guidance = "PanGloss's guidance for this finding.";

    [Fact]
    public void TheRowKeepsPanGlossMessageAndGuidanceAsSupplied()
    {
        var row = new GrammarWarningRowViewModel(Finding(Guidance));

        Assert.Equal(Message, row.Message);
        Assert.Equal(Guidance, row.PanGlossGuidance);
        Assert.True(row.HasGuidance);
        Assert.Equal(string.Empty, row.PanGlossExplanation);
        Assert.False(row.HasExplanation);
    }

    [Fact]
    public void MissingPanGlossGuidanceDoesNotCreateAnAdviceBlock()
    {
        var row = new GrammarWarningRowViewModel(Finding(null));

        Assert.False(row.HasGuidance);
        Assert.False(row.HasExplanation);
    }

    [Fact]
    public void AnUnresolvedFindingDoesNotInventAWhereToLookLine()
    {
        var warning = Finding(null) with
        {
            Subject = [],
        };
        var row = new GrammarWarningRowViewModel(warning);

        Assert.Equal("PanGloss did not name a subject for this finding", row.ReachStateText);
        Assert.DoesNotContain("look", row.ReachStateText, StringComparison.OrdinalIgnoreCase);
    }

    private static GrammarWarning Finding(string? guidance) => new(
        GrammarDiagnosticLevel.Warning,
        "Allomorph",
        [new GrammarWarningPart("kat", GrammarWarningPartRole.Object, "g", "MoForm", "silfw://localhost/link?x")],
        [new GrammarWarningPart(Message, GrammarWarningPartRole.Text)],
        Message)
    {
        Code = "conversion.unsegmentable-form",
        Group = "Allomorph form cannot be segmented",
        Description = Message,
        Guidance = guidance,
    };
}
