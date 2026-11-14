using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Compare;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Concurrent Rationale edits resolved through the shared viewer, entirely headless (ADR 0099).</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class RationaleCompareTests
{
    private const string Original = "The first reason is clear. The second reason is sound.";
    private const string Mine = "The new reason is clear. The second reason is sound.";
    private const string Theirs = "The other reason is clear. The second reason is checked.";
    private const string Merged = "The new reason is clear. The second reason is checked.";

    [Fact]
    public void AnAgentRevisesDuringAnEditSaveWarnsCompareDecidesAndDoneSavesTheMerge()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, editor) = await EditingAsync();
            Revise(fake, Theirs, "agent-1");
            var view = new RationaleEditor { DataContext = editor };
            var owner = new Window { Content = view };
            using var closer = owner.CloseOnExit();
            owner.Show();

            await editor.SaveCommand.ExecuteAsync(null);

            Assert.Equal(Theirs, fake.StoredRationale);
            Assert.True(editor.IsEditing);
            Assert.Equal(Mine, editor.Text());
            Assert.True(editor.IsChangedElsewhere);
            Assert.Equal("This Rationale changed while you were editing", MotifAppStrings.RationaleChangedElsewhere);
            var compare = view.FindControl<Button>("RationaleCompareButton")!;
            Assert.True(compare.IsVisible);
            Assert.True(compare.IsEnabled);
            Assert.Equal("Compare…", compare.Content);
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var window = Assert.Single(owner.OwnedWindows.OfType<CompareWindow>());
            Assert.True(view.Field!.IsReadOnly);
            Assert.False(editor.SaveCommand.CanExecute(null));
            Assert.False(window.IsDoneEnabled);
            Assert.Contains("agent-1", window.SummaryText, StringComparison.Ordinal);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "agent-1");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "FW");
            var both = Assert.Single(window.Comparison.Undecided);
            Assert.Contains("agent-1", AutomationProperties.GetName(window.MarkOf(both)!), StringComparison.Ordinal);
            window.Comparison.Choose(both, Choice.Mine);
            Assert.True(window.Finish());
            await editor.Working;

            Assert.Equal(2, fake.SetRationaleRequests.Count);
            Assert.Equal(Theirs, fake.SetRationaleRequests[^1].ExpectedRationale);
            Assert.Equal(Merged, fake.StoredRationale);
            Assert.Equal(Merged, editor.Markdown);
            Assert.False(editor.IsEditing);
            Assert.False(editor.IsChangedElsewhere);
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelOrClosingCompareKeepsThePersonsUnsavedText(bool close)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, editor) = await EditingAsync();
            Revise(fake, Theirs, "agent-1");
            await editor.SaveCommand.ExecuteAsync(null);
            await editor.CompareAsync(comparison =>
            {
                var window = new CompareWindow("Rationale", comparison.Comparison, labels: comparison.Labels);
                using var closer = window.CloseOnExit();
                window.Show();
                window.Comparison.ChooseAll(Choice.FieldWorks);
                if (close) window.Close();
                else window.DecideLater();
                return Task.FromResult(window.Outcome);
            });

            Assert.Single(fake.SetRationaleRequests);
            Assert.Equal(Theirs, fake.StoredRationale);
            Assert.Equal(Original, editor.Markdown);
            Assert.Equal(Mine, editor.Text());
            Assert.True(editor.IsEditing);
            Assert.True(editor.CanCompare);
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("agent-1", "Person 1", "agent-1")]
    [InlineData(null, "Person 1", "Person 1")]
    [InlineData(null, "", "a person")]
    public async Task TheLatestRevisionsAuthorNamesTheOtherSide(string? label, string person, string expected)
    {
        var (fake, editor) = await EditingAsync();
        Revise(fake, Theirs, label, person);
        await editor.SaveCommand.ExecuteAsync(null);
        await editor.CompareAsync(comparison =>
        {
            Assert.Equal(expected, comparison.Labels.OtherName);
            Assert.Equal(expected, comparison.Labels.OtherTag);
            Assert.Equal("Cancel", comparison.Labels.Cancel);
            Assert.Equal(Theirs, comparison.Other.Markdown);
            return Task.FromResult<CompareOutcome?>(null);
        });
    }

    [Fact]
    public async Task AnotherRevisionDuringCompareIsNotOverwrittenAndCanBeComparedAgain()
    {
        var (fake, editor) = await EditingAsync();
        Revise(fake, Theirs, "agent-1");
        await editor.SaveCommand.ExecuteAsync(null);
        await editor.CompareAsync(comparison =>
        {
            foreach (var change in comparison.Comparison.Undecided) comparison.Comparison.Choose(change, Choice.Mine);
            Revise(fake, "A still newer reason.", "agent-2", revision: 3);
            return Task.FromResult<CompareOutcome?>(new CompareOutcome(CompareEnd.Done, comparison.Comparison.Result()));
        });

        Assert.True(editor.IsChangedElsewhere);
        Assert.True(editor.IsEditing);
        Assert.Equal(Merged, editor.Text());
        Assert.Equal("A still newer reason.", fake.StoredRationale);
        Assert.Equal(Theirs, fake.SetRationaleRequests[^1].ExpectedRationale);

        await editor.CompareAsync(comparison =>
        {
            Assert.Equal("agent-2", comparison.Labels.OtherName);
            comparison.Comparison.ChooseAll(Choice.Mine);
            return Task.FromResult<CompareOutcome?>(new CompareOutcome(CompareEnd.Done, comparison.Comparison.Result()));
        });
        Assert.Equal("A still newer reason.", fake.SetRationaleRequests[^1].ExpectedRationale);
        Assert.Equal(Merged, fake.StoredRationale);
        Assert.False(editor.IsEditing);
    }

    [Fact]
    public async Task AMergedRationaleRefusedForFindingsStaysEditableAndTheLastCleanVersionStaysStored()
    {
        var (fake, editor) = await EditingAsync();
        Revise(fake, Theirs, "agent-1");
        await editor.SaveCommand.ExecuteAsync(null);
        fake.SetRationaleHandler = _ => CommandOutcome<ProposalTextChangedResponse>.Refused(new Refusal(
            RefusalCodes.ProposalRationaleFindings, FailureReason.Refused, "Not saved.",
            new Dictionary<string, string> { ["findings"] = "1:1 A finding." }));

        await editor.CompareAsync(comparison =>
        {
            foreach (var change in comparison.Comparison.Undecided) comparison.Comparison.Choose(change, Choice.Mine);
            return Task.FromResult<CompareOutcome?>(new CompareOutcome(CompareEnd.Done, comparison.Comparison.Result()));
        });

        Assert.Equal(Theirs, fake.StoredRationale);
        Assert.Equal(Merged, editor.Text());
        Assert.True(editor.IsEditing);
        Assert.Equal(["1:1 A finding."], editor.Findings);
        Assert.NotNull(editor.Refusal);
    }

    [Fact]
    public async Task StartingAgainFromTheOtherVersionUsesThatVersionAsTheNextSavesBase()
    {
        var (fake, editor) = await EditingAsync();
        Revise(fake, Theirs, "agent-1");
        await editor.SaveCommand.ExecuteAsync(null);
        await editor.CompareAsync(_ => Task.FromResult<CompareOutcome?>(new CompareOutcome(CompareEnd.StartAgain, [])));

        Assert.Equal(Theirs, editor.Text());
        Assert.True(editor.IsEditing);
        Assert.False(editor.IsChangedElsewhere);
        editor.Document.Paragraphs[0].Runs[0].Text += " More.";
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Theirs, fake.SetRationaleRequests[^1].ExpectedRationale);
        Assert.Equal(Theirs + " More.", fake.StoredRationale);
        Assert.False(editor.IsEditing);
    }

    [Theory]
    [InlineData("The first reason has **bold** text.\n\nAnother [link](https://example.org).")]
    [InlineData("Some <b>raw</b> text.\nSecond line.")]
    [InlineData("The first reason mentions café and [a link](https://example.org/café).")]
    public async Task ComparePreservesWrittenMarkupEvenWhenItCannotReadAsRichText(string original)
    {
        var (fake, editor) = await EditingAsync(original);
        var mine = editor.Text();
        Revise(fake, original + " Another change.", "agent-1");
        await editor.SaveCommand.ExecuteAsync(null);
        await editor.CompareAsync(comparison =>
        {
            comparison.Comparison.ChooseAll(Choice.Mine);
            return Task.FromResult<CompareOutcome?>(new CompareOutcome(CompareEnd.Done, comparison.Comparison.Result()));
        });

        Assert.Contains("new", fake.StoredRationale!, StringComparison.Ordinal);
        Assert.Equal(mine, fake.StoredRationale);
    }

    private static async Task<(FakeCommandClient Fake, RationaleEditorModel Editor)> EditingAsync(string original = Original)
    {
        var fake = new FakeCommandClient { StoredRationale = original };
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync(@"C:\projects\one.fwdata");
        var editor = new RationaleEditorModel(context, new ProposalDetailProjection("proposal/one", "proposed", "Label",
            new RationaleView(original, null, "2026-11-11T00:00:00Z", false, null, false), null, []));
        editor.EditCommand.Execute(null);
        var run = editor.Document.Paragraphs[0].Runs[0];
        run.Text = run.Text.Contains("first", StringComparison.Ordinal)
            ? run.Text.Replace("first", "new", StringComparison.Ordinal) : run.Text + " new";
        return (fake, editor);
    }

    private static void Revise(FakeCommandClient fake, string markdown, string? author, string person = "", long revision = 2)
    {
        fake.StoredRationale = markdown;
        fake.RationaleRevisions = [new RationaleRevisionView(revision, markdown, author, person, "2026-11-12T00:00:00Z")];
    }
}
