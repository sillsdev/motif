using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>One change in Review changes' single list: what it is, whether it was looked at, and Keep and Undo.</summary>
public sealed partial class ReviewEntryViewModel : ObservableObject
{
    internal ReviewEntryViewModel(ReviewPageModel page, ReviewEntry? entry, ChangeViewModel? change)
    {
        if (entry is null && change is null)
            throw new ArgumentException("A review entry needs the review's entry or a word change.", nameof(entry));
        Entry = entry;
        Change = change;
        _isLookedAt = entry?.LookedAt == true;
        if (change is not null) change.IsLookedAt = _isLookedAt;
        KeepCommand = new AsyncRelayCommand(() => page.KeepAsync(this), () => Entry is not null);
        UndoCommand = new AsyncRelayCommand(() => page.UndoAsync(this));
    }

    /// <summary>
    /// The entry as the review read it, or <see langword="null"/> for a word change made since the list was read,
    /// which can be undone but not yet marked looked at.
    /// </summary>
    public ReviewEntry? Entry { get; }

    /// <summary>The existing word row this entry presents, when it is a change to a word's analyses.</summary>
    public ChangeViewModel? Change { get; }

    /// <summary>The key that identifies this entry within its Proposal: a word change's id, else its first operation.</summary>
    public string Key => Entry?.EntryKey ?? Change!.ChangeId;

    public string Title => Entry?.Title ?? ChangeVocabulary.WordTitle(Change!.Word, Change.Kind);

    /// <summary>Whether this is a change to the grammar or lexicon rather than to a word.</summary>
    public bool IsGrammar => Change is null;

    /// <summary>The word change's fit in the window's words, separate from its staged Opinion.</summary>
    public string FitText => Change?.ReviewFitText ?? string.Empty;

    /// <summary>The word's parser outcome, or the grammar change's recorded effects.</summary>
    public string TableOutcomeText => Change?.Listed?.Row.CompactOutcomeWord ??
        (HasEffects ? "Changes observed" : "No effects recorded");

    /// <summary>The compact fit label beside a change, with its full status in <see cref="TableFitDescription"/>.</summary>
    public string TableFitText => Change switch
    {
        null => "At Apply",
        { IsNoLongerFits: true } => "No fit",
        { IsUncertain: true } => "Recheck",
        { StillFits: true } => "Still fits",
        _ => "Unchecked",
    };

    /// <summary>The complete fit status, including when a grammar change is checked under ADR 0046.</summary>
    public string TableFitDescription => Change?.ReviewFitText ?? "Checked on Apply";

    public string FitGlyph => Change is { IsNoLongerFits: true } ? "✗"
        : Change is { IsUncertain: true } ? "!" : Change is { StillFits: true } ? "✓" : string.Empty;

    public string TableKeepAutomationName => Change is { } change ? $"Keep: {change.Word}" : KeepAutomationName;

    public string TableUndoAutomationName => Change?.UndoAutomationName ?? UndoAutomationName;

    public string MoreAutomationName => $"More for {Change?.Word ?? Title}";

    /// <summary>What a grammar change makes, counted in a linguist's nouns, or empty for a word change.</summary>
    public string Summary => Entry is GrammarChangeEntry grammar ? grammar.Summary : string.Empty;

    /// <summary>The parts of a retirement, one per line, or empty.</summary>
    public string PartsText => Entry is GrammarChangeEntry grammar
        ? string.Join("\n", grammar.Parts.Select(part => part.Title)) : string.Empty;

    public bool HasParts => PartsText.Length > 0;

    /// <summary>What the Dry Run observed this change doing, one line per field that moved.</summary>
    public string EffectText => Entry is GrammarChangeEntry grammar
        ? string.Join("\n", grammar.Effects.SelectMany(effect => effect.Changes.Select(change =>
            $"{effect.Field}: {change.Before ?? "(none)"} → {change.After ?? "(none)"}")).Take(6))
        : string.Empty;

    public bool HasEffects => EffectText.Length > 0;

    /// <summary>Whether the current content and Trial evidence were looked at.</summary>
    public bool IsLookedAt
    {
        get => _isLookedAt;
        set
        {
            if (!SetProperty(ref _isLookedAt, value)) return;
            if (Change is not null) Change.IsLookedAt = value;
        }
    }

    private bool _isLookedAt;

    /// <summary>Whether Previous and Next are standing on this entry.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    public string KeepAutomationName => $"Keep: {Title}";

    public string UndoAutomationName => $"Undo: {Title}";

    /// <summary>Marks this entry looked at and moves to the next one; it changes nothing in the Proposal.</summary>
    public IAsyncRelayCommand KeepCommand { get; }

    /// <summary>Takes this entry's changes out of the Proposal.</summary>
    public IAsyncRelayCommand UndoCommand { get; }
}

/// <summary>The question Undo asks before it also takes out the changes that depend on the one being undone.</summary>
/// <param name="Undone">The titles of the changes the person chose to undo.</param>
/// <param name="Titles">The titles of the dependent changes that would go too.</param>
/// <param name="NeedsAnotherTry">Whether the Draft is an open Proposal, which then has to be tried again.</param>
/// <param name="UndoAllOfThem">Takes out the change and every dependent.</param>
/// <param name="Cancel">Leaves everything as it was.</param>
public sealed record UndoPromptViewModel(
    IReadOnlyList<string> Undone, IReadOnlyList<string> Titles, bool NeedsAnotherTry, IAsyncRelayCommand UndoAllOfThem,
    IRelayCommand Cancel)
{
    /// <summary>The sentence the prompt leads with, naming what goes and, for a Proposal, what follows.</summary>
    public string Sentence => $"Undoing {Quoted(Undone)} also undoes {Quoted(Titles)}." +
        (NeedsAnotherTry ? " Try the rest again before Apply." : string.Empty);

    /// <summary>The words on the button that undoes them all: "Undo both" for two.</summary>
    public string UndoAllText => Undone.Count + Titles.Count == 2 ? "Undo both"
        : $"Undo these {Undone.Count + Titles.Count}";

    private static string Quoted(IReadOnlyList<string> titles) =>
        string.Join(", ", titles.Select(title => $"“{title}”"));
}

/// <summary>The band over the grammar changes, worded as a word group's band is.</summary>
public sealed class ReviewGrammarGroupViewModel(int count) : IDenseTableGroupHeading
{
    public string Title => "Grammar changes";

    /// <summary>How many grammar changes the band heads, beside its title as a word group's count is.</summary>
    public string CountText => PluralText.Format(MotifAppStrings.ChangeGroupCountPattern, count);
}

/// <summary>One thing Apply checks for an open Proposal, and whether it passed.</summary>
/// <param name="Text">The check in the window's words.</param>
/// <param name="Passed">Whether it passed, which shows as a tick or a cross.</param>
public sealed record ApplyCheck(string Text, bool Passed)
{
    /// <summary>The check with its mark, read the same by eye and by a screen reader.</summary>
    public string MarkedText => (Passed ? "✓ " : "✗ ") + Text;
}
