using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class GrammarFindingGroupsTests
{
    [Fact]
    public void ErrorDiagnosticsRemainSeparateFromWarningsAndInformation()
    {
        var error = new GrammarWarning(
            Enum.Parse<GrammarDiagnosticLevel>("Error"), "Error", [], [],
            "error: fwdata.no-usable-allomorphs: entry has no usable allomorph.")
        {
            Group = "No usable entry allomorphs",
            Code = "fwdata.no-usable-allomorphs",
        };
        var warnings = new GrammarWarningsViewModel();

        warnings.Load([error]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(warnings.Rows));
        Assert.Equal("error", row.Severity);
        Assert.Equal(1, warnings.ErrorCount);
        Assert.Equal("Entry with no usable allomorph", Assert.Single(warnings.ErrorGroups).Name);
        Assert.False(warnings.HasWarningGroups);
        Assert.Empty(warnings.WarningGroups);
        Assert.Empty(warnings.InformationGroups);
        Assert.Contains("1 error", warnings.BreakdownText, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticLevelControlsItsBucketRegardlessOfDescription()
    {
        var warning = new GrammarWarning(
            GrammarDiagnosticLevel.Warning, "Warn", [], [new GrammarWarningPart("A warning that loaded normally.", GrammarWarningPartRole.Text)],
            "warning: fwdata.warning: A warning that loaded normally.")
        {
            Group = "Warning group",
            Code = "fwdata.warning",
        };
        var info = new GrammarWarning(
            GrammarDiagnosticLevel.Information, "Info", [], [new GrammarWarningPart("This information was skipped.", GrammarWarningPartRole.Text)],
            "info: fwdata.information: This information was skipped.")
        {
            Group = "Information group",
            Code = "fwdata.information",
        };
        var warnings = new GrammarWarningsViewModel();

        warnings.Load([warning, info]);

        Assert.Equal("Warning group", Assert.Single(warnings.WarningGroups).Name);
        Assert.Equal("Information group", Assert.Single(warnings.InformationGroups).Name);
        Assert.Equal(1, warnings.WarningCount);
        Assert.Equal(1, warnings.InformationCount);
    }
}
