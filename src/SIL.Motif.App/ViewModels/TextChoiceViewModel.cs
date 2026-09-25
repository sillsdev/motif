using CommunityToolkit.Mvvm.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One Text a person can add to a Selection. <see cref="Id"/> is the Text's own GUID and the only thing
/// <see cref="SelectionViewModel"/> composes into a <c>SelectionRequest</c> — never <see cref="Title"/>,
/// which is display text only (AGENTS.md rule 12).
/// </summary>
public sealed partial class TextChoiceViewModel : ObservableObject
{
    public TextChoiceViewModel(
        Guid id, string title, int wordCount = 0, int interlinearizedWordCount = 0,
        double interlinearizationPercent = 0)
    {
        Id = id;
        Title = title;
        WordCount = wordCount;
        InterlinearizedWordCount = interlinearizedWordCount;
        InterlinearizationPercent = interlinearizationPercent;
    }

    public Guid Id { get; }

    public string Title { get; }

    public int WordCount { get; }

    public int InterlinearizedWordCount { get; }

    /// <summary>The share computed by the Text inventory command.</summary>
    public double InterlinearizationPercent { get; }

    public string DistinctCountText => $"{WordCount:N0} distinct";

    public string InterlinearizedText => WordCount == 0
        ? "no words"
        : $"{InterlinearizationPercent:0}% interlinearized";

    [ObservableProperty]
    private bool _isChecked;

    /// <summary>This Text's occurrence count once read; <see langword="null"/> until then.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountsText))]
    private int? _occurrenceCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountsText))]
    private int? _distinctWordCount;

    public string CountsText => OccurrenceCount is { } occurrences && DistinctWordCount is { } distinct
        ? $"{occurrences} words · {distinct} distinct"
        : string.Empty;
}
