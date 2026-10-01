using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what an opened warning row says: PanGloss's advice for its code, the steps that confirm a fix, and, for a
/// finding that names nothing, what that costs the linguist and where in FieldWorks to look instead.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WarningRowAdviceTests
{
    private const string SpellingAdvice =
        "In Lexicon > Lexicon Edit, check the named allomorph's spelling and the project's phoneme inventory.";

    private readonly AvaloniaHeadlessFixture _avalonia;

    public WarningRowAdviceTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    private static GrammarWarning Finding(GrammarDiagnosticLevel level, string? guidance, bool named) => new(
        level, "Allomorph",
        named ? [new GrammarWarningPart("kat", GrammarWarningPartRole.Object, "g", "MoForm", "silfw://localhost/link?x")] : [],
        [new GrammarWarningPart("cannot segment \"kat\"", GrammarWarningPartRole.Text)],
        "warning: conversion.unsegmentable-form: cannot segment \"kat\"")
    {
        Code = "conversion.unsegmentable-form",
        Group = "Allomorph form cannot be segmented",
        Guidance = guidance,
    };

    [Fact]
    public void ARowCarriesItsOwnAdviceRatherThanOnlyItsKind()
    {
        var row = new GrammarWarningRowViewModel(Finding(GrammarDiagnosticLevel.Warning, SpellingAdvice, named: true));

        Assert.True(row.HasAdvice);
        Assert.Equal(SpellingAdvice, row.Advice);
        Assert.True(row.ShowsFixSteps);
        Assert.False(row.NamesNoItem);
    }

    [Fact]
    public void AFindingThatNamesNothingSaysWhatThatCostsAndWhereToLook()
    {
        var row = new GrammarWarningRowViewModel(Finding(GrammarDiagnosticLevel.Warning, SpellingAdvice, named: false));

        Assert.True(row.NamesNoItem);
        Assert.Contains("can't open it in FieldWorks", row.NoItemText, StringComparison.Ordinal);
        Assert.Equal("Look in Lexicon > Lexicon Edit.", row.WhereToLookText);
    }

    [Theory]
    [InlineData("In Grammar > Environments, correct the expression for phonological environment 'the item'.",
        "Look in Grammar > Environments.")]
    [InlineData("In Lexicon > Lexicon Edit, correct the form or add the missing phoneme in Grammar > Phonemes.",
        "Look in Lexicon > Lexicon Edit and Grammar > Phonemes.")]
    [InlineData("In Grammar > Environments, add the missing environment; in Lexicon > Lexicon Edit, correct its reference.",
        "Look in Grammar > Environments and Lexicon > Lexicon Edit.")]
    [InlineData("In Grammar > Category Edit > the category's Affix Templates, check the named template's slots.",
        "Look in Grammar > Category Edit > the category's Affix Templates.")]
    [InlineData("In Words > Edit Parser Parameters..., correct the named parser setting.",
        "Look in Words > Edit Parser Parameters...")]
    [InlineData("In Lexicon > Lexicon Edit, fix one; in Lexicon > Lexicon Edit, fix the other.",
        "Look in Lexicon > Lexicon Edit.")]
    [InlineData("Restore the missing item.", "PanGloss doesn't say where in FieldWorks to look.")]
    public void WhereToLookNamesEachFieldWorksPlaceTheAdviceNamesOnce(string guidance, string whereToLook)
    {
        var row = new GrammarWarningRowViewModel(Finding(GrammarDiagnosticLevel.Warning, guidance, named: false));

        Assert.Equal(whereToLook, row.WhereToLookText);
    }

    [Fact]
    public void InformationWithNoAdviceAsksForNothingToBeFixed()
    {
        var row = new GrammarWarningRowViewModel(Finding(GrammarDiagnosticLevel.Information, null, named: false));

        Assert.False(row.HasAdvice);
        Assert.False(row.ShowsFixSteps);
        Assert.StartsWith("Nothing to change", row.Advice, StringComparison.Ordinal);
    }

    [Fact]
    public void AWarningWithNoAdviceStillSaysSoAndKeepsTheStepsToConfirmAFix()
    {
        var row = new GrammarWarningRowViewModel(Finding(GrammarDiagnosticLevel.Warning, "  ", named: true));

        Assert.False(row.HasAdvice);
        Assert.True(row.ShowsFixSteps);
        Assert.Equal("PanGloss gives no advice for this kind of finding.", row.Advice);
    }

    [Fact]
    public async Task OpeningARowShowsWhatToDoInFieldWorksAndTheStepsAfterTheFix()
    {
        var fake = new FakeCommandClient();
        fake.OnCheckGrammar((_, _) => Task.FromResult(CommandOutcome<GrammarCheckResponse>.Success(
            new GrammarCheckResponse(SeededGrammarFindings.All(), HasBaseline: true))));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(@"C:\projects\sample.fwdata");

        _avalonia.Invoke(() =>
        {
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 1240, Height = 1400 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                Assert.DoesNotContain("What to do in FieldWorks", VisibleTexts(panel));

                grid.SelectedItem = grammar.Warnings.Rows.Cast<GrammarWarningRowViewModel>()
                    .First(row => row.GroupCode == "conversion.unsegmentable-form");
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var shown = VisibleTexts(panel);
                Assert.Contains("What to do in FieldWorks", shown);
                Assert.Contains(SeededGrammarFindings.SpellingGuidance, shown);
                Assert.Contains("After you fix it", shown);
                Assert.Contains("Save your change in FieldWorks.", shown);
                Assert.Contains(shown, text => text.StartsWith("Press Refresh", StringComparison.Ordinal));
                Assert.Contains(shown, text => text.Contains("checks the grammar again", StringComparison.Ordinal));
                Assert.DoesNotContain(shown, text => text.Contains("can't open it in FieldWorks", StringComparison.Ordinal));

                grid.SelectedItem = grammar.Warnings.Rows.Cast<GrammarWarningRowViewModel>()
                    .First(row => row.NamesNoItem && row.GroupCode == "grammar.msa.no-rule-form-allomorphs");
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                shown = VisibleTexts(panel);
                Assert.Contains(shown, text => text.Contains("can't open it in FieldWorks", StringComparison.Ordinal));
                Assert.Contains("Look in Lexicon > Lexicon Edit.", shown);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task ChoosingAKindNoLongerRepeatsTheAdviceOnceForTheWholeGroup()
    {
        var fake = new FakeCommandClient();
        fake.OnCheckGrammar((_, _) => Task.FromResult(CommandOutcome<GrammarCheckResponse>.Success(
            new GrammarCheckResponse(SeededGrammarFindings.All(), HasBaseline: true))));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(@"C:\projects\sample.fwdata");

        _avalonia.Invoke(() =>
        {
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 1240, Height = 1400 };
            try
            {
                window.Show();
                grammar.Warnings.SelectGroupCommand.Execute(
                    grammar.Warnings.WarningGroups.Single(group => group.Code == "conversion.unsegmentable-form"));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var shown = VisibleTexts(panel);
                Assert.Contains("What it means", shown);
                Assert.DoesNotContain("What usually helps", shown);
                Assert.DoesNotContain(SeededGrammarFindings.SpellingGuidance, shown);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static List<string> VisibleTexts(Control root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
        .Select(block => block.Text!)
        .ToList();
}
