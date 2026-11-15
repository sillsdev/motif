using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Sets exactly two open Proposals side by side. It reads only what their documents already hold and never subtracts
/// unless <see cref="ProposalComparability"/> finds both measured against one body of evidence; otherwise it says why
/// not and offers an explicit measure-again action.
/// </summary>
public sealed class ProposalCompareViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceContext _context;
    private readonly Action _back;
    private readonly Func<ProposalCardViewModel, Task>? _tryAgain;
    private readonly ProposalDocumentViewModel[] _documents;
    private readonly List<(INotifyPropertyChanged Source, PropertyChangedEventHandler Handler)> _watches = [];
    private ProposalComparability _comparability;
    private IReadOnlyList<ProposalComparisonRow> _rows = [];
    private bool _remeasuring;
    private bool _disposed;

    /// <summary>Compares the two documents a person checked.</summary>
    /// <param name="context">The workspace the documents belong to.</param>
    /// <param name="pair">Exactly the two checked documents, in the order they were opened.</param>
    /// <param name="back">Returns to the open documents; their edits and checks stay.</param>
    /// <param name="tryAgain">Measures a Proposal's Trial again only when a person chooses to measure again.</param>
    public ProposalCompareViewModel(WorkspaceContext context, IReadOnlyList<ProposalDocumentViewModel> pair, Action back,
        Func<ProposalCardViewModel, Task>? tryAgain = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(back);
        if (pair.Count != 2) throw new ArgumentException("A comparison needs exactly two Proposals.", nameof(pair));
        _context = context;
        _back = back;
        _tryAgain = tryAgain;
        _documents = [.. pair];
        First = new ProposalComparisonSide(pair[0], MotifAppStrings.ProposalCompareFirstHeading);
        Second = new ProposalComparisonSide(pair[1], MotifAppStrings.ProposalCompareSecondHeading);
        BackCommand = new RelayCommand(_back);
        RemeasureCommand = new AsyncRelayCommand(RemeasureAsync, () => !_disposed && !IsRemeasuring && CanRemeasure);
        foreach (var document in _documents)
        {
            Watch(document);
            Watch(document.Evidence);
            Watch(document.Difference);
            Watch(document.Difference.Maker);
        }
        Watch(context.Evidence);
        Watch(context);
        _comparability = ProposalComparability.Evaluate(context, _documents[0], _documents[1]);
        _rows = BuildRows();
        First.Refresh(IsComparable);
        Second.Refresh(IsComparable);
    }

    public ProposalComparisonSide First { get; }
    public ProposalComparisonSide Second { get; }
    public IRelayCommand BackCommand { get; }
    public IAsyncRelayCommand RemeasureCommand { get; }

    /// <summary>Where the comparison stands.</summary>
    public ProposalComparisonState State => IsRemeasuring ? ProposalComparisonState.Reading : _comparability.State;

    /// <summary>Why the comparison is not offered; <see cref="ProposalComparisonReason.None"/> when it is.</summary>
    public ProposalComparisonReason Reason => IsRemeasuring ? ProposalComparisonReason.Reading : _comparability.Reason;

    public bool IsComparable => State == ProposalComparisonState.Comparable;
    public bool IsRemeasuring
    {
        get => _remeasuring;
        private set
        {
            if (SetProperty(ref _remeasuring, value)) Raise();
        }
    }

    /// <summary>Whether a notice replaces the effects.</summary>
    public bool ShowsNotice => !IsComparable;
    public bool CanRemeasure => !IsRemeasuring && _comparability.CanRemeasure;

    /// <summary>The notice's heading: outdated, unavailable or reading.</summary>
    public string NoticeTitle => State switch
    {
        ProposalComparisonState.Outdated => MotifAppStrings.ProposalCompareOutdatedTitle,
        ProposalComparisonState.Unavailable => MotifAppStrings.ProposalCompareUnavailableTitle,
        ProposalComparisonState.Reading => IsRemeasuring ? MotifAppStrings.ProposalCompareRemeasuring :
            MotifAppStrings.ProposalCompareReading,
        _ => string.Empty,
    };

    /// <summary>Why the comparison is not offered.</summary>
    public string NoticeText => IsRemeasuring ? string.Empty : _comparability.ReasonText;

    /// <summary>
    /// The sentence that says both were measured against one body of evidence; empty unless the evidence proves it.
    /// </summary>
    public string BasisText => IsComparable && First.Document.Difference.Summary is { } summary
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalCompareBasisFormat, summary.TextTitle)
        : string.Empty;

    /// <summary>The words one Proposal treats differently from the other, in reading order of the first's lists.</summary>
    public IReadOnlyList<ProposalComparisonRow> Rows => IsComparable ? _rows : [];
    public bool HasRows => IsComparable && _rows.Count > 0;
    public bool ShowsNoDifference => IsComparable && _rows.Count == 0;

    /// <summary>Whether the standings say the two change some of the same words; this proves overlap and nothing more.</summary>
    public bool HasOverlap => Overlaps(_documents[0], _documents[1]) || Overlaps(_documents[1], _documents[0]);

    private static bool Overlaps(ProposalDocumentViewModel x, ProposalDocumentViewModel y) =>
        x.Card?.Card.AlsoChangedBy.Contains(y.ProposalId) == true;

    /// <summary>The documents' diagnostics slots show only while Diagnostics is on.</summary>
    public bool DiagnosticsVisible => _context.Diagnostics;

    private async Task RemeasureAsync()
    {
        IsRemeasuring = true;
        try
        {
            foreach (var document in _documents.Where(NeedsRemeasure))
            {
                if (_disposed) return;
                if (_tryAgain is not null && document.Card is { CanTry: true } card && document.IsOutdated)
                    await _tryAgain(card).ConfigureAwait(true);
                await document.RefreshAsync().ConfigureAwait(true);
            }
            var text = CommonTextId();
            var made = false;
            foreach (var document in _documents)
            {
                if (_disposed) return;
                var difference = document.Difference;
                if (text is null || (difference.IsShown && difference.Summary?.TextId == text) || !difference.OffersMake)
                    continue;
                var choice = difference.TextChoices.FirstOrDefault(item => item.Id.ToString("D") == text);
                if (choice is null) continue;
                difference.SelectedText = choice;
                await difference.Maker.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
                made = true;
            }
            if (made)
                foreach (var document in _documents)
                    if (!_disposed) await document.RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            IsRemeasuring = false;
            Recompute();
        }
    }

    // A side that reads, is current and has evidence is left alone; only the one that does not is measured again.
    private bool NeedsRemeasure(ProposalDocumentViewModel document) =>
        document.Detail is null || document.DetailRefusal is not null || document.IsOutdated ||
        !document.Difference.IsShown || !document.Evidence.CanShowFacts;

    private string? CommonTextId()
    {
        foreach (var document in _documents)
            if (!NeedsRemeasure(document) && document.Difference.Summary is { } summary) return summary.TextId;
        return _documents.Select(document => document.Difference.SelectedText).OfType<DifferenceTextChoice>()
            .Select(choice => choice.Id.ToString("D")).FirstOrDefault();
    }

    private IReadOnlyList<ProposalComparisonRow> BuildRows()
    {
        if (_comparability.State != ProposalComparisonState.Comparable) return [];
        var first = Effects(First);
        var second = Effects(Second);
        var rows = new List<ProposalComparisonRow>();
        foreach (var word in first.Keys.Concat(second.Keys.Where(key => !first.ContainsKey(key))))
        {
            first.TryGetValue(word, out var left);
            second.TryGetValue(word, out var right);
            if (left?.Effect.Effect == right?.Effect.Effect && left?.Result == right?.Result) continue;
            var same = left is not null && right is not null && left.Effect.Effect == right.Effect.Effect;
            rows.Add(new ProposalComparisonRow(word, Say(left, same), Say(right, same),
                new RelayCommand(() => _context.OpenWord(word)), (left ?? right)?.Effect.WritingSystemId));
        }
        return rows;
    }

    private static string Say(SideEffect? effect, bool differentResult) => effect is null
        ? MotifAppStrings.ProposalCompareNoChange
        : differentResult ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalCompareDifferentResult,
            effect.Effect.Effect) : effect.Effect.Effect;

    private sealed record SideEffect(ProposalEvidenceWord Effect, string Result);

    // A complete side leaves an unlisted word unchanged; one label can hide different results.
    private static Dictionary<string, SideEffect> Effects(ProposalComparisonSide side)
    {
        var facts = side.Document.Difference.EvidenceWords.GroupBy(word => word.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Facts, StringComparer.Ordinal);
        var effects = new Dictionary<string, SideEffect>(StringComparer.Ordinal);
        foreach (var word in side.WillFillIn.Concat(side.WillChange))
            effects.TryAdd(word.Word, new SideEffect(word, facts.TryGetValue(word.Word, out var fact) ? Result(fact) : string.Empty));
        return effects;
    }

    private static string Result(ProposalDifferenceCaseFacts facts) => System.Text.Json.JsonSerializer.Serialize(
        new object[] { facts.GainedIdentities, facts.LostIdentities, facts.After.SpellingAcceptance, facts.After.ProducedIdentityCount });

    private void Watch(INotifyPropertyChanged source)
    {
        PropertyChangedEventHandler handler = (_, _) => Recompute();
        source.PropertyChanged += handler;
        _watches.Add((source, handler));
    }

    private void Recompute()
    {
        if (_disposed) return;
        _comparability = ProposalComparability.Evaluate(_context, _documents[0], _documents[1]);
        _rows = BuildRows();
        First.Refresh(IsComparable);
        Second.Refresh(IsComparable);
        Raise();
    }

    private void Raise()
    {
        foreach (var name in new[] { nameof(State), nameof(Reason), nameof(IsComparable), nameof(ShowsNotice), nameof(CanRemeasure),
                     nameof(NoticeTitle), nameof(NoticeText), nameof(BasisText), nameof(Rows), nameof(HasRows),
                     nameof(ShowsNoDifference), nameof(HasOverlap), nameof(DiagnosticsVisible) })
            OnPropertyChanged(name);
        RemeasureCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Stops listening to the documents; the documents themselves stay open.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (source, handler) in _watches) source.PropertyChanged -= handler;
        _watches.Clear();
    }
}
