using SIL.Motif.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Group = SIL.Motif.App.ViewModels.SettingsGroup;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Random but replayable sequences over the Settings parser work and time limits. After every act the next parse must send
/// the last saved value whenever a field shows a refused value or after Escape, a commit or leaving with one held;
/// otherwise it sends the saved value or the good value showing in its field. No message may describe a value the
/// field no longer holds. A project switch lands on a project saved with explicit, estimated or no parser work limits and must
/// leave nothing of the last project in the fields or the Selection, and the bound properties must agree with the
/// fields after every act. A failure names the seed and the acts so it can be replayed.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsLimitsSequenceTests
{
    private static readonly int[] Seeds = ExtraSeeds() ?? [1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233];
    private const int StepsPerSequence = 30;
    private static readonly decimal[] GoodSteps = [20000m, 150000m, 250000m];
    private static readonly decimal[] RefusedSteps = [100000.5m, 0.5m, -3m, 100000000000000000000m];
    private static readonly decimal[] GoodTimes = [2m, 7.5m, 12m];
    private static readonly decimal[] RefusedTimes = [-1m, 0m];

    // MOTIF_SEQUENCE_SEEDS=1-112 replays a wider range of seeds.
    private static int[]? ExtraSeeds() =>
        Environment.GetEnvironmentVariable("MOTIF_SEQUENCE_SEEDS")?.Split('-') is [var from, var to]
            ? [.. Enumerable.Range(int.Parse(from), int.Parse(to) - int.Parse(from) + 1)] : null;

    private enum Act { TypeStep, TypeTime, Tick, Untick, Commit, Escape, LeaveAndReturn, Switch, UseEstimate }

    private static bool GoodStep(decimal? value) => value is > 0 && decimal.Truncate(value.Value) == value && value <= long.MaxValue;

    private static bool GoodTime(decimal? value) => value is > 0;

    [Fact]
    public void RandomLimitSequencesKeepTheLastSavedValueAndNeverLetARefusedOneIntoTheSelection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var failures = new List<string>();
            foreach (var seed in Seeds)
            {
                try { await RunAsync(seed); }
                catch (Xunit.Sdk.XunitException failure) { failures.Add(failure.Message); }
            }
            Assert.True(failures.Count == 0, $"{failures.Count} of {Seeds.Length} seeds failed:\n{string.Join("\n", failures)}");
        }, TimeSpan.FromMinutes(Seeds.Length > 20 ? 20 : 4));
    }

    [Fact]
    public void AGoodValueStaysOutOfTheSelectionWhileAnotherFieldShowsARefusedOne()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, settings, _) = await OpenAsync();
            try
            {
                settings.TimeLimitSeconds = 7.5m;
                Assert.Equal(7.5m, workspace.Selection.PerWordTimeLimitSeconds);
                settings.TimeLimitSeconds = 0m;
                Assert.Equal(5m, workspace.Selection.PerWordTimeLimitSeconds);
                settings.ParserWorkLimitValue = 250000m;
                Assert.Equal(ParserWorkLimit.DefaultWork, workspace.Selection.PerWordParserWorkLimit);
                settings.TimeLimitSeconds = 12m;
                Assert.Equal(250000m, workspace.Selection.PerWordParserWorkLimit);
                Assert.Equal(12m, workspace.Selection.PerWordTimeLimitSeconds);
            }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void EscapeAfterTickingNoParserWorkLimitPutsTheSavedTimeBackInTheSelectionAndTheField()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, settings, _) = await OpenAsync();
            try
            {
                settings.IsNoParserWorkLimit = true;
                settings.CancelParsingLimitEdit();
                Assert.False(workspace.Selection.PerWordParserWorkLimitUnbounded);
                Assert.Equal(ParserWorkLimit.DefaultWork, workspace.Selection.PerWordParserWorkLimit);
                Assert.Equal(5m, workspace.Selection.PerWordTimeLimitSeconds);
                Assert.Equal(5m, settings.TimeLimitSeconds);
                Assert.False(settings.IsUsingEstimate);
            }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void UntickingAfterSwitchingToANoParserWorkLimitProjectShowsTheDefaultNotTheLastProjectsNumber()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, settings, fake) = await OpenAsync();
            try
            {
                settings.ParserWorkLimitValue = 250000m;
                await SaveAsync(settings);
                await SwitchProjectAsync(workspace, settings, fake, UnboundedLimits);
                Assert.True(settings.IsNoParserWorkLimit);
                settings.IsNoParserWorkLimit = false;
                Assert.Equal(ParserWorkLimit.DefaultWork, settings.ParserWorkLimitValue);
                Assert.Equal(ParserWorkLimit.DefaultWork, workspace.Selection.PerWordParserWorkLimit);
                await SaveAsync(settings);
                Assert.Equal(ParserWorkLimit.Default, fake.SetSelectionLimitsRequests[^1].Limits.PerWordParserWorkLimit);
            }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void EscapeAfterSwitchingToANoParserWorkLimitProjectKeepsItUnboundedWithNoOtherProjectsNumber()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, settings, fake) = await OpenAsync();
            try
            {
                settings.ParserWorkLimitValue = 250000m;
                await SaveAsync(settings);
                await SwitchProjectAsync(workspace, settings, fake, UnboundedLimits);
                settings.CancelParsingLimitEdit();
                Assert.True(workspace.Selection.PerWordParserWorkLimitUnbounded);
                Assert.Null(workspace.Selection.PerWordParserWorkLimit);
                Assert.True(workspace.Selection.BuildRequest().PerWordParserWorkLimit!.IsUnbounded);
            }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AFreshProjectsEstimateAndParserWorkLimitReplaceTheLastProjectsSavedOnesOnEveryRoute()
    {
        WithProject(null, async (workspace, settings, fake) =>
        {
            settings.ParserWorkLimitValue = 250000m;
            settings.TimeLimitSeconds = 12m;
            await SaveAsync(settings);
            await SwitchProjectAsync(workspace, settings, fake, SelectionLimitPolicy.Estimated(new ParserWorkLimit(150000)));
            Assert.Equal(150000m, settings.ParserWorkLimitValue);
            Assert.True(settings.IsUsingEstimate);
            settings.IsNoParserWorkLimit = true;
            settings.CancelParsingLimitEdit();
            Assert.False(workspace.Selection.PerWordParserWorkLimitUnbounded);
            Assert.Equal(150000m, workspace.Selection.PerWordParserWorkLimit);
            Assert.True(settings.IsUsingEstimate);
            Assert.NotEqual(12m, workspace.Selection.PerWordTimeLimitSeconds);
        });
    }

    [Fact]
    public void EscapeInTheMiddleOfAProjectSwitchPutsNothingOfTheLastProjectIntoTheSelection()
    {
        WithProject(null, async (workspace, settings, fake) =>
        {
            settings.ParserWorkLimitValue = 250000m;
            settings.IsNoParserWorkLimit = true;
            await SaveAsync(settings);
            var hold = new HoldOpen(workspace.Context);
            var opening = StartSwitch(workspace, fake, EstimatedLimits);
            await hold.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            settings.CancelParsingLimitEdit();
            AssertNothingSelected(workspace, 250000m);
            hold.Release.SetResult();
            await opening;
            Assert.Equal(ParserWorkLimit.DefaultWork, workspace.Selection.PerWordParserWorkLimit);
            Assert.False(workspace.Selection.PerWordParserWorkLimitUnbounded);
        });
    }

    [Fact]
    public void AFailedSaveFinishingInTheMiddleOfAProjectSwitchPutsNothingOfTheLastProjectIntoTheSelection()
    {
        WithProject(null, async (workspace, settings, fake) =>
        {
            var save = new TaskCompletionSource<CommandOutcome<NamedSelectionProjection>>();
            fake.OnSetSelectionLimits((_, _) => save.Task);
            settings.ParserWorkLimitValue = 150000m;
            var committing = settings.CommitParsingLimitsAsync();
            var hold = new HoldOpen(workspace.Context);
            var opening = StartSwitch(workspace, fake, EstimatedLimits);
            await hold.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            save.SetResult(CommandOutcome<NamedSelectionProjection>.Refused(
                new Refusal("selection.not-saved", FailureReason.Refused, "The Selection could not be saved.")));
            await committing;
            AssertNothingSelected(workspace, 150000m);
            hold.Release.SetResult();
            await opening;
            Assert.Equal(ParserWorkLimit.DefaultWork, workspace.Selection.PerWordParserWorkLimit);
        });
    }

    [Fact]
    public void ASwitchWithARefusedValueAndATickHeldShowsOnlyTheNewProjectsLimits()
    {
        WithProject(null, async (workspace, settings, fake) =>
        {
            settings.ParserWorkLimitValue = 0.5m;
            settings.IsNoParserWorkLimit = true;
            Assert.True(settings.HasParserWorkLimitRefusal);
            await SwitchProjectAsync(workspace, settings, fake,
                new SelectionParsingLimits(new ParserWorkLimit(150000), SelectionTimeLimitMode.Explicit, 2000, 600));
            Assert.False(settings.IsNoParserWorkLimit);
            Assert.False(settings.HasParserWorkLimitRefusal);
            Assert.Equal(150000m, settings.ParserWorkLimitValue);
            Assert.Equal(2m, settings.TimeLimitSeconds);
            settings.CancelParsingLimitEdit();
            Assert.Equal(150000m, workspace.Selection.PerWordParserWorkLimit);
            Assert.Equal(2m, workspace.Selection.PerWordTimeLimitSeconds);
        });
    }

    [Fact]
    public void AnEstimateChosenBesideARefusedParserWorkLimitSurvivesAnotherRefusedEdit()
    {
        WithProject(null, async (workspace, settings, fake) =>
        {
            settings.ParserWorkLimitValue = 0.5m;
            settings.UseMotifEstimate();
            var estimate = settings.TimeLimitSeconds;
            settings.ParserWorkLimitValue = -3m;
            Assert.True(settings.IsUsingEstimate);
            Assert.False(settings.ShowUseEstimate);
            Assert.Equal(estimate, settings.TimeLimitSeconds);
            Assert.Equal(5m, workspace.Selection.PerWordTimeLimitSeconds);
            Assert.Equal(ParserWorkLimit.DefaultWork, workspace.Selection.PerWordParserWorkLimit);
            settings.ParserWorkLimitValue = 150000m;
            await SaveAsync(settings);
            Assert.Equal(SelectionTimeLimitMode.Estimated, fake.SetSelectionLimitsRequests[^1].Limits.TimeMode);
        });
    }

    [Fact]
    public void ASwitchWhoseConfigurationFailsToLoadSavesTheDefaultAssessBudgetNotTheLastProjectsOne()
    {
        WithProject(null, async (workspace, settings, fake) =>
        {
            settings.AssessBudgetSeconds = 7200;
            await SaveAsync(settings);
            fake.OnShowConfig((_, _) => Task.FromResult(CommandOutcome<ProjectConfigurationProjection>.Refused(
                new Refusal("config.unreadable", FailureReason.Refused, "The configuration could not be read."))));
            await SwitchProjectAsync(workspace, settings, fake, EstimatedLimits);
            Assert.Equal((decimal)SelectionParsingLimits.DefaultAssessBudgetSeconds, settings.AssessBudgetSeconds);
            settings.ParserWorkLimitValue = 150000m;
            await SaveAsync(settings);
            Assert.Equal(SelectionParsingLimits.DefaultAssessBudgetSeconds, fake.SetSelectionLimitsRequests[^1].Limits.AssessBudgetSeconds);
        });
    }

    private static void AssertNothingSelected(WorkspaceShellViewModel workspace, decimal lastProjectsParserWork)
    {
        Assert.False(workspace.Selection.PerWordParserWorkLimitUnbounded);
        Assert.NotEqual(lastProjectsParserWork, workspace.Selection.PerWordParserWorkLimit);
    }

    private static void WithProject(SelectionParsingLimits? limits,
        Func<WorkspaceShellViewModel, SettingsViewModel, FakeCommandClient, Task> body) =>
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, settings, fake) = await OpenAsync(limits);
            try { await body(workspace, settings, fake); }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));

    // Holds the open of the next project before Setup loads it, so a test can act while the old project is cleared.
    private sealed class HoldOpen(WorkspaceContext context) : PageModel(context)
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
        }
    }

    private static Task StartSwitch(WorkspaceShellViewModel workspace, FakeCommandClient fake, SelectionParsingLimits limits)
    {
        fake.DefaultSelectionIs(Saved(limits));
        return workspace.Context.OpenProjectAsync(workspace.Context.ProjectPath + ".other");
    }

    [Fact]
    public void TickingAndUntickingInEstimateModeSavesEstimatedNotExplicit()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, settings, fake) = await OpenAsync(EstimatedLimits);
            try
            {
                Assert.True(settings.IsUsingEstimate);
                settings.IsNoParserWorkLimit = true;
                settings.IsNoParserWorkLimit = false;
                Assert.True(settings.IsUsingEstimate);
                await SaveAsync(settings);
                Assert.Equal(SelectionTimeLimitMode.Estimated, fake.SetSelectionLimitsRequests[^1].Limits.TimeMode);
            }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void LeavingAHeldTickRaisesTheTickTheTimeFieldAndUseEstimate()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, settings, _) = await OpenAsync();
            try
            {
                settings.ParserWorkLimitValue = 0.5m;
                settings.IsNoParserWorkLimit = true;
                Assert.True(settings.IsNoParserWorkLimit);
                var raised = new List<string?>();
                settings.PropertyChanged += (_, args) => raised.Add(args.PropertyName);
                settings.CancelParsingLimitEdit();
                Assert.False(settings.IsNoParserWorkLimit);
                Assert.Contains(nameof(SettingsViewModel.IsNoParserWorkLimit), raised);
                Assert.Contains(nameof(SettingsViewModel.IsTimeLimitEnabled), raised);
                Assert.Contains(nameof(SettingsViewModel.ShowUseEstimate), raised);
            }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void CorrectingTheValueBesideAHeldUntickFromASavedNoLimitProjectReleasesTheUntick()
    {
        WithProject(UnboundedLimits, async (workspace, settings, fake) =>
        {
            settings.IsNoParserWorkLimit = false;
            settings.ParserWorkLimitValue = 0.5m;
            Assert.True(settings.HasParserWorkLimitRefusal);
            Assert.True(workspace.Selection.PerWordParserWorkLimitUnbounded);
            settings.ParserWorkLimitValue = 150000m;
            Assert.False(settings.HasParserWorkLimitRefusal);
            Assert.False(workspace.Selection.PerWordParserWorkLimitUnbounded);
            Assert.Equal(150000m, workspace.Selection.PerWordParserWorkLimit);
            workspace.CurrentPage = WorkspacePage.Overview;
            workspace.CurrentPage = WorkspacePage.Settings;
            settings.OpenGroup(Group.Parsing);
            Assert.False(settings.IsNoParserWorkLimit);
            Assert.Equal(150000m, settings.ParserWorkLimitValue);
            await SaveAsync(settings);
            Assert.Equal(150000, fake.SetSelectionLimitsRequests[^1].Limits.PerWordParserWorkLimit.Work);
        });
    }

    [Fact]
    public void CorrectingTheTimeBesideAHeldUntickFromASavedNoLimitProjectReleasesTheUntick()
    {
        WithProject(UnboundedLimits, async (workspace, settings, fake) =>
        {
            settings.IsNoParserWorkLimit = false;
            settings.TimeLimitSeconds = 0m;
            Assert.True(settings.HasTimeLimitRefusal);
            Assert.True(workspace.Selection.PerWordParserWorkLimitUnbounded);
            settings.TimeLimitSeconds = 9m;
            Assert.False(workspace.Selection.PerWordParserWorkLimitUnbounded);
            Assert.Equal(9m, workspace.Selection.PerWordTimeLimitSeconds);
            workspace.CurrentPage = WorkspacePage.Overview;
            workspace.CurrentPage = WorkspacePage.Settings;
            settings.OpenGroup(Group.Parsing);
            Assert.False(settings.IsNoParserWorkLimit);
            Assert.Equal(9m, settings.TimeLimitSeconds);
            await SaveAsync(settings);
            Assert.Equal(9000, fake.SetSelectionLimitsRequests[^1].Limits.ExplicitPerWordLimitMs);
        });
    }

    [Fact]
    public void CorrectingTheValueBesideAHeldUntickInEstimateModeShowsTheEstimateForTheCorrectedValue()
    {
        WithProject(UnboundedLimits, (workspace, settings, _) =>
        {
            settings.IsNoParserWorkLimit = false;
            settings.ParserWorkLimitValue = 0.5m;
            Assert.True(settings.IsUsingEstimate);
            settings.ParserWorkLimitValue = 150000m;
            Assert.False(workspace.Selection.PerWordParserWorkLimitUnbounded);
            Assert.True(settings.IsUsingEstimate);
            var estimate = workspace.Context.Setup!.EstimatedPerWordLimitMs / 1000m;
            Assert.True(estimate > 0, "Setup estimates the corrected step");
            Assert.Equal(estimate, settings.TimeLimitSeconds);
            Assert.Equal(estimate, workspace.Selection.PerWordTimeLimitSeconds);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void WritingTheShownNumberBackIntoADisabledFieldDuringAHoldKeepsTheTypedNumber()
    {
        WithProject(null, (workspace, settings, _) =>
        {
            settings.ParserWorkLimitValue = 0.5m;
            settings.TimeLimitSeconds = 0m;
            settings.IsNoParserWorkLimit = true;
            settings.ParserWorkLimitValue = settings.ParserWorkLimitValue;
            settings.TimeLimitSeconds = settings.TimeLimitSeconds;
            Assert.True(settings.IsNoParserWorkLimit);
            settings.IsNoParserWorkLimit = false;
            Assert.Equal(0.5m, settings.ParserWorkLimitValue);
            Assert.Equal(0m, settings.TimeLimitSeconds);
            Assert.True(settings.HasParserWorkLimitRefusal);
            Assert.True(settings.HasTimeLimitRefusal);
            Assert.Null(workspace.Selection.ParserWorkLimitValidationMessage);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ACommittedTickNeverLeavesARefusedNumberInTheDisabledLimitFields()
    {
        WithProject(null, async (workspace, settings, fake) =>
        {
            settings.ParserWorkLimitValue = 0.5m;
            settings.TimeLimitSeconds = 0m;
            settings.IsNoParserWorkLimit = true;
            AssertDisabledFieldsShowNoRefusedNumber(settings, "while the tick is held");
            await SaveAsync(settings);
            Assert.True(settings.IsNoParserWorkLimit);
            Assert.True(fake.SetSelectionLimitsRequests[^1].Limits.PerWordParserWorkLimit.IsUnbounded);
            Assert.False(settings.HasParserWorkLimitRefusal);
            Assert.False(settings.HasTimeLimitRefusal);
            AssertDisabledFieldsShowNoRefusedNumber(settings, "after the commit");
            settings.IsNoParserWorkLimit = false;
            Assert.Equal(0.5m, settings.ParserWorkLimitValue);
            Assert.True(settings.HasParserWorkLimitRefusal);
        });
    }

    private static void AssertDisabledFieldsShowNoRefusedNumber(SettingsViewModel settings, string when)
    {
        Assert.True(settings.ParserWorkLimitValue is > 0 && decimal.Truncate(settings.ParserWorkLimitValue.Value) == settings.ParserWorkLimitValue,
            $"the disabled parser work limit field shows {settings.ParserWorkLimitValue} {when}");
        Assert.True(settings.TimeLimitSeconds is null or > 0, $"the disabled time field shows {settings.TimeLimitSeconds} {when}");
    }

    private static readonly SelectionParsingLimits ExplicitLimits =
        new(ParserWorkLimit.Default, SelectionTimeLimitMode.Explicit, 5000, 600);
    private static readonly SelectionParsingLimits UnboundedLimits = SelectionLimitPolicy.Estimated(ParserWorkLimit.Unbounded);
    private static readonly SelectionParsingLimits EstimatedLimits = SelectionLimitPolicy.Estimated(ParserWorkLimit.Default);

    private static NamedSelectionProjection Saved(SelectionParsingLimits limits) => new("Default",
        [Guid.Parse("11111111-0000-0000-0000-000000000001")], [], string.Empty, string.Empty, limits, "1");

    // Opens another project, then returns to the Parsing group as a person would.
    private static async Task SwitchProjectAsync(WorkspaceShellViewModel workspace, SettingsViewModel settings,
        FakeCommandClient fake, SelectionParsingLimits limits)
    {
        await StartSwitch(workspace, fake, limits);
        workspace.CurrentPage = WorkspacePage.Settings;
        settings.OpenGroup(Group.Parsing);
    }

    private static async Task SaveAsync(SettingsViewModel settings)
    {
        Assert.Null(await settings.CommitParsingLimitsAsync());
    }

    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Frame, SettingsViewModel Settings, FakeCommandClient Fake)> OpenAsync(
        SelectionParsingLimits? limits = null)
    {
        FakeCommandClient? fake = null;
        var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false,
            configure: (client, _) =>
            {
                fake = client;
                client.DefaultSelectionIs(Saved(limits ?? ExplicitLimits));
            });
        var settings = SettingsTestAccess.Model(frame);
        workspace.CurrentPage = WorkspacePage.Settings;
        settings.OpenGroup(Group.Parsing);
        return (workspace, frame, settings, fake!);
    }

    private static async Task RunAsync(int seed)
    {
        var random = new Random(seed);
        var log = new List<string>();
        var (workspace, frame, settings, fake) = await OpenAsync();
        try
        {
            var model = new Model(workspace, settings, fake, log);
            model.Watch();
            model.Check("opened", exact: true);
            for (var index = 0; index < StepsPerSequence; index++)
            {
                var act = model.Pick(random);
                try { await model.DoAsync(act, random); }
                catch (Exception error) when (error is not Xunit.Sdk.XunitException)
                {
                    throw new Xunit.Sdk.XunitException($"seed {seed}: {error}\nsteps: {string.Join(" | ", log)}");
                }
            }
        }
        catch (Xunit.Sdk.XunitException failure) when (!failure.Message.StartsWith("seed ", StringComparison.Ordinal))
        {
            throw new Xunit.Sdk.XunitException($"seed {seed}: {failure.Message}\nsteps: {string.Join(" | ", log)}");
        }
        finally
        {
            frame.Close();
            await workspace.DisposeAsync();
        }
    }

    private sealed class Model(WorkspaceShellViewModel workspace, SettingsViewModel settings, FakeCommandClient fake, List<string> log)
    {
        private bool _savedNoStep;
        private decimal _savedStep = ParserWorkLimit.DefaultWork;
        private decimal _savedTime = 5m;
        private bool _ticked;
        private decimal? _step = ParserWorkLimit.DefaultWork;
        private decimal? _time = 5m;
        private int _saves;
        private bool _tickHeld;
        private bool _estimate;
        private bool _savedEstimate;
        private bool _estimateBeforeTick;
        private bool _heldUntick;
        private int _projects;
        private readonly Dictionary<string, bool> _announced = [];

        public void Watch()
        {
            settings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(SettingsViewModel.IsNoParserWorkLimit) or nameof(SettingsViewModel.IsTimeLimitEnabled)
                    or nameof(SettingsViewModel.ShowUseEstimate))
                    _announced[args.PropertyName] = Bound(args.PropertyName);
            };
            foreach (var name in new[] { nameof(SettingsViewModel.IsNoParserWorkLimit), nameof(SettingsViewModel.IsTimeLimitEnabled),
                         nameof(SettingsViewModel.ShowUseEstimate) })
                _announced[name] = Bound(name);
        }

        private bool Bound(string name) => name switch
        {
            nameof(SettingsViewModel.IsNoParserWorkLimit) => settings.IsNoParserWorkLimit,
            nameof(SettingsViewModel.IsTimeLimitEnabled) => settings.IsTimeLimitEnabled,
            _ => settings.ShowUseEstimate,
        };

        private bool RefusedShown => !_ticked && (!GoodStep(_step) || !GoodTime(_time));

        public Act Pick(Random random)
        {
            var allowed = new List<Act> { Act.Escape, Act.LeaveAndReturn, Act.Commit, Act.Switch };
            if (!_ticked) allowed.AddRange([Act.TypeStep, Act.TypeStep, Act.TypeTime, Act.TypeTime, Act.Tick]);
            if (!_ticked && !_estimate) allowed.Add(Act.UseEstimate);
            if (_ticked) allowed.Add(Act.Untick);
            return allowed[random.Next(allowed.Count)];
        }

        public async Task DoAsync(Act act, Random random)
        {
            var exact = false;
            switch (act)
            {
                case Act.TypeStep:
                    _step = random.Next(2) == 0 ? GoodSteps[random.Next(GoodSteps.Length)] : RefusedSteps[random.Next(RefusedSteps.Length)];
                    log.Add($"step={_step}");
                    settings.ParserWorkLimitValue = _step;
                    break;
                case Act.TypeTime:
                    _time = random.Next(2) == 0 ? GoodTimes[random.Next(GoodTimes.Length)] : RefusedTimes[random.Next(RefusedTimes.Length)];
                    log.Add($"time={_time}");
                    _estimate = false;
                    settings.TimeLimitSeconds = _time;
                    break;
                case Act.Tick:
                    log.Add("tick");
                    _tickHeld = RefusedShown;
                    // Ticking onto a held saved no limit returns to the save, so nothing typed is brought back.
                    _estimateBeforeTick = _heldUntick || _estimate;
                    _heldUntick = false;
                    _estimate = true;
                    _ticked = true;
                    settings.IsNoParserWorkLimit = true;
                    break;
                case Act.Untick:
                    log.Add("untick");
                    _ticked = false;
                    _tickHeld = false;
                    _heldUntick = false;
                    settings.IsNoParserWorkLimit = false;
                    // Unticking brings back the time mode the person left; on a saved no limit project it may land on the estimate.
                    _estimate = _savedNoStep && settings.IsUsingEstimate || _estimateBeforeTick;
                    Resync("untick");
                    break;
                case Act.UseEstimate:
                    log.Add("use estimate");
                    Assert.True(settings.ShowUseEstimate, "Use estimate is offered whenever the time is typed");
                    _estimate = true;
                    settings.UseMotifEstimate();
                    break;
                case Act.Commit:
                    await CommitAsync();
                    exact = true;
                    break;
                case Act.Escape:
                    log.Add("escape");
                    settings.CancelParsingLimitEdit();
                    Reset();
                    exact = true;
                    break;
                case Act.Switch:
                    await SwitchAsync(random);
                    exact = true;
                    break;
                case Act.LeaveAndReturn:
                    log.Add("leave+return");
                    exact = RefusedShown || _tickHeld || _heldUntick;
                    workspace.CurrentPage = WorkspacePage.Overview;
                    workspace.CurrentPage = WorkspacePage.Settings;
                    settings.OpenGroup(Group.Parsing);
                    if (exact) Reset();
                    else Resync("leave");
                    break;
            }
            if (_savedNoStep && !_ticked) _heldUntick = RefusedShown;
            Adopt();
            Check(log[^1], exact);
        }

        // In estimate mode the time field shows the figure Setup estimates for the parser work limit it holds.
        private decimal? EstimateTime() => workspace.Context.Setup!.EstimatedPerWordLimitMs is > 0 and var ms ? ms / 1000m : null;

        private void Adopt()
        {
            if (_ticked || !_estimate) return;
            _time = EstimateTime();
            if (!_savedNoStep) Assert.True(GoodTime(_time), $"after '{log[^1]}': Setup has no estimate");
        }

        private async Task SwitchAsync(Random random)
        {
            var kind = random.Next(3);
            var step = random.Next(2) == 0 ? 20000m : 150000m;
            var time = random.Next(2) == 0 ? 2m : 7.5m;
            log.Add(kind switch { 0 => $"switch explicit {step}/{time}", 1 => "switch unbounded", _ => "switch estimated" });
            var limits = kind switch
            {
                0 => new SelectionParsingLimits(new ParserWorkLimit((long)step), SelectionTimeLimitMode.Explicit, (int)(time * 1000), 600),
                1 => UnboundedLimits,
                _ => EstimatedLimits,
            };
            await SwitchProjectAsync(workspace, settings, fake, limits);
            _projects++;
            _saves = 0;
            fake.SetSelectionLimitsRequests.Clear();
            _savedNoStep = kind == 1;
            _savedEstimate = kind != 0;
            _estimate = _savedEstimate;
            _ticked = _savedNoStep;
            _tickHeld = false;
            _heldUntick = false;
            _savedStep = kind == 0 ? step : ParserWorkLimit.DefaultWork;
            _step = _savedStep;
            _time = kind == 0 ? time : kind == 2 ? EstimateTime() : null;
            if (kind == 2) Assert.True(GoodTime(_time), "an estimated project has an estimate");
            _savedTime = _time ?? _savedTime;
        }

        private async Task CommitAsync()
        {
            log.Add("commit");
            var before = fake.SetSelectionLimitsRequests.Count;
            var wasTicked = _ticked;
            var willSave = _ticked || GoodStep(_step) && GoodTime(_time);
            var refusal = await settings.CommitParsingLimitsAsync();
            if (!willSave)
            {
                Assert.NotNull(refusal);
                Assert.Equal(before, fake.SetSelectionLimitsRequests.Count);
                Resync("refused commit");
                return;
            }
            Assert.Null(refusal);
            Assert.Equal(before + 1, fake.SetSelectionLimitsRequests.Count);
            _saves++;
            var saved = fake.SetSelectionLimitsRequests[^1].Limits;
            if (wasTicked)
            {
                Assert.True(saved.PerWordParserWorkLimit.IsUnbounded, "a ticked commit saves no parser work limit");
                Assert.Equal(SelectionTimeLimitMode.Estimated, saved.TimeMode);
                _savedNoStep = true;
                _savedEstimate = true;
                _tickHeld = false;
                return;
            }
            _savedNoStep = false;
            _heldUntick = false;
            _savedStep = _step!.Value;
            Assert.Equal((long)_savedStep, saved.PerWordParserWorkLimit.Work);
            Assert.Equal(_estimate ? SelectionTimeLimitMode.Estimated : SelectionTimeLimitMode.Explicit, saved.TimeMode);
            _savedEstimate = _estimate;
            _savedTime = _time!.Value;
            if (!_estimate) Assert.Equal((int)decimal.Round(_savedTime * 1000m), saved.ExplicitPerWordLimitMs);
            Assert.False(settings.HasParserWorkLimitRefusal || settings.HasTimeLimitRefusal, "a saved commit leaves no message");
        }

        private void Reset()
        {
            _ticked = _savedNoStep;
            _tickHeld = false;
            _estimate = _savedEstimate;
            _heldUntick = false;
            _step = _savedStep;
            _time = _savedNoStep ? null : _savedTime;
        }

        // After a toggle, a refused commit or leaving, the field shows what was typed or the saved value.
        private void Resync(string when)
        {
            if (_ticked) return;
            var step = settings.ParserWorkLimitValue;
            var time = settings.TimeLimitSeconds;
            Assert.True(step == _step || step == _savedStep, $"{when}: the step field shows {step}, neither the typed {_step} nor the saved {_savedStep}");
            Assert.Equal(_estimate, settings.IsUsingEstimate);
            if (_estimate) Assert.Equal(EstimateTime(), time);
            else Assert.True(time == _time || time == _savedTime, $"{when}: the time field shows {time}, neither the typed {_time} nor the saved {_savedTime}");
            _step = step;
            _time = time;
        }

        public void Check(string after, bool exact)
        {
            var selection = workspace.Selection;
            var request = selection.BuildRequest().PerWordParserWorkLimit;
            string Why(string what) => $"after '{after}': {what}";

            Assert.Null(selection.ParserWorkLimitValidationMessage);
            var mustBeSaved = exact || RefusedShown || _tickHeld;
            var unboundedAllowed = _savedNoStep || _ticked && !_tickHeld;
            if (selection.PerWordParserWorkLimitUnbounded)
            {
                Assert.True(unboundedAllowed, Why("the Selection is unbounded though neither the save nor a live tick says so"));
                Assert.True(request!.IsUnbounded, Why("an unbounded Selection must send an unbounded cap"));
            }
            else
            {
                Assert.False(_savedNoStep && mustBeSaved, Why("the saved no parser work limit is not what the Selection holds"));
                var shown = !mustBeSaved && !_ticked && GoodStep(_step) ? _step : _savedStep;
                Assert.True(selection.PerWordParserWorkLimit == _savedStep || selection.PerWordParserWorkLimit == shown,
                    Why($"the Selection holds step {selection.PerWordParserWorkLimit}; the saved is {_savedStep} and the field shows {_step}"));
                Assert.Equal((long)selection.PerWordParserWorkLimit!.Value, request?.Work);
                if (mustBeSaved && !_savedNoStep) Assert.Equal(_savedStep, selection.PerWordParserWorkLimit);
            }
            var time = selection.PerWordTimeLimitSeconds;
            var timeAllowed = new List<decimal?> { _savedTime };
            if (!mustBeSaved && !_ticked && GoodTime(_time)) timeAllowed.Add(_time);
            if (_ticked && !_tickHeld || _savedNoStep) timeAllowed.Add(null);
            Assert.True(timeAllowed.Contains(time), Why($"the Selection holds time {time}; the saved is {_savedTime} and the field shows {_time}"));
            if (mustBeSaved && !_savedNoStep) Assert.Equal(_savedTime, time);

            // What was saved survives whatever was typed since.
            if (_saves > 0) Assert.Equal(_saves, fake.SetSelectionLimitsRequests.Count);
            else Assert.Empty(fake.SetSelectionLimitsRequests);

            foreach (var (name, announced) in _announced)
                Assert.True(Bound(name) == announced, Why($"{name} is {Bound(name)} but its last change notice said {announced}"));
            if (!_tickHeld) Assert.True(_estimate == settings.IsUsingEstimate, Why($"the time mode is estimate={settings.IsUsingEstimate}; expected {_estimate}"));
            if (_ticked)
            {
                Assert.True(settings.IsNoParserWorkLimit, Why("the tick is not showing"));
                if (!_tickHeld) Assert.Null(settings.TimeLimitSeconds);
                else Assert.True(settings.TimeLimitSeconds is null or > 0, Why($"the disabled time field shows the refused {settings.TimeLimitSeconds}"));
                var shown = settings.ParserWorkLimitValue;
                Assert.True(GoodStep(shown), Why($"the disabled limit field shows the refused {shown}"));
                Assert.True(shown == _step || shown == _savedStep || shown == ParserWorkLimit.DefaultWork,
                    Why($"the ticked limit field shows {shown}, not this project's {_savedStep}, the typed {_step} or the default"));
                return;
            }
            Assert.Equal(_step, settings.ParserWorkLimitValue);
            Assert.Equal(_time, settings.TimeLimitSeconds);
            Assert.True(settings.HasParserWorkLimitRefusal == !GoodStep(_step),
                Why($"the step message is {settings.HasParserWorkLimitRefusal} while the field holds {_step}"));
            Assert.True(settings.HasTimeLimitRefusal == (!_estimate && !GoodTime(_time)),
                Why($"the time message is {settings.HasTimeLimitRefusal} while the field holds {_time}"));
        }
    }
}
