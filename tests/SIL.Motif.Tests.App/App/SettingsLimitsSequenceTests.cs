using SIL.Motif.Contract.Assess;
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
/// Random but replayable sequences over the Settings step and time limits. After every step the next parse must send
/// the last saved value, no refused value may reach the shared Selection, and no message may describe a value the
/// field no longer holds. A failure names the seed and the steps so it can be replayed. Projects here are saved with
/// explicit limits and never switch, so the sequences leave out the estimate-mode and project-switch cases.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsLimitsSequenceTests
{
    private static readonly int[] Seeds = [1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233];
    private const int StepsPerSequence = 24;
    private static readonly decimal[] GoodSteps = [20000m, 150000m, 250000m];
    private static readonly decimal[] RefusedSteps = [100000.5m, 0.5m, -3m, 100000000000000000000m];
    private static readonly decimal[] GoodTimes = [2m, 7.5m, 12m];
    private static readonly decimal[] RefusedTimes = [-1m, 0m];

    private enum Act { TypeStep, TypeTime, Tick, Untick, Commit, Escape, LeaveAndReturn }

    private static bool GoodStep(decimal? value) => value is > 0 && decimal.Truncate(value.Value) == value && value <= long.MaxValue;

    private static bool GoodTime(decimal? value) => value is > 0;

    [Fact]
    public void RandomLimitSequencesKeepTheLastSavedValueAndNeverLetARefusedOneIntoTheSelection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            foreach (var seed in Seeds) await RunAsync(seed);
        }, TimeSpan.FromMinutes(4));
    }

    private static async Task RunAsync(int seed)
    {
        var random = new Random(seed);
        var log = new List<string>();
        FakeCommandClient? fake = null;
        var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false,
            configure: (client, _) =>
            {
                fake = client;
                client.DefaultSelectionIs(new NamedSelectionProjection("Default",
                    [Guid.Parse("11111111-0000-0000-0000-000000000001")], [], string.Empty, string.Empty,
                    new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Explicit, 5000, 600), "1"));
            });
        try
        {
            var settings = SettingsTestAccess.Model(frame);
            workspace.CurrentPage = WorkspacePage.Settings;
            settings.OpenGroup(Group.Parsing);
            var model = new Model(workspace, settings, fake!, log);
            model.Check("opened", exact: true);
            for (var index = 0; index < StepsPerSequence && !model.Over; index++)
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
        private decimal _savedStep = StepCap.DefaultSteps;
        private decimal _savedTime = 5m;
        private bool _ticked;
        private decimal? _step = StepCap.DefaultSteps;
        private decimal? _time = 5m;
        private int _saves;
        private readonly HashSet<decimal> _seenSteps = [], _seenTimes = [];

        public bool Over { get; private set; }

        public Act Pick(Random random)
        {
            var allowed = new List<Act> { Act.Escape, Act.LeaveAndReturn, Act.Commit };
            if (!_ticked) allowed.AddRange([Act.TypeStep, Act.TypeStep, Act.TypeTime, Act.TypeTime, Act.Tick]);
            if (_ticked && !_savedNoStep) allowed.Add(Act.Untick);
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
                    if (GoodStep(_step)) _seenSteps.Add(_step!.Value);
                    settings.StepLimit = _step;
                    break;
                case Act.TypeTime:
                    _time = random.Next(2) == 0 ? GoodTimes[random.Next(GoodTimes.Length)] : RefusedTimes[random.Next(RefusedTimes.Length)];
                    log.Add($"time={_time}");
                    if (GoodTime(_time)) _seenTimes.Add(_time!.Value);
                    settings.TimeLimitSeconds = _time;
                    break;
                case Act.Tick:
                    log.Add("tick");
                    _ticked = true;
                    settings.IsNoStepLimit = true;
                    break;
                case Act.Untick:
                    log.Add("untick");
                    _ticked = false;
                    settings.IsNoStepLimit = false;
                    Resync("untick");
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
                case Act.LeaveAndReturn:
                    log.Add("leave+return");
                    workspace.CurrentPage = WorkspacePage.Overview;
                    workspace.CurrentPage = WorkspacePage.Settings;
                    settings.OpenGroup(Group.Parsing);
                    exact = !_ticked && (!GoodStep(_step) || !GoodTime(_time)) || _ticked && !_savedNoStep && false;
                    Resync("leave");
                    break;
            }
            Check(log[^1], exact);
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
                Assert.True(saved.PerWordStepLimit.IsUnbounded, "a ticked commit saves no step limit");
                _savedNoStep = true;
                Over = true;
                return;
            }
            _savedNoStep = false;
            _savedStep = _step!.Value;
            _savedTime = _time!.Value;
            Assert.Equal((long)_savedStep, saved.PerWordStepLimit.Steps);
            Assert.Equal(SelectionTimeLimitMode.Explicit, saved.TimeMode);
            Assert.Equal((int)decimal.Round(_savedTime * 1000m), saved.ExplicitPerWordLimitMs);
            Assert.False(settings.HasStepLimitRefusal || settings.HasTimeLimitRefusal, "a saved commit leaves no message");
        }

        private void Reset()
        {
            _ticked = _savedNoStep;
            _step = _savedStep;
            _time = _savedTime;
        }

        // After a toggle, a refused commit or leaving, the field shows what was typed or the saved value.
        private void Resync(string when)
        {
            if (_ticked) return;
            var step = settings.StepLimit;
            var time = settings.TimeLimitSeconds;
            Assert.True(step == _step || step == _savedStep, $"{when}: the step field shows {step}, neither the typed {_step} nor the saved {_savedStep}");
            Assert.True(time == _time || time == _savedTime, $"{when}: the time field shows {time}, neither the typed {_time} nor the saved {_savedTime}");
            _step = step;
            _time = time;
        }

        public void Check(string after, bool exact)
        {
            var selection = workspace.Selection;
            var request = selection.BuildRequest().PerWordStepLimit;
            string Why(string what) => $"after '{after}': {what}";

            Assert.Null(selection.StepLimitValidationMessage);
            // The saved value, or a good value typed since and not yet saved or cancelled.
            var unboundedAllowed = _savedNoStep || _ticked;
            if (selection.PerWordStepLimitUnbounded)
            {
                Assert.True(unboundedAllowed, Why("the Selection is unbounded though neither the save nor a held tick says so"));
                Assert.True(request!.IsUnbounded, Why("an unbounded Selection must send an unbounded cap"));
            }
            else
            {
                Assert.False(_savedNoStep && exact, Why("the saved no step limit is not what the Selection holds"));
                var allowed = new List<decimal> { _savedStep };
                if (!exact) allowed.AddRange(_seenSteps);
                Assert.True(selection.PerWordStepLimit is { } held && allowed.Contains(held),
                    Why($"the Selection holds step {selection.PerWordStepLimit}; the saved is {_savedStep} and the typed {_step}"));
                Assert.Equal((long)selection.PerWordStepLimit!.Value, request?.Steps);
                if (exact && !_savedNoStep) Assert.Equal(_savedStep, selection.PerWordStepLimit);
            }
            var time = selection.PerWordTimeLimitSeconds;
            var timeAllowed = new List<decimal?> { _savedTime };
            if (!exact) timeAllowed.AddRange(_seenTimes.Select(t => (decimal?)t));
            if (_ticked || _savedNoStep) timeAllowed.Add(null);
            Assert.True(timeAllowed.Contains(time), Why($"the Selection holds time {time}; the saved is {_savedTime} and the typed {_time}"));
            if (exact && !_savedNoStep) Assert.Equal(_savedTime, time);

            // What was saved survives whatever was typed since.
            if (_saves > 0) Assert.Equal(_saves, fake.SetSelectionLimitsRequests.Count);
            else Assert.Empty(fake.SetSelectionLimitsRequests);

            if (_ticked) return;
            Assert.Equal(_step, settings.StepLimit);
            Assert.Equal(_time, settings.TimeLimitSeconds);
            Assert.True(settings.HasStepLimitRefusal == !GoodStep(_step),
                Why($"the step message is {settings.HasStepLimitRefusal} while the field holds {_step}"));
            Assert.True(settings.HasTimeLimitRefusal == !GoodTime(_time),
                Why($"the time message is {settings.HasTimeLimitRefusal} while the field holds {_time}"));
        }
    }
}
