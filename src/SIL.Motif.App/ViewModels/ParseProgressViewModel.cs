using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Shows observed word progress and estimates from completed searches, with a clock for long waits.</summary>
public sealed class ParseProgressViewModel(TimeProvider clock) : ObservableObject
{
    private long _started;
    private long? _parsingStarted;
    private long _lastFinished;
    private TimeSpan _sampleElapsed;
    private AssessmentProgress? _progress;

    public bool IsActive { get; private set; }
    public bool IsStalled { get; private set; }
    public IReadOnlyList<StoppedParseWordViewModel> StoppedWords =>
        (_progress?.StoppedWords ?? []).Select(word => new StoppedParseWordViewModel(word)).ToArray();
    public bool HasStoppedWords => StoppedWords.Count > 0;
    public bool HasSlowestWord => _progress?.SlowestWord is not null;
    public string SlowestText => _progress?.SlowestWord is { } word
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressSlowestFormat, word.Word, SpeedText.Duration(word.ElapsedMs)) : string.Empty;
    public double Fraction => _progress switch
    {
        { Stage: AssessmentStage.Complete } => 1,
        { Stage: AssessmentStage.Parsing, Total: > 0 } progress =>
            Math.Min(0.98, (double)progress.Completed / progress.Total.Value),
        { Total: > 0 } progress => (double)progress.Completed / progress.Total.Value,
        _ => 0,
    };
    public bool IsIndeterminate => _progress?.Total is null;
    public string ProgressText => _progress switch
    {
        { Stage: AssessmentStage.Parsing, Total: { } total } progress when progress.Completed >= total =>
            MotifAppStrings.ProgressFinishing,
        { Stage: AssessmentStage.Parsing, Total: { } total } progress => progress.CurrentWord is { } word
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressWordsDoneParsingFormat, progress.Completed.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), total.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), word)
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressWordsDoneFormat, progress.Completed.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), total.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)),
        { Stage: AssessmentStage.ReadingStatistics } => MotifAppStrings.ProgressReadingStatistics,
        _ => _progress?.Message ?? MotifAppStrings.ProgressPreparing,
    };
    public string StatusText => _progress switch
    {
        { Stage: AssessmentStage.Parsing, Total: { } total } progress when progress.Completed >= total =>
            MotifAppStrings.ProgressFinishing,
        { Stage: AssessmentStage.Parsing, Total: { } total } progress =>
            string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressStatusParsingFormat, progress.Completed.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), total.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), EstimateText(progress, total)),
        { Stage: AssessmentStage.ReadingStatistics } => MotifAppStrings.ProgressReadingStatistics,
        { Stage: AssessmentStage.Complete } => MotifAppStrings.ProgressComplete,
        _ => _progress?.Message ?? MotifAppStrings.ProgressPreparing,
    };
    public string TimeText
    {
        get
        {
            var elapsed = Duration(clock.GetElapsedTime(_started));
            var detail = _progress switch
            {
                { Stage: AssessmentStage.Parsing, Completed: >= 3, Total: { } total } progress
                    when progress.Completed < total =>
                    string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressAboutLeftFormat, Duration(TimeSpan.FromSeconds(Math.Ceiling(_sampleElapsed.TotalSeconds / progress.Completed *
                        (total - progress.Completed))))),
                { Stage: AssessmentStage.Parsing, Total: { } total } progress when progress.Completed >= total =>
                    MotifAppStrings.ProgressTimeFinishing,
                { Stage: AssessmentStage.ReadingStatistics } => MotifAppStrings.ProgressTimeReadingStatistics,
                { Stage: AssessmentStage.Complete } => MotifAppStrings.ProgressTimeComplete,
                _ => MotifAppStrings.ProgressTimeEstimating,
            };
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressElapsedFormat, elapsed, detail);
        }
    }
    public string StoppedText => string.Join("; ", (_progress?.StoppedWords ?? []).GroupBy(word => word.Reason).Select(group =>
        group.Key == "TIMEOUT"
            ? _progress?.PerWordLimitMs is { } limit
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressStoppedTimedOutAfterFormat, PluralText.Format(MotifAppStrings.ProgressStoppedWordsPattern, group.Count()), (limit / 1000.0).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture), group.Last().Word)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressStoppedTimedOutFormat, PluralText.Format(MotifAppStrings.ProgressStoppedWordsPattern, group.Count()), group.Last().Word)
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressStoppedSearchLimitFormat, PluralText.Format(MotifAppStrings.ProgressStoppedWordsPattern, group.Count()), group.Last().Word)));
    public string StalledText => IsStalled
        ? _progress?.PerWordLimitMs is null
            ? MotifAppStrings.ProgressStalledNoLimit
            : MotifAppStrings.ProgressStalledWithLimit
        : string.Empty;
    public string CancellationText => MotifAppStrings.ProgressCancellation;

    public void Report(AssessmentProgress progress)
    {
        if (!IsActive) return;
        var now = clock.GetTimestamp();
        if (progress.Stage == AssessmentStage.Parsing)
        {
            if (progress.Completed == 0 && progress.CurrentWord is not null && _progress?.CurrentWord is null)
                _parsingStarted = _lastFinished = now;
            _parsingStarted ??= now;
            if (_progress?.Stage != AssessmentStage.Parsing || progress.Completed > _progress.Completed)
            {
                _lastFinished = now;
                _sampleElapsed = clock.GetElapsedTime(_parsingStarted.Value, now);
            }
        }
        _progress = progress;
        Tick();
    }

    public async Task<T> TrackAsync<T>(Func<Task<T>> run)
    {
        _started = clock.GetTimestamp();
        _lastFinished = _started;
        _parsingStarted = null;
        _sampleElapsed = TimeSpan.Zero;
        _progress = null;
        IsActive = true;
        IsStalled = false;
        Notify();
        try
        {
            var task = run();
            while (!task.IsCompleted)
            {
                await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(1), clock)).ConfigureAwait(true);
                Tick();
            }
            return await task.ConfigureAwait(true);
        }
        finally
        {
            IsActive = false;
            IsStalled = false;
            Notify();
        }
    }

    private void Tick()
    {
        IsStalled = IsActive && _progress is { Stage: AssessmentStage.Parsing } progress &&
            progress.Completed < progress.Total && clock.GetElapsedTime(_lastFinished) >
                TimeSpan.FromMilliseconds(progress.PerWordLimitMs is { } limit ? limit + 10_000L : 60_000L);
        Notify();
    }

    private void Notify()
    {
        foreach (var property in new[] { nameof(IsActive), nameof(IsStalled), nameof(ProgressText), nameof(TimeText),
            nameof(StatusText), nameof(StoppedText), nameof(StoppedWords), nameof(HasStoppedWords), nameof(StalledText),
            nameof(Fraction), nameof(IsIndeterminate), nameof(HasSlowestWord), nameof(SlowestText) }) OnPropertyChanged(property);
    }

    private string EstimateText(AssessmentProgress progress, int total)
    {
        if (progress.Completed < 3) return MotifAppStrings.ProgressEstimatingTime;
        var seconds = _sampleElapsed.TotalSeconds / progress.Completed * Math.Max(0, total - progress.Completed);
        return seconds < 1 ? MotifAppStrings.ProgressLessThanSecondLeft
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressAboutLeftFormat, Duration(TimeSpan.FromSeconds(Math.Ceiling(seconds))));
    }

    private static string Duration(TimeSpan duration) => duration.TotalHours >= 1
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressDurationHoursFormat, (int)duration.TotalHours, duration.Minutes)
        : duration.TotalMinutes >= 1 ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressDurationMinutesFormat, (int)duration.TotalMinutes, duration.Seconds)
        : duration.TotalSeconds < 1 ? MotifAppStrings.ProgressDurationLessThanSecond
        : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressDurationSecondsFormat, (int)duration.TotalSeconds);
}

public sealed record StoppedParseWordViewModel(StoppedParseWord Word)
{
    public string Text => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProgressStoppedWordFormat, Word.Word, Word.Reason == "TIMEOUT" ? MotifAppStrings.ProgressTimedOut : MotifAppStrings.ProgressStoppedAtSearchLimit, (Word.ElapsedMs / 1000.0).ToString("0.###", System.Globalization.CultureInfo.CurrentCulture));
}
