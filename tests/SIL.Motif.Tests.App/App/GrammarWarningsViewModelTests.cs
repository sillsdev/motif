using System.Linq;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the grammar-warning table's per-column search over rows already in memory.</summary>
public sealed class GrammarWarningsViewModelTests
{
    private static readonly GrammarWarning EntryWarning = new(
        GrammarDiagnosticLevel.Warning, "Entry",
        [new("lex entry", GrammarWarningPartRole.Text), new("kuona", GrammarWarningPartRole.Object, "e", "Entry", "silfw://localhost/link?x")],
        [new("msa", GrammarWarningPartRole.Text), new("0c686afa-8d21-4e3b-bc0e-41812150cf4c", GrammarWarningPartRole.Missing),
         new("does not resolve within this entry", GrammarWarningPartRole.Text)],
        "warning: hc-unresolved-morph-type: msa does not resolve within this entry")
    {
        Group = "Unresolved morph type",
        Code = "hc-unresolved-morph-type",
        Origin = GrammarFindingOrigin.Import,
    };

    private static readonly GrammarWarning PhonemeWarning = new(
        GrammarDiagnosticLevel.Information, "Phoneme",
        [new("phoneme", GrammarWarningPartRole.Text), new("ng", GrammarWarningPartRole.Object, "p", "Phoneme")],
        [new("is not modelled", GrammarWarningPartRole.Text)],
        "info: hc-unused-phoneme: is not modelled")
    {
        Group = "Unused phoneme",
        Code = "hc-unused-phoneme",
        Origin = GrammarFindingOrigin.Check,
    };

    [Fact]
    public void LoadingShowsEveryRowAndCountsThem()
    {
        var table = new GrammarWarningsViewModel();

        table.Load([EntryWarning, PhonemeWarning]);

        Assert.True(table.HasAny);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("2 findings", table.CountSummary);
    }

    [Fact]
    public void AnExactlyRepeatedReportIsOneRowWithItsCount()
    {
        var table = new GrammarWarningsViewModel();

        table.Load([EntryWarning, EntryWarning, PhonemeWarning]);

        // Counts stay in reports, so the stage badge and the table agree; the table shows two rows.
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("3 findings", table.CountSummary);
        var repeated = table.Rows.Cast<GrammarWarningRowViewModel>().Single(row => row.IsRepeated);
        Assert.Equal("reported twice", repeated.RepeatText);
    }

    [Fact]
    public void ChoosingAKindCountsThatKindRatherThanAFilterMatch()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning, EntryWarning, PhonemeWarning]);

        var kind = table.WarningGroups.Concat(table.InformationGroups).MaxBy(group => group.Count)!;
        table.SelectGroupCommand.Execute(kind);

        Assert.Equal("2 findings of this kind", table.CountSummary);
        table.WhereFilter = "nothing matches this";
        Assert.Equal("0 of 3 findings match the filters", table.CountSummary);
    }

    [Fact]
    public void AKindCarriesTheParsersDescriptionAndGuidance()
    {
        var named = EntryWarning with
        {
            Group = "Unresolved grammatical info",
            Description = "The entry points at grammatical info it does not own.",
            Guidance = "Choose the entry's grammatical info again in FieldWorks.",
        };
        var table = new GrammarWarningsViewModel();

        table.Load([named, PhonemeWarning]);

        var group = table.WarningGroups.Concat(table.InformationGroups).Single(kind => kind.Name == "Unresolved grammatical info");
        Assert.Equal("The entry points at grammatical info it does not own.", group.Description);
        Assert.True(group.HasGuidance);
        Assert.False(table.WarningGroups.Concat(table.InformationGroups).Single(kind => kind != group).HasDescription);
    }

    [Fact]
    public void EachColumnFiltersOnItsOwnText()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning, PhonemeWarning]);

        table.WhereFilter = "phon";
        Assert.Equal("ng", Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).SubjectParts[1].Text);
        Assert.Equal("1 of 2 findings match the filters", table.CountSummary);

        table.WhereFilter = string.Empty;
        table.WhereFilter = "kuona";
        Assert.Equal("Unresolved morph type", Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).GroupName);

        table.WhereFilter = string.Empty;
        table.ProblemFilter = "not modelled";
        Assert.Equal("Unused phoneme", Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).GroupName);
    }

    [Fact]
    public void TheProblemFilterAlsoMatchesTheOriginalLine_SoAPastedIdentifierFindsItsRow()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning, PhonemeWarning]);

        table.ProblemFilter = "0c686afa";

        Assert.Equal("warning", Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).Severity);
    }

    [Fact]
    public void LoadingNothingClearsTheTable()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning]);

        table.Load(null);

        Assert.False(table.HasAny);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public void ImportOriginIsShownAsTheSource()
    {
        var table = new GrammarWarningsViewModel();

        table.Load([EntryWarning, PhonemeWarning]);

        var imported = Assert.Single(table.Rows.Cast<GrammarWarningRowViewModel>(),
            row => row.OriginLabel == "From import");
        Assert.Equal("From import", imported.OriginLabel);
    }
}
