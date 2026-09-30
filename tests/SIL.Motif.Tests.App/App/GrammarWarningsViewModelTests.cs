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
    public void ErrorFindingsHaveTheirOwnBucketAndSeverity()
    {
        var error = EntryWarning with
        {
            Severity = GrammarDiagnosticLevel.Error,
            Text = "error: hc-unresolved-morph-type: msa does not resolve within this entry",
        };
        var table = new GrammarWarningsViewModel();

        table.Load([EntryWarning, error, PhonemeWarning]);

        Assert.Equal(1, table.ErrorCount);
        Assert.Single(table.ErrorGroups);
        table.SetBucketCommand.Execute(GrammarFindingBucket.Errors);
        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal("error", row.Severity);
        Assert.True(row.IsError);
        Assert.Equal("1 error, 1 warning, 1 information finding.", table.BreakdownText);
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
    public void AKindTheTableDoesNotKnowCarriesTheParsersDescriptionAndGuidance()
    {
        var named = EntryWarning with
        {
            Code = "grammar.future.unresolved-info",
            Group = "Unresolved grammatical info",
            Description = "The entry points at grammatical info it does not own.",
            Guidance = "Choose the entry's grammatical info again in FieldWorks.",
        };
        var table = new GrammarWarningsViewModel();

        table.Load([named, PhonemeWarning]);

        var group = table.WarningGroups.Concat(table.InformationGroups).Single(kind => kind.Name == "Unresolved grammatical info");
        Assert.Equal("The entry points at grammatical info it does not own.", group.Description);
        Assert.True(group.HasGuidance);
        Assert.Equal("No form in the lexicon uses this phoneme.",
            table.WarningGroups.Concat(table.InformationGroups).Single(kind => kind != group).Description);
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
        Assert.Equal("Morph type couldn't be found", Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).GroupName);

        table.WhereFilter = string.Empty;
        table.ProblemFilter = "not modelled";
        Assert.Equal("Phoneme never used", Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).GroupName);
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
    public void AKnownCodeReadsInMotifsPlainWordsWithTheParsersLineBeneath()
    {
        var table = new GrammarWarningsViewModel();

        table.Load([EntryWarning, PhonemeWarning]);

        var row = table.Rows.Cast<GrammarWarningRowViewModel>().Single(row => row.GroupCode == "hc-unresolved-morph-type");
        Assert.Equal("Morph type couldn't be found", row.GroupName);
        Assert.True(row.HasMeaning);
        Assert.StartsWith("An allomorph's morph type can't be found", row.Meaning, StringComparison.Ordinal);
        Assert.Equal("msa 0c686afa-8d21-4e3b-bc0e-41812150cf4c does not resolve within this entry", row.Problem);
        var group = table.WarningGroups.Single(group => group.Code == "hc-unresolved-morph-type");
        Assert.Equal("Morph type couldn't be found", group.Name);
        Assert.Equal(row.Meaning, group.Description);
    }

    [Fact]
    public void AnUnknownCodeKeepsTheParsersGroupAndSentence()
    {
        var future = EntryWarning with
        {
            Code = "grammar.future.thing", Group = "Future thing could not be loaded",
            Description = "The future thing could not be loaded.",
        };
        var table = new GrammarWarningsViewModel();

        table.Load([future]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal("Future thing could not be loaded", row.GroupName);
        Assert.False(row.HasMeaning);
        Assert.Equal("The future thing could not be loaded.", Assert.Single(table.WarningGroups).Description);
    }

    [Fact]
    public void EachRowNamesItsObjectsKindOrSaysItIsGrammarWide()
    {
        var environment = new GrammarWarning(GrammarDiagnosticLevel.Warning, string.Empty,
            [new GrammarWarningPart("e2 (/ _ [C])", GrammarWarningPartRole.Object, "g", "PhEnvironment", "silfw://localhost/link?tool=EnvironmentEdit")],
            [new GrammarWarningPart("unknown natural class \"C\"; treated as absent", GrammarWarningPartRole.Text)],
            "warning: grammar.environment.invalid: unknown natural class")
        { Code = "grammar.environment.invalid", Group = "Invalid phonological environment" };
        var nowhere = environment with { Subject = [], Text = "warning: grammar.environment.invalid: failed validation" };
        var table = new GrammarWarningsViewModel();

        table.Load([environment, nowhere]);

        var rows = table.Rows.Cast<GrammarWarningRowViewModel>().ToList();
        Assert.Equal(["Environment", "Grammar-wide"], rows.Select(row => row.KindLabel).Order(StringComparer.Ordinal));
        Assert.Equal("1×", rows[0].SeenText);
    }

    [Fact]
    public void TheLevelColumnIsNeededOnlyWhenTheShownRowsMixLevels()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning, PhonemeWarning]);

        Assert.True(table.AnyShownLevelsDiffer);
        table.SetBucketCommand.Execute(GrammarFindingBucket.Warnings);
        Assert.False(table.AnyShownLevelsDiffer);
    }

    [Fact]
    public void TheProblemFilterAlsoMatchesThePlainMeaning()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning, PhonemeWarning]);

        table.ProblemFilter = "stem or an affix";

        Assert.Equal("hc-unresolved-morph-type",
            Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).GroupCode);
    }
}
