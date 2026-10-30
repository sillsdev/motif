using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Jobs;

/// <summary>A Trial stops a word at the same deterministic cap Assess does; time is only a safety net.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class TrialLimitsTests
{
    private const string ProductVersion = "1.0";
    private readonly string _fwDataPath;

    public TrialLimitsTests(PristineProjectFixture pristine)
    {
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    [Fact]
    public void WholeRunBudgetIsValidatedAndPersistsWithSelectionLimits()
    {
        var limits = new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null, 90);
        SaveDefault(limits);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var repository = new NamedSelectionRepository(database);
        var saved = repository.GetDefault()!;
        Assert.Equal(90, saved.Limits.AssessBudgetSeconds);
        repository.SetLimits(saved.Name, saved.Revision.ToString(), limits with { AssessBudgetSeconds = 120 });
        Assert.Equal(120, repository.GetDefault()!.Limits.AssessBudgetSeconds);
        Assert.NotNull((limits with { AssessBudgetSeconds = 0 }).ValidationError());
        Assert.NotNull((limits with { AssessBudgetSeconds = 59 }).ValidationError());
        Assert.Null((limits with { AssessBudgetSeconds = 60 }).ValidationError());
    }

    [Fact]
    public void OmittedBudgetKeepsSavedValueThroughBothSelectionCommands()
    {
        SaveDefault(new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null, 120));
        var current = SelectionCommands.ReadDefault(new ReadDefaultSelectionRequest(_fwDataPath)).Value!.Selection!;
        var omitted = new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null);
        var changed = SelectionCommands.SetLimits(new SetSelectionLimitsRequest(_fwDataPath,
            current.Name, current.Revision, omitted));
        Assert.True(changed.Succeeded, changed.Refusal?.Message);
        Assert.Equal(120, changed.Value!.Limits.AssessBudgetSeconds);
        var replaced = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(_fwDataPath,
            current.Name, [], ["word"], omitted, changed.Value.Revision));
        Assert.True(replaced.Succeeded, replaced.Refusal?.Message);
        Assert.Equal(120, replaced.Value!.Selection!.Limits.AssessBudgetSeconds);
    }

    [Fact]
    public void ATrialHonoursTheDefaultSelectionsStepCapAndSizesTheTimeLimitAsASafetyNet()
    {
        SaveDefault(new SelectionParsingLimits(new StepCap(5000), SelectionTimeLimitMode.Estimated, null));

        var limits = QueuedLimits("selection-cap");

        Assert.Equal(5000, limits.PerWordStepLimit.Steps);
        // 20 ms estimated, a 1 s allowance, a 10 s safety net: never the fixed second a Trial once carried.
        Assert.Equal(TimeSpan.FromSeconds(10), limits.PerWordLimit);
        Assert.NotEqual(TimeSpan.FromSeconds(1), limits.PerWordLimit);
    }

    [Fact]
    public void AnExplicitSelectionTimeIsKeptAsTheUserChoseIt()
    {
        SaveDefault(new SelectionParsingLimits(new StepCap(5000), SelectionTimeLimitMode.Explicit, 2500));

        var limits = QueuedLimits("selection-explicit");

        Assert.Equal(5000, limits.PerWordStepLimit.Steps);
        Assert.Equal(TimeSpan.FromMilliseconds(2500), limits.PerWordLimit);
    }

    [Fact]
    public void AnUnboundedSelectionGivesATrialNoTimeLimitEither()
    {
        SaveDefault(new SelectionParsingLimits(StepCap.Unbounded, SelectionTimeLimitMode.Estimated, null));

        var limits = QueuedLimits("selection-unbounded");

        Assert.True(limits.PerWordStepLimit.IsUnbounded);
        Assert.Null(limits.PerWordLimit);
    }

    [Fact]
    public void ATrialWithoutASavedSelectionUsesTheDefaultCapAndANonFixedSafetyNet()
    {
        var limits = QueuedLimits("no-selection");

        Assert.Equal(StepCap.DefaultSteps, limits.PerWordStepLimit.Steps);
        Assert.Equal(TimeSpan.FromSeconds(80), limits.PerWordLimit);
    }

    [Fact]
    public void LimitsCopiedFromAnEarlierAssessmentAreNotReplaced()
    {
        SaveDefault(new SelectionParsingLimits(new StepCap(5000), SelectionTimeLimitMode.Estimated, null));
        var earlier = new TrialLimits(TimeSpan.FromMilliseconds(700), new StepCap(123));

        var limits = QueuedLimits("copied", earlier);

        Assert.Equal(earlier, limits);
    }

    private void SaveDefault(SelectionParsingLimits limits)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        new NamedSelectionRepository(database).SetDefault("Default", [], ["word"], limits, expectedRevision: null);
    }

    private TrialLimits QueuedLimits(string draftName, TrialLimits? supplied = null)
    {
        var created = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(_fwDataPath, ProductVersion, draftName,
            CanonicalId.Mint().Value, "en", "a gloss")).Succeeded);
        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion,
            created.Value!.ProposalId, Words: ["word"], Limits: supplied));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return TrialJobInput.Parse(new JobRepository(database).Get(queued.Value!.JobId)!.InputJson).Limits
            ?? throw new Xunit.Sdk.XunitException("The queued Trial carries no limits.");
    }
}

/// <summary>The one rule that turns a Selection's limits into a run's step cap and time safety net.</summary>
public sealed class ParseRunLimitsTests
{
    private static readonly ParserStepRate Typical = StepLimitEstimator.TypicalMachineRate;

    [Fact]
    public void TheSafetyNetIsTenTimesTheEstimatedAllowanceAndTheStepCapStaysPrimary()
    {
        var limits = ParseRunLimits.Resolve(
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), Typical);

        Assert.Equal(StepCap.DefaultSteps, limits.PerWordStepLimit.Steps);
        // 200,000 steps estimate 800 ms, the allowance is 8 s, and the safety net is ten times that.
        Assert.Equal(TimeSpan.FromSeconds(80), limits.PerWordLimit);
    }

    [Fact]
    public void ASlowerComputerLengthensTheSafetyNetButNeverTheStepCap()
    {
        var slow = new ParserStepRate(Typical.MillisecondsPerStep * 4, IsTypicalMachine: false);

        var limits = ParseRunLimits.Resolve(
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), slow);

        Assert.Equal(StepCap.DefaultSteps, limits.PerWordStepLimit.Steps);
        Assert.Equal(TimeSpan.FromSeconds(320), limits.PerWordLimit);
    }

    [Fact]
    public void AnUnboundedStepCapHasNoSafetyNet()
    {
        var limits = ParseRunLimits.Resolve(
            new SelectionParsingLimits(StepCap.Unbounded, SelectionTimeLimitMode.Estimated, null), Typical);

        Assert.Null(limits.PerWordLimit);
    }

    [Theory]
    [InlineData(200_000L, "20000000")]
    [InlineData(7L, "700")]
    public void TheWorkCapIsOneHundredTimesTheStepCap(long steps, string expected) =>
        Assert.Equal(expected, new StepCap(steps).ToWorkCapArgument());

    [Fact]
    public void AnUnboundedStepCapSendsAnUnboundedWorkCap() =>
        Assert.Equal("unbounded", StepCap.Unbounded.ToWorkCapArgument());
}
