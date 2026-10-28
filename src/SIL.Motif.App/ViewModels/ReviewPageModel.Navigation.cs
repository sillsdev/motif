using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ReviewPageModel
{
    private int _selectedFilterIndex;
    private static readonly string[] FilterNames = ["All", "NotLookedAt", "StillFits", "NeedsAttention"];
    private ProjectFilterMemory? _filterMemory;

    private void InitializeFilterMemory() => _filterMemory = new(Context.ViewMemory, "Review",
        () => _selectedFilterIndex == 0 ? null : FilterNames[_selectedFilterIndex], saved =>
        {
            var index = Array.IndexOf(FilterNames, saved);
            SelectedFilterIndex = index < 0 ? 0 : index;
        });
    private IReadOnlyList<object>? _tableEntries;

    /// <summary>The shared table's typed changes and actionable group headings.</summary>
    public IReadOnlyList<object> TableEntries => _tableEntries ??= ReviewEntries.Where(entry => !entry.IsPageHeader)
        .Select<ReviewListEntryViewModel, object>(entry => entry.GrammarGroup is { } grammar
            ? grammar
            : entry.GroupHeader is { } group ? group : entry)
        .ToArray();

    /// <summary>The table row corresponding to the Inspector's current change, without marking it looked at.</summary>
    public object? SelectedTableEntry
    {
        get => TableEntries.OfType<ReviewListEntryViewModel>().FirstOrDefault(row => ReferenceEquals(row.Review, Current));
        set
        {
            if (value is ReviewListEntryViewModel { Review: { } review }) SelectEntry(review);
        }
    }

    /// <summary>
    /// The navigator's filter: all changes, not looked at, still fitting, or needing a decision, in that order.
    /// Filtering changes neither the Proposal nor which remaining changes Apply writes.
    /// </summary>
    public int SelectedFilterIndex
    {
        get => _selectedFilterIndex;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 3);
            if (!SetProperty(ref _selectedFilterIndex, value)) return;
            EnsureVisibleCurrent();
            AfterMove();
            InvalidateVisibleEntries();
            _filterMemory?.Changed();
        }
    }

    public bool HasChangesNeedingAttention => HasNonFittingChanges || HasUncertainChanges;

    /// <summary>Shows the changes that must be checked again or undone before Apply.</summary>
    public IRelayCommand ShowNeedsAttentionCommand { get; }

    /// <summary>Selects a listed change without marking it looked at or changing the Proposal.</summary>
    public void SelectEntry(ReviewEntryViewModel entry)
    {
        var index = _entries.ToList().IndexOf(entry);
        if (index < 0 || !MatchesFilter(entry)) return;
        if (_currentIndex == index && entry.IsCurrent) return;
        _currentIndex = index;
        AfterMove();
    }

    private bool MatchesFilter(ReviewEntryViewModel entry) => _selectedFilterIndex switch
    {
        1 => !entry.IsLookedAt,
        2 => entry.Change?.StillFits == true,
        3 => entry.Change is { IsNoLongerFits: true } or { IsUncertain: true },
        _ => true,
    };

    private int AdjacentVisibleIndex(int direction)
    {
        for (var index = _currentIndex + direction; index >= 0 && index < _entries.Count; index += direction)
            if (MatchesFilter(_entries[index])) return index;
        return -1;
    }

    private void EnsureVisibleCurrent()
    {
        if (Current is { } current && MatchesFilter(current)) return;
        _currentIndex = _entries.ToList().FindIndex(MatchesFilter);
    }

    private void InvalidateVisibleEntries()
    {
        _reviewEntries = null;
        OnPropertyChanged(nameof(ReviewEntries));
        RaiseWalkState();
    }
}
