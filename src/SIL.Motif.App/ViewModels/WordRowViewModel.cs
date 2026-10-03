using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Controls;
using SIL.Motif.Commands.Queries;
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

    public WordRowViewModel(WordRow row, WordRowRoutes? routes = null) : this(row, routes, notParsedYet: false)
    {
    }

    private WordRowViewModel(WordRow row, WordRowRoutes? routes, bool notParsedYet)
    {
        ArgumentNullException.ThrowIfNull(row);
        _row = row;
        _routes = routes;
        _notParsedYet = notParsedYet;
        _isUnread = row.IsUnread;
        var standing = WordProjectStatuses.FromStanding(row.Opinion);
        // Without a parse, an unknown opinion is unread, not "Not in FieldWorks", so it shows no mark.
        var opinionUnknown = notParsedYet && row.Opinion is null;
        OpinionMark = opinionUnknown ? null : WordProjectStatuses.MarkOf(standing);
        OpinionLabel = opinionUnknown ? string.Empty : WindowWords.LabelOf(standing);
        FieldWorksMorphemes = row.FieldWorksMorphemes.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        var differing = row.DifferingPositions.ToHashSet();
        PanGlossMorphemes = row.PanGlossMorphemes.Select((morph, index) =>
            new WordRowMorphemeViewModel(new ParserReadingMorphViewModel(morph), differing.Contains(index + 1))).ToArray();
        Outcome = OutcomeOf(row.Outcome);
        Tone = WindowWords.MeaningOf(row.Opinion, Outcome).Tone;
        WordAnalysesLink = row.WordAnalysesLink is { } link ? new Uri(link) : null;
        OpenInTextCommand = new RelayCommand(() => _routes?.OpenInText?.Invoke(Word));
        TryWordCommand = new RelayCommand(() => _routes?.TryWord?.Invoke(Word));
    }

    /// <summary>
    /// The row of a word the latest parse did not reach: what FieldWorks holds for it, PanGloss's outcome as Not
    /// parsed, and the three next steps, so a page can list any word of the project the same way.
    /// </summary>
    /// <param name="word">The word form.</param>
    /// <param name="opinion">What FieldWorks holds, as a <see cref="ProjectStanding"/> value, or <see langword="null"/>.</param>
    /// <param name="fieldWorks">The morphemes of the analysis FieldWorks holds, or none.</param>
    /// <param name="places">How many places in the chosen Texts the word occurs, or <see langword="null"/>.</param>
    /// <param name="routes">Where the next steps lead.</param>
    public static WordRowViewModel NotParsed(string word, string? opinion = null,
        IReadOnlyList<ParserReadingMorph>? fieldWorks = null, int? places = null, WordRowRoutes? routes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        var morphs = fieldWorks ?? [];
        var (meaning, family) = CompareSemantics.MeaningOf(opinion, CompareColumnKind.Skipped);
        var row = new WordRow(word, WordRowOutcome.NotParsed, meaning, WordRowProjection.ToneOf(family))
        {
            MeaningCode = "not-parsed",
            Gloss = string.Join(" ", morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss)),
            Opinion = opinion,
            FieldWorksMorphemes = morphs,
            Places = places,
        };
        return new WordRowViewModel(row, routes, notParsedYet: true);
    }

    // The Word Analyses link comes with a parse, so a row built before one cannot say FieldWorks lacks the word.
    private readonly bool _notParsedYet;

    private WordRow _row;

    /// <summary>The row's facts as the projection gave them, with the read state as it is now.</summary>
    public WordRow Row => _row;

    internal WordRowViewModel WithOpinionMark(OpinionMarkKind opinion) => new(_row with
    {
        Opinion = opinion switch
        {
            OpinionMarkKind.Approved => ProjectStanding.Approved,
            OpinionMarkKind.Unknown => ProjectStanding.Candidate,
            OpinionMarkKind.Disapproved => ProjectStanding.Rejected,
            _ => _row.Opinion,
        },
    }, _routes, _notParsedYet);

    internal WordRowViewModel WithFieldWorksMorphemes(IReadOnlyList<ParserReadingMorph> morphs) => new(_row with
    {
        Gloss = string.Join(" ", morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss)),
        FieldWorksMorphemes = morphs,
    }, _routes, _notParsedYet);

    public string Word => _row.Word;

    /// <summary>The FieldWorks analysis's glosses, morph by morph; empty when FieldWorks holds none.</summary>
    public string Gloss => _row.Gloss;

    /// <summary>What FieldWorks holds, as its opinion mark; an incorrect spelling has none.</summary>
    public Mark? OpinionMark { get; }

    /// <summary>What FieldWorks holds, in its own words.</summary>
    public string OpinionLabel { get; }

    /// <summary>The opinion mark's letter box, or <see langword="null"/> for an incorrect spelling, which has none.</summary>
    public OpinionMarkKind? OpinionKind => OpinionMark?.Opinion;

    public bool HasOpinionMark => OpinionMark is not null;

    /// <summary>The morphemes of the analysis FieldWorks holds, each a way into its entry.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> FieldWorksMorphemes { get; }

    /// <summary>PanGloss's morphemes when it built something different, each marked when FieldWorks lacks it.</summary>
    public IReadOnlyList<WordRowMorphemeViewModel> PanGlossMorphemes { get; }

    public bool HasPanGlossMorphemes => PanGlossMorphemes.Count > 0;

    /// <summary>What PanGloss built.</summary>
    public ParserOutcome Outcome { get; }

    public Mark OutcomeMark => Mark.Of(Outcome);

    public string OutcomeWord => WindowWords.Of(Outcome);

    /// <summary>The compact result says only what PanGloss built; its meaning stays in the meaning column.</summary>
    public string CompactOutcomeWord => OutcomeWord;

    /// <summary>The compact result mark says only what PanGloss built.</summary>
    public Mark CompactOutcomeMark => OutcomeMark;

    /// <summary>The outcome's word beside its sign; PanGloss's own morphemes take the word's place when shown.</summary>
    public string OutcomeLabel => HasPanGlossMorphemes ? string.Empty : OutcomeWord;

    /// <summary>What the opinion and outcome mean together, using the Matrix cell's words.</summary>
    public string Meaning => WindowWords.MeaningOf(_row.Opinion, Outcome).Word;

    /// <summary>Recorded comparison detail that qualifies the cell's meaning without changing its wording.</summary>
    public string MeaningDetail => string.Join("; ", new[]
    {
        _row.MeaningDetail,
        _row.MeaningCode == "disapproved-rebuilt"
            ? "PanGloss matched an analysis FieldWorks marked Disapproved."
            : string.Empty,
    }.Where(detail => detail.Length > 0));

    /// <summary>Explains differences hidden by identical forms and glosses without guessing an entry or sense.</summary>
    public string IdentityDetail
    {
        get
        {
            var fieldWorks = _row.FieldWorksMorphemes;
            var panGloss = _row.PanGlossMorphemes;
            var sameText = fieldWorks.Select(morph => (morph.Form, morph.Gloss))
                .SequenceEqual(panGloss.Select(morph => (morph.Form, morph.Gloss)));
            var explanations = new List<string>();
            foreach (var segment in WordRowProjection.Align(fieldWorks, panGloss).Where(segment => !segment.Shared))
            {
                if (segment.FieldWorks.Count == 1 && segment.PanGloss.Count == 1)
                {
                    var stored = fieldWorks[segment.FieldWorks[0]];
                    var parsed = panGloss[segment.PanGloss[0]];
                    if (stored.Form != parsed.Form || stored.Gloss != parsed.Gloss) continue;
                    var storedForm = ObjectIdentity.Create("form", stored.AllomorphId);
                    var parsedForm = ObjectIdentity.Create("form", parsed.AllomorphId);
                    var storedInfo = ObjectIdentity.Create("msa", stored.GrammaticalInfoId);
                    var parsedInfo = ObjectIdentity.Create("msa", parsed.GrammaticalInfoId);
                    if (storedForm is null || parsedForm is null || storedInfo is null || parsedInfo is null)
                        explanations.Add($"identity not recorded: {parsed.Form} ‘{parsed.Gloss}’");
                    else if (!ObjectIdentity.Same(storedForm, parsedForm) || !ObjectIdentity.Same(storedInfo, parsedInfo))
                        explanations.Add($"different morpheme identity: {parsed.Form} ‘{parsed.Gloss}’");
                }
                else if (sameText)
                {
                    var morphs = segment.FieldWorks.Select(index => fieldWorks[index])
                        .Concat(segment.PanGloss.Select(index => panGloss[index])).ToArray();
                    var recorded = morphs.All(morph => ObjectIdentity.Create("form", morph.AllomorphId) is not null &&
                        ObjectIdentity.Create("msa", morph.GrammaticalInfoId) is not null);
                    var reason = recorded ? "morpheme sequence differs" : "identity not recorded";
                    explanations.Add(reason + ": " + string.Join(", ",
                        morphs.Select(morph => $"{morph.Form} ‘{morph.Gloss}’").Distinct()));
                }
            }
            return explanations.Count == 0 ? string.Empty : "Same form and gloss; " +
                string.Join("; ", explanations.Distinct());
        }
    }

    public bool HasIdentityDetail => IdentityDetail.Length > 0;

    public bool HasMeaningDetail => MeaningDetail.Length > 0;

    public MeaningTone Tone { get; }

    public Mark MeaningMark => Mark.Of(Tone);

    /// <summary>How many grammar warnings name something the word uses, or <see langword="null"/> when not known.</summary>
    public int? WarningsNamed => _row.WarningsNamed;

    public bool HasWarningsNamed => WarningsNamed is > 0;

    /// <summary>The warning mark beside the count of warnings that name something the word uses.</summary>
    public Mark WarningMark => Mark.Warning;

    public string WarningsText =>
        WarningsNamed is { } count and > 0 ? count.ToString(CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>How many places the word occurs, such as <c>×3</c>, or empty when not known.</summary>
    public string PlacesText => _row.Places is { } places ? $"×{places}" : string.Empty;

    /// <summary>The place count in the window's words, or empty when it is not known.</summary>
    public string PlacesTooltip => _row.Places is { } places
        ? places == 1 ? "1 place" : $"{places:N0} places"
        : string.Empty;

    /// <summary>How long the parser took, or empty for a word it did not time.</summary>
    public string ElapsedText => _row.ElapsedMs is not { } ms ? string.Empty : ms == 0 ? "<1 ms" : $"{ms:N0} ms";

    /// <summary>When this word's producing measurement was recorded, independent of its neighbours.</summary>
    public string MeasuredText => _row.Origin is { } origin
        ? $"Measured {origin.MeasuredUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}" : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnread))]
    [NotifyPropertyChangedFor(nameof(Row))]
    private bool? _isUnread;

    partial void OnIsUnreadChanged(bool? value) => _row = _row with { IsUnread = value };

    /// <summary>Whether to show the Unread mark; a read word, or one whose read state is unknown, shows nothing.</summary>
    public bool ShowUnread => IsUnread == true;

    public string UnreadText => "Unread";

    /// <summary>The stable automation id of one part of this word's row in <paramref name="list"/>.</summary>
    /// <param name="list">The list the row sits in, such as <c>matrix</c>, <c>fix-first</c> or <c>lists</c>.</param>
    /// <param name="part">One of row, tick, open-in-text, try-a-word, word-analyses or card.</param>
    public string AutomationIdOf(string list, string part) => AutomationIds.ForWordRowPart(list, Word, part);

    /// <summary>The one-line summary a hover or focus shows, in window words.</summary>
    public string Summary => string.Join(" · ",
        new[] { Word, OpinionLabel, $"PanGloss: {OutcomeWord}", Meaning }.Where(part => part.Length > 0));

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

    /// <summary>The Word Analyses step's tip: where it opens, or why it cannot.</summary>
    public string WordAnalysesTip => HasWordAnalysesLink ? WordAnalysesName : WordAnalysesDisabledReason;

    /// <summary>Why the Word Analyses step cannot open, or empty when it can.</summary>
    public string WordAnalysesDisabledReason => HasWordAnalysesLink ? string.Empty
        : _notParsedYet ? $"Parse all words to link {Word} to Word Analyses"
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
