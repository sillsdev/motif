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
        ? $"Slowest so far: {word.Word} · {SpeedText.Duration(word.ElapsedMs)}" : string.Empty;
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
            "All words have been parsed; finishing the Assessment...",
        { Stage: AssessmentStage.Parsing, Total: { } total } progress =>
            $"{progress.Completed:N0} of {total:N0} words done" +
            (progress.CurrentWord is { } word ? $" · Parsing {word}" : string.Empty),
        { Stage: AssessmentStage.ReadingStatistics } => "All words have been parsed; Motif is reading PanGloss's statistics...",
        _ => _progress?.Message ?? "Preparing to parse words...",
    };
    public string StatusText => _progress switch
    {
        { Stage: AssessmentStage.Parsing, Total: { } total } progress when progress.Completed >= total =>
            "All words have been parsed; finishing the Assessment...",
        { Stage: AssessmentStage.Parsing, Total: { } total } progress =>
            $"Parsing · {progress.Completed:N0} of {total:N0} words · {EstimateText(progress, total)}",
        { Stage: AssessmentStage.ReadingStatistics } => "All words have been parsed; Motif is reading PanGloss's statistics...",
        { Stage: AssessmentStage.Complete } => "Assessment complete",
        _ => _progress?.Message ?? "Preparing to parse words...",
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
                    $"about {Duration(TimeSpan.FromSeconds(Math.Ceiling(_sampleElapsed.TotalSeconds / progress.Completed *
                        (total - progress.Completed))))} left",
                { Stage: AssessmentStage.Parsing, Total: { } total } progress when progress.Completed >= total =>
                    "finishing the Assessment",
                { Stage: AssessmentStage.ReadingStatistics } => "reading PanGloss's statistics",
                { Stage: AssessmentStage.Complete } => "complete",
                _ => "estimating time left",
            };
            return $"{elapsed} elapsed · {detail}";
        }
    }
    public string StoppedText => string.Join("; ", (_progress?.StoppedWords ?? []).GroupBy(word => word.Reason).Select(group =>
        $"{group.Count():N0} word{(group.Count() == 1 ? string.Empty : "s")} " +
        (group.Key == "TIMEOUT"
            ? "timed out" + (_progress?.PerWordLimitMs is { } limit ? $" after {limit / 1000.0:0.#} s" : string.Empty)
            : "stopped at the step limit") + $" ({group.Last().Word})"));
    public string StalledText => IsStalled
        ? _progress?.PerWordLimitMs is null
            ? "No word has finished for 1 min. This search has no per-word time limit. You can cancel it or report a problem."
            : "No word has finished for longer than the word time limit plus 10 s. The parser may have stopped responding."
        : string.Empty;
    public string CancellationText => "Cancel keeps the previous results. This unfinished parse is not saved; your FieldWorks project is unchanged.";

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
        if (progress.Completed < 3) return "estimating time";
        var seconds = _sampleElapsed.TotalSeconds / progress.Completed * Math.Max(0, total - progress.Completed);
        return seconds < 1 ? "<1 s left" : $"about {Duration(TimeSpan.FromSeconds(Math.Ceiling(seconds)))} left";
    }

    private static string Duration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours} h {duration.Minutes} min"
        : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min {duration.Seconds} s"
        : duration.TotalSeconds < 1 ? "<1 s" : $"{(int)duration.TotalSeconds} s";
}

public sealed record StoppedParseWordViewModel(StoppedParseWord Word)
{
    public string Text => $"{Word.Word} · {(Word.Reason == "TIMEOUT" ? "Timed out" : "Stopped at the step limit")} · {Word.ElapsedMs / 1000.0:0.###} s";
}
