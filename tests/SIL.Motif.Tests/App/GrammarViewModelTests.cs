using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="GrammarViewModel"/>: it checks the grammar when a project is set and again on demand,
/// never from an Assessment, and its five display states — loading, refused, no Baseline, no findings, and
/// findings — follow directly from the last check's outcome.
/// </summary>
public sealed class GrammarViewModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    [Fact]
    public void BeforeAnyProjectNothingHasBeenCheckedAndTheCommandCannotRun()
    {
        var grammar = new GrammarViewModel(new FakeCommandClient());

        Assert.False(grammar.HasChecked);
        Assert.Equal("Not checked yet", grammar.SummaryText);
        Assert.False(grammar.CheckCommand.CanExecute(null));
    }

    // A view knows only what it is told: the last notified value of each state must match the outcome.
    [Fact]
    public async Task AViewThatOnlyListensSeesTheFindingsTableAndNeverTheEmptyStateOnceFindingsArrive()
    {
        var fake = new FakeCommandClient();
        var answer = new TaskCompletionSource<CommandOutcome<GrammarCheckResponse>>();
        fake.OnCheckGrammar((_, _) => answer.Task);
        var grammar = new GrammarViewModel(fake);
        var seen = new Dictionary<string, bool>();
        grammar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GrammarViewModel.ShowFindings) or nameof(GrammarViewModel.ShowNoFindings)
                or nameof(GrammarViewModel.ShowLoading))
                seen[e.PropertyName] = (bool)typeof(GrammarViewModel).GetProperty(e.PropertyName)!.GetValue(grammar)!;
        };

        var setting = grammar.SetProjectAsync(ProjectPath);
        Assert.True(seen[nameof(GrammarViewModel.ShowLoading)]);
        answer.SetResult(CommandOutcome<GrammarCheckResponse>.Success(new GrammarCheckResponse(
            [new GrammarWarning("warning", "Entry", [], [new GrammarWarningPart("dropped", "text")], "warning: dropped")],
            HasBaseline: true)));
        await setting;

        Assert.True(seen[nameof(GrammarViewModel.ShowFindings)]);
        Assert.False(seen[nameof(GrammarViewModel.ShowNoFindings)]);
        Assert.False(seen[nameof(GrammarViewModel.ShowLoading)]);
    }

    [Fact]
    public async Task SettingAProjectChecksItImmediatelyAndShowsFindings()
    {
        var fake = new FakeCommandClient();
        fake.CheckGrammarCompletesWith(new GrammarCheckResponse(
            [new GrammarWarning("warning", "Entry", [], [new GrammarWarningPart("dropped", "text")], "warning: dropped")],
            HasBaseline: true));
        var grammar = new GrammarViewModel(fake);

        await grammar.SetProjectAsync(ProjectPath);

        Assert.True(grammar.HasChecked);
        Assert.True(grammar.ShowFindings);
        Assert.False(grammar.ShowNoFindings);
        Assert.False(grammar.ShowRefused);
        Assert.False(grammar.ShowNoBaseline);
        Assert.Equal(1, grammar.Warnings.TotalCount);
        Assert.Equal("1 finding", grammar.SummaryText);
        Assert.Equal(ProjectPath, Assert.Single(fake.CheckGrammarRequests).ProjectPath);
    }

    [Fact]
    public async Task NoFindingsShowsTheEmptyStateRatherThanTheTable()
    {
        var fake = new FakeCommandClient();
        fake.CheckGrammarCompletesWith(new GrammarCheckResponse([], HasBaseline: true));
        var grammar = new GrammarViewModel(fake);

        await grammar.SetProjectAsync(ProjectPath);

        Assert.True(grammar.ShowNoFindings);
        Assert.Equal("No findings", grammar.SummaryText);
    }

    [Fact]
    public async Task NoBaselineShowsItsOwnStateRatherThanAnEmptyFindingsTable()
    {
        var fake = new FakeCommandClient();
        fake.CheckGrammarCompletesWith(new GrammarCheckResponse([], HasBaseline: false));
        var grammar = new GrammarViewModel(fake);

        await grammar.SetProjectAsync(ProjectPath);

        Assert.True(grammar.ShowNoBaseline);
        Assert.False(grammar.ShowNoFindings);
        Assert.Equal("Capture a Baseline first", grammar.SummaryText);
    }

    [Fact]
    public async Task ARefusalShowsItsMessageAndCountsAsCheckedForTheStepper()
    {
        var fake = new FakeCommandClient();
        var refusal = new Refusal("app.not-built", FailureReason.Refused, "The grammar check query is not built yet.");
        fake.CheckGrammarRefusesWith(refusal);
        var grammar = new GrammarViewModel(fake);

        await grammar.SetProjectAsync(ProjectPath);

        Assert.True(grammar.HasChecked);
        Assert.True(grammar.ShowRefused);
        Assert.Same(refusal, grammar.Refusal);
        Assert.Equal(refusal.Message, grammar.SummaryText);
    }

    [Fact]
    public async Task CheckCommandRechecksTheSameProjectOnDemand()
    {
        var fake = new FakeCommandClient();
        fake.CheckGrammarCompletesWith(new GrammarCheckResponse([], HasBaseline: true));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(ProjectPath);

        await grammar.CheckCommand.ExecuteAsync(null);

        Assert.Equal(2, fake.CheckGrammarRequests.Count);
        Assert.All(fake.CheckGrammarRequests, request => Assert.Equal(ProjectPath, request.ProjectPath));
    }

    [Fact]
    public async Task SettingANewProjectClearsTheOldOnesFindingsBeforeCheckingTheNewOne()
    {
        var fake = new FakeCommandClient();
        fake.CheckGrammarCompletesWith(new GrammarCheckResponse(
            [new GrammarWarning("warning", "Entry", [], [new GrammarWarningPart("x", "text")], "warning: x")],
            HasBaseline: true));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(ProjectPath);
        Assert.Equal(1, grammar.Warnings.TotalCount);

        fake.CheckGrammarCompletesWith(new GrammarCheckResponse([], HasBaseline: true));
        await grammar.SetProjectAsync(@"C:\projects\two.fwdata");

        Assert.Equal(0, grammar.Warnings.TotalCount);
        Assert.True(grammar.ShowNoFindings);
    }
}
