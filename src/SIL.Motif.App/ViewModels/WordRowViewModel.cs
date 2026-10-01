using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.App.ViewModels;

/// <summary>Where a word row's next steps lead: the page that hosts the rows fills these in.</summary>
public sealed class WordRowRoutes
{
    /// <summary>Opens a word in the Texts page, where its places in the chosen Texts are.</summary>
    public Action<string>? OpenInText { get; set; }

    /// <summary>Opens Try a Word on a word.</summary>
    public Action<string>? TryWord { get; set; }
}

/// <summary>
/// One word as every page's word row shows it, in the window's marks and words: what FieldWorks holds, what PanGloss
/// built, what the two mean together, where the word occurs, and the three next steps, which are always present.
/// </summary>
public sealed partial class WordRowViewModel : ObservableObject
{
    private const string WordAnalysesTool = "Analyses";
    private readonly WordRowRoutes? _routes;

    public WordRowViewModel(WordRow row, WordRowRoutes? routes = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        _row = row;
        _routes = routes;
        _isUnread = row.IsUnread;
        var standing = WordProjectStatuses.FromStanding(row.Opinion);
        OpinionMark = WordProjectStatuses.MarkOf(standing);
        OpinionLabel = WindowWords.LabelOf(standing);
        FieldWorksMorphemes = row.FieldWorksMorphemes.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        var differing = row.DifferingPositions.ToHashSet();
        PanGlossMorphemes = row.PanGlossMorphemes.Select((morph, index) =>
            new WordRowMorphemeViewModel(new ParserReadingMorphViewModel(morph), differing.Contains(index + 1))).ToArray();
        Outcome = OutcomeOf(row.Outcome);
        Tone = ToneOf(row.Tone);
        WordAnalysesLink = row.WordAnalysesLink is { } link ? new Uri(link) : null;
        OpenInTextCommand = new RelayCommand(() => _routes?.OpenInText?.Invoke(Word));
        TryWordCommand = new RelayCommand(() => _routes?.TryWord?.Invoke(Word));
    }

    private WordRow _row;

    /// <summary>The row's facts as the projection gave them, with the read state as it is now.</summary>
    public WordRow Row => _row;

    public string Word => _row.Word;

    /// <summary>The FieldWorks analysis's glosses, morph by morph; empty when FieldWorks holds none.</summary>
    public string Gloss => _row.Gloss;

    /// <summary>What FieldWorks holds, as its opinion mark; an incorrect spelling has none.</summary>
    public Mark? OpinionMark { get; }

    /// <summary>What FieldWorks holds, in its own words.</summary>
    public string OpinionLabel { get; }

    /// <summary>The morphemes of the analysis FieldWorks holds, each a way into its entry.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> FieldWorksMorphemes { get; }

    /// <summary>PanGloss's morphemes when it built something different, each marked when FieldWorks lacks it.</summary>
    public IReadOnlyList<WordRowMorphemeViewModel> PanGlossMorphemes { get; }

    public bool HasPanGlossMorphemes => PanGlossMorphemes.Count > 0;

    /// <summary>What PanGloss built.</summary>
    public ParserOutcome Outcome { get; }

    public Mark OutcomeMark => Mark.Of(Outcome);

    public string OutcomeWord => WindowWords.Of(Outcome);

    /// <summary>What the opinion and the outcome mean together, in the Matrix's words.</summary>
    public string Meaning => _row.Meaning;

    public MeaningTone Tone { get; }

    public Mark MeaningMark => Mark.Of(Tone);

    /// <summary>How many grammar warnings name something the word uses, or <see langword="null"/> when not known.</summary>
    public int? WarningsNamed => _row.WarningsNamed;

    public bool HasWarningsNamed => WarningsNamed is > 0;

    /// <summary>How many places the word occurs, such as <c>×3</c>, or empty when not known.</summary>
    public string PlacesText => _row.Places is { } places ? $"×{places}" : string.Empty;

    /// <summary>How long the parser took, or empty for a word it did not time.</summary>
    public string ElapsedText => _row.ElapsedMs is not { } ms ? string.Empty : ms == 0 ? "<1 ms" : $"{ms:N0} ms";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnread))]
    [NotifyPropertyChangedFor(nameof(Row))]
    private bool? _isUnread;

    partial void OnIsUnreadChanged(bool? value) => _row = _row with { IsUnread = value };

    /// <summary>Whether to show the Unread mark; a read word, or one whose read state is unknown, shows nothing.</summary>
    public bool ShowUnread => IsUnread == true;

    public string UnreadText => "Unread";

    /// <summary>The one-line summary a hover or focus shows, in window words.</summary>
    public string Summary => $"{Word} · {OpinionLabel} · PanGloss: {OutcomeWord} · {Meaning}";

    public string OpenInTextLabel => "Open in text";

    public IRelayCommand OpenInTextCommand { get; }

    public string TryWordLabel => "Try a Word";

    public IRelayCommand TryWordCommand { get; }

    /// <summary>The link selecting the word in FieldWorks' Word Analyses, or <see langword="null"/> when there is none.</summary>
    public Uri? WordAnalysesLink { get; }

    public bool HasWordAnalysesLink => WordAnalysesLink is not null;

    public string WordAnalysesLabel => $"{FieldWorksLinks.ToolName(WordAnalysesTool)} ↗";

    /// <summary>The accessible name and tip of the Word Analyses link.</summary>
    public string WordAnalysesName => $"Open {Word} in {FieldWorksLinks.ToolName(WordAnalysesTool)}";

    /// <summary>Why the Word Analyses step cannot open, or empty when it can.</summary>
    public string WordAnalysesDisabledReason => HasWordAnalysesLink ? string.Empty
        : $"FieldWorks has no wordform spelled {Word}";

    private static ParserOutcome OutcomeOf(WordRowOutcome outcome) => outcome switch
    {
        WordRowOutcome.Same => ParserOutcome.Same,
        WordRowOutcome.Different => ParserOutcome.Different,
        WordRowOutcome.NoParse => ParserOutcome.NoParse,
        WordRowOutcome.Stopped => ParserOutcome.Stopped,
        _ => ParserOutcome.NotParsed,
    };

    private static MeaningTone ToneOf(WordRowTone tone) => tone switch
    {
        WordRowTone.Fine => MeaningTone.Fine,
        WordRowTone.Look => MeaningTone.Look,
        WordRowTone.Problem => MeaningTone.Problem,
        _ => MeaningTone.Neutral,
    };
}

/// <summary>One of PanGloss's morphemes in a word row, and whether FieldWorks' analysis lacks it there.</summary>
public sealed record WordRowMorphemeViewModel(ParserReadingMorphViewModel Morph, bool IsDifferent);
