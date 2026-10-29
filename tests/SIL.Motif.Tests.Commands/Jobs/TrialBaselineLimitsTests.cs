using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Jobs;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class TrialBaselineLimitsTests : IDisposable
{
    private const string ProductVersion = "1.0";
    private readonly WalkthroughProject _project;
    private readonly string _parser;

    public TrialBaselineLimitsTests(PristineProjectFixture pristine)
    {
        _project = new WalkthroughProject(pristine);
        _parser = FakeParser.CopyRecordingInvocations(Path.Combine(_project.ManagedRoot, "parser"));
    }

    public void Dispose() => _project.Dispose();

    [Theory]
    [InlineData(false, 10_000)]
    [InlineData(true, 2500)]
    public void AutomaticBaselineAndTrialUseTheSavedSelectionLimitsForNamedWords(bool explicitTime, int expectedMs)
    {
        SaveDefault(explicitTime
            ? SelectionLimitPolicy.Explicit(new StepCap(5000), 2500)
            : SelectionLimitPolicy.Estimated(new StepCap(5000)));

        var (before, trial) = RunPair(NewRequest());

        AssertLimits(before, trial, new StepCap(5000), expectedMs);
    }

    [Fact]
    public void AssessingNamedWordsUsesTheSameSavedLimitsAsATrial()
    {
        SaveDefault(SelectionLimitPolicy.Explicit(new StepCap(5000), 2500));
        var assess = TrialBaselines.Assess(_project.FwDataPath, [SeededProject.FirstForm],
            _project.ManagedRoot, _parser, CancellationToken.None);
        Assert.True(assess.Succeeded, assess.Refusal?.Message);
        var (_, trial) = RunPair(NewRequest());
        using var database = ProjectMotifDatabase.Open(_project.FwDataPath);
        var before = new AssessmentRepository(database).Get(assess.Value!.Measurements
            .First(item => item.Kind == "ParseTime").AssessmentId);
        AssertLimits(before, trial, new StepCap(5000), 2500);
    }

    [Fact]
    public void AutomaticBaselineAndTrialPreserveExplicitRequestLimitsOverTheSavedSelection()
    {
        SaveDefault(SelectionLimitPolicy.Explicit(new StepCap(5000), 2500));
        var supplied = new TrialLimits(TimeSpan.FromMilliseconds(700), new StepCap(123));

        var (before, trial) = RunPair(NewRequest() with { Limits = supplied });

        AssertLimits(before, trial, new StepCap(123), 700);
        Assert.Equal(supplied, trial.Limits);
    }

    [Fact]
    public void AnExplicitFiniteCapWithoutATimeOverrideKeepsTheTrialTimeUnbounded()
    {
        SaveDefault(SelectionLimitPolicy.Explicit(new StepCap(5000), 2500));
        var supplied = new TrialLimits(null, new StepCap(123));

        var (before, trial) = RunPair(NewRequest() with { Limits = supplied });

        AssertLimits(before, trial, new StepCap(123), null);
    }

    [Fact]
    public void AnUnboundedSavedSelectionLeavesBothSearchesUnbounded()
    {
        SaveDefault(SelectionLimitPolicy.Estimated(StepCap.Unbounded));

        var (before, trial) = RunPair(NewRequest());

        AssertLimits(before, trial, StepCap.Unbounded, null);
    }

    [Fact]
    public void AnExplicitUnboundedRequestOverridesTheFiniteSavedSelectionForBothSearches()
    {
        SaveDefault(SelectionLimitPolicy.Explicit(new StepCap(5000), 2500));

        var (before, trial) = RunPair(NewRequest() with { Limits = new TrialLimits(null, StepCap.Unbounded) });

        AssertLimits(before, trial, StepCap.Unbounded, null);
    }

    [Fact]
    public void NamedWordsWithoutASavedSelectionUseTheSameConfiguredLimitsOnBothSides()
    {
        var (before, trial) = RunPair(NewRequest());

        AssertLimits(before, trial, StepCap.Default, 80_000);
    }

    [Fact]
    public void DefaultSelectionWordsKeepThePolicyChosenBeforeTheirBaselineWasMeasured()
    {
        SaveDefault(SelectionLimitPolicy.Estimated(new StepCap(5000)));

        var (before, trial) = RunPair(NewRequest() with { Words = null });

        AssertLimits(before, trial, new StepCap(5000), 10_000);
        Assert.Contains(SeededProject.FirstForm, before.Selection.Words);
    }

    [Fact]
    public void MissingDefaultSelectionKeepsItsTypedRefusalWhenNoWordsAreNamed()
    {
        var queued = TrialWorkflow.Enqueue(NewRequest() with { Words = null }, _project.ManagedRoot, _parser,
            BaselineMode.Required, CancellationToken.None);

        Assert.False(queued.Succeeded);
        Assert.Equal("selection.default-missing", queued.Refusal!.Code);
        Assert.DoesNotContain("batch", FakeParser.Invocations(_parser));
    }

    private void SaveDefault(SelectionParsingLimits limits)
    {
        var saved = SelectionCommands.ReadDefault(new ReadDefaultSelectionRequest(_project.FwDataPath));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var result = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            _project.FwDataPath, "Default", [], [SeededProject.FirstForm], limits,
            saved.Value!.Selection?.Revision));
        Assert.True(result.Succeeded, result.Refusal?.Message);
    }

    private EnqueueTrialRequest NewRequest()
    {
        var created = ProposalCommands.New(new NewDraftRequest(_project.FwDataPath, ProductVersion, "limits", null));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var edited = ProposalCommands.AddSetGloss(new AddSetGlossRequest(_project.FwDataPath, ProductVersion, "limits",
            SIL.Motif.Contract.Ids.CanonicalId.FromGuid(_project.Seed.FirstSenseId).Value, "en", "changed gloss"));
        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        return new EnqueueTrialRequest(_project.FwDataPath, ProductVersion, created.Value!.ProposalId,
            Words: [SeededProject.FirstForm]);
    }

    private (AssessmentRecord Before, TrialJobInput Trial) RunPair(EnqueueTrialRequest request)
    {
        var queued = TrialWorkflow.Enqueue(request, _project.ManagedRoot, _parser, BaselineMode.Required,
            CancellationToken.None);
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Null(queued.Value!.BaselineWarning);
        Assert.Contains("batch", FakeParser.Invocations(_parser));
        using var database = ProjectMotifDatabase.Open(_project.FwDataPath);
        var trial = TrialJobInput.Parse(new JobRepository(database).Get(queued.Value.Job.JobId)!.InputJson);
        Assert.NotNull(trial.BaselineAssessmentIds);
        var repository = new AssessmentRepository(database);
        var before = Assert.Single(trial.BaselineAssessmentIds.Select(repository.Get),
            assessment => assessment.Kind == "ParseTime");
        Assert.Contains(SeededProject.FirstForm, before.Selection.Words);
        Assert.NotNull(before.Invocation);
        return (before, trial);
    }

    private static void AssertLimits(AssessmentRecord before, TrialJobInput trial, StepCap cap, int? milliseconds)
    {
        Assert.Equal(cap, before.Invocation!.PerWordStepLimit);
        Assert.Equal(milliseconds, before.Invocation.PerWordTimeoutMs);
        Assert.NotNull(trial.Limits);
        Assert.Equal(cap, trial.Limits.PerWordStepLimit);
        Assert.Equal(milliseconds is { } value ? TimeSpan.FromMilliseconds(value) : (TimeSpan?)null,
            trial.Limits.PerWordLimit);
    }
}
