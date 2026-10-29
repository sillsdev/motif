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

        Assert.Equal("PanGloss names nothing here", row.ReachStateText);
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

public sealed class StoredAnalysisIncompleteGroupingTests
{
    private const string Code = "grammar.stored-analysis.incomplete";

    [Fact]
    public void ManyIncompleteFindingsShowAsOneRowWithOneDetailListingTheirWordforms()
    {
        var row = Assert.Single(Load(
            Finding("ajoocha", "a1"), Finding("ajoocha", "a2"), Finding("aachwɛ", "a3"), Other()).Rows
            .Cast<GrammarWarningRowViewModel>(), row => row.GroupCode == Code);

        Assert.Equal(3, row.RepeatCount);
        var detail = Assert.Single(row.Details);
        Assert.Equal(3, detail.RepeatCount);
        Assert.True(detail.HasWordforms);
        Assert.Equal("ajoocha (2 stored analyses), aachwɛ (1 stored analysis)", detail.WordformsText);
        Assert.False(detail.HasProblemParts);
        Assert.Empty(detail.SubjectParts);
    }

    [Fact]
    public void FindingsOfOtherKindsKeepTheirOwnDetails()
    {
        var row = new GrammarWarningRowViewModel([Other("x"), Other("y")]);

        Assert.Equal(2, row.Details.Count);
        Assert.All(row.Details, detail => Assert.False(detail.HasWordforms));
    }

    private static GrammarWarningsViewModel Load(params GrammarWarning[] findings)
    {
        var model = new GrammarWarningsViewModel();
        model.Load(findings);
        return model;
    }

    private static GrammarWarning Finding(string wordform, string analysis) => new(
        GrammarDiagnosticLevel.Warning, "Stored analysis not checked",
        [
            new GrammarWarningPart(wordform, GrammarWarningPartRole.Object, "w-" + wordform, "Unknown")
                { Title = wordform, SourceClass = "WfiWordform" },
            new GrammarWarningPart("Stored analysis", GrammarWarningPartRole.Object, analysis, "Unknown")
                { Title = "Stored analysis", SourceClass = "WfiAnalysis" },
        ],
        [new GrammarWarningPart($"{analysis} of {wordform} was not compared", GrammarWarningPartRole.Text)],
        $"warning: {Code}: {analysis}")
    {
        Code = Code,
        Group = "Stored analysis not checked",
        Title = "Stored analysis not checked",
        Description = $"Stored analysis {analysis} for wordform \"{wordform}\" was not compared.",
        Guidance = "Report the wordform.",
    };

    private static GrammarWarning Other(string detail = "x") => new(
        GrammarDiagnosticLevel.Warning, "Other", [],
        [new GrammarWarningPart(detail, GrammarWarningPartRole.Text)], detail)
    {
        Code = "conversion.other",
        Group = "Other",
        Description = detail,
    };
}
