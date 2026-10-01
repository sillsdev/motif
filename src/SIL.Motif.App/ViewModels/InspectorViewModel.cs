using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The inspector beside the page: one object, by identity, in the sections a linguist asks about in turn. What it
/// is, the words in the Selection that use it, the rules that ran on it, the grammar warnings that name it, and
/// what FieldWorks says about it. Each section names where its facts come from and stays short until asked for all.
/// </summary>
/// <remarks>
/// It opens when <see cref="WorkspaceContext.Inspector"/> is set and closes when that is cleared, so any page can
/// open it without the shell. The breadcrumb starts with where it opened; following a name inside it adds a crumb,
/// and stepping back or closing never changes the page.
/// </remarks>
public sealed partial class InspectorViewModel : ObservableObject
{
    /// <summary>How many rows a section shows before it offers to show them all.</summary>
    public const int ShortList = 3;

    private readonly WorkspaceContext _context;
    private readonly Func<WorkspacePage, string> _pageTitle;
    private string _openedOn = string.Empty;
    private int _generation;

    public InspectorViewModel(WorkspaceContext context, Func<WorkspacePage, string> pageTitle)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pageTitle);
        _context = context;
        _pageTitle = pageTitle;
        CloseCommand = new RelayCommand(Close);
        BackCommand = new RelayCommand(Back);
        GoToCommand = new RelayCommand<InspectorCrumbViewModel>(GoTo);
        InspectCommand = new RelayCommand<InspectorSubject>(subject => { if (subject is not null) Push(subject); });
        OpenWarningsCommand = new RelayCommand(() => _context.OpenPage(WorkspacePage.Warnings));
        ShowAllWarningsCommand = new RelayCommand(() =>
        {
            ShowsAllWarnings = true;
            OnPropertyChanged(nameof(ShowsAllWarnings));
            OnPropertyChanged(nameof(ShownWarnings));
            OnPropertyChanged(nameof(CanShowAllWarnings));
        });
        context.PropertyChanged += OnContextChanged;
    }

    /// <summary>Raised after the inspector closes, so the window can give the keyboard back to what opened it.</summary>
    public event EventHandler? Closed;

    /// <summary>Where the inspector opened, then each object followed from there, the one shown last.</summary>
    public ObservableCollection<InspectorCrumbViewModel> Crumbs { get; } = [];

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The read of the object shown, or a completed task when none is under way.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    public bool IsLoading { get; private set; }

    /// <summary>The object's name, as large as the panel shows anything.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>What kind of object it is and its gloss, in one line under the name.</summary>
    public string Subtitle { get; private set; } = string.Empty;

    /// <summary>Why the inspector can say little, such as no Parse all words yet; empty when it can say everything.</summary>
    public string Message { get; private set; } = string.Empty;

    public bool HasMessage => Message.Length > 0;

    /// <summary>Whether FieldWorks has been saved since the Baseline these facts were read from.</summary>
    public bool IsStale { get; private set; }

    /// <summary>Whether the Baseline these facts were read from still matches FieldWorks, as the top bar's mark says.</summary>
    public bool IsCurrent => !IsStale;

    /// <summary>What the freshness mark beside "From the Baseline" means, for its tip.</summary>
    public string FreshnessTip => IsStale
        ? "FieldWorks has changed since your last Refresh; these facts are from then."
        : "These facts are from your last Refresh, and FieldWorks hasn't changed since.";

    /// <summary>Whether <see cref="WhatItIs"/> was read from the Baseline, so it carries the freshness mark.</summary>
    public bool WhatItIsFromBaseline { get; private set; }

    /// <summary>The lines that say what the object is: its gloss and kind first, then where it belongs.</summary>
    public IReadOnlyList<string> WhatItIs { get; private set; } = [];

    public bool HasWhatItIs => WhatItIs.Count > 0;

    /// <summary>Where <see cref="WhatItIs"/> comes from: the Baseline, or the page that named the object.</summary>
    public string WhatItIsSource { get; private set; } = string.Empty;

    /// <summary>The Selection's words that use the object, when read.</summary>
    public InspectorWordsViewModel? Uses { get; private set; }

    /// <summary>Why the words that use it couldn't be read, such as no Parse all words yet; empty when they were.</summary>
    public string UsesNote { get; private set; } = string.Empty;

    /// <summary>Whether the section shows: it was read, or its source is missing. A subject words can't use has none.</summary>
    public bool HasUses => Uses is not null || UsesNote.Length > 0;

    public bool HasUsesNote => UsesNote.Length > 0;

    /// <summary>The words the object ran in, with its time in each, when read.</summary>
    public InspectorWordsViewModel? RanIn { get; private set; }

    public string RanInNote { get; private set; } = string.Empty;

    public bool HasRanIn => RanIn is not null || RanInNote.Length > 0;

    public bool HasRanInNote => RanInNote.Length > 0;

    /// <summary>"Rules that ran on it" for a morpheme, "Words it ran in" for a rule.</summary>
    public string RanInTitle { get; private set; } = string.Empty;

    /// <summary>The stored grammar check's findings that name the object or reach it.</summary>
    public IReadOnlyList<InspectorWarningViewModel> Warnings { get; private set; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    public bool ShowsAllWarnings { get; private set; }

    public IReadOnlyList<InspectorWarningViewModel> ShownWarnings =>
        ShowsAllWarnings ? Warnings : Warnings.Take(ShortList).ToArray();

    public bool CanShowAllWarnings => !ShowsAllWarnings && Warnings.Count > ShortList;

    public string ShowAllWarningsText => $"Show all {Warnings.Count}";

    /// <summary>What FieldWorks says about the object, row by row, each link naming its tool.</summary>
    public IReadOnlyList<InspectorFactViewModel> Facts { get; private set; } = [];

    /// <summary>Why FieldWorks' facts couldn't be read, such as two names that belong to different objects.</summary>
    public string FactsNote { get; private set; } = string.Empty;

    public bool HasFacts => Facts.Count > 0 || FactsNote.Length > 0;

    public bool HasFactsNote => FactsNote.Length > 0;

    public IRelayCommand CloseCommand { get; }

    /// <summary>Steps back one crumb, closing the inspector from the first object.</summary>
    public IRelayCommand BackCommand { get; }

    public IRelayCommand<InspectorCrumbViewModel> GoToCommand { get; }

    /// <summary>Shows another object, such as one allomorph of the entry, as the next crumb.</summary>
    public IRelayCommand<InspectorSubject> InspectCommand { get; }

    public IRelayCommand ShowAllWarningsCommand { get; }

    public IRelayCommand OpenWarningsCommand { get; }

    /// <summary>Shows <paramref name="subject"/> after the current crumb.</summary>
    public void Push(InspectorSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!IsOpen) return;
        Crumbs.Add(new InspectorCrumbViewModel(CrumbLabel(subject), subject));
        Load(subject);
    }

    /// <summary>Steps back one crumb; from the first object it closes the inspector.</summary>
    public void Back()
    {
        if (Crumbs.Count > 2)
        {
            Crumbs.RemoveAt(Crumbs.Count - 1);
            Load(Crumbs[^1].Subject!);
        }
        else Close();
    }

    public void Close() => _context.CloseInspector();

    private void GoTo(InspectorCrumbViewModel? crumb)
    {
        if (crumb is null) return;
        var index = Crumbs.IndexOf(crumb);
        if (index < 0) return;
        if (crumb.Subject is null)
        {
            Close();
            return;
        }
        while (Crumbs.Count > index + 1) Crumbs.RemoveAt(Crumbs.Count - 1);
        Load(crumb.Subject);
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceContext.Inspector)) return;
        if (_context.Inspector is { } request) Open(request);
        else if (IsOpen) Shut();
    }

    private void Open(OpenInspectorRequest request)
    {
        _openedOn = _pageTitle(_context.CurrentPage);
        Crumbs.Clear();
        Crumbs.Add(new InspectorCrumbViewModel(request.From ?? _openedOn, null));
        Crumbs.Add(new InspectorCrumbViewModel(CrumbLabel(request.Subject), request.Subject));
        IsOpen = true;
        Load(request.Subject);
    }

    private void Shut()
    {
        _generation++;
        Crumbs.Clear();
        IsOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Load(InspectorSubject subject)
    {
        var generation = ++_generation;
        Show(subject, null, string.Empty, loading: true);
        Loading = LoadAsync(subject, generation);
    }

    private async Task LoadAsync(InspectorSubject subject, int generation)
    {
        if (_context.ProjectPath is not { } projectPath)
        {
            Show(subject, null, "Open a project to see where it is used.", loading: false);
            return;
        }
        var inspected = await _context.Commands.InspectAsync(new InspectRequest(projectPath, subject),
            CancellationToken.None).ConfigureAwait(true);
        if (generation != _generation) return;
        Show(subject, inspected.Value, inspected.Value is null ? inspected.Refusal?.Message ?? "Motif couldn't read this." : string.Empty,
            loading: false);
    }

    private void Show(InspectorSubject subject, InspectResponse? response, string message, bool loading)
    {
        var facts = response?.Facts.Value;
        IsLoading = loading;
        Message = message;
        IsStale = response?.IsStale == true;
        Title = TitleOf(subject, facts);
        Subtitle = SubtitleOf(subject, facts);
        WhatItIs = [.. new[] { Subtitle }.Where(line => line.Length > 0), .. WhatItIsOf(subject, facts)];
        WhatItIsSource = facts is null ? $"From {_openedOn}" : "From the Baseline";
        WhatItIsFromBaseline = facts is not null;
        Uses = response?.Uses.Value is { } uses ? InspectorWordsViewModel.ForUses(uses, Title, _context.TryWord) : null;
        UsesNote = Note(response?.Uses);
        RanIn = response?.RanIn.Value is { } ranIn ? InspectorWordsViewModel.ForRanIn(ranIn, _context.TryWord) : null;
        RanInNote = Note(response?.RanIn);
        RanInTitle = subject.Kind == InspectorSubjectKind.Morpheme ? "Rules that ran on it" : "Words it ran in";
        Warnings = [.. (response?.Warnings.Value ?? []).Select(InspectorWarningViewModel.Of)];
        ShowsAllWarnings = false;
        Facts = facts is null ? [] : InspectorFactViewModel.Rows(facts);
        FactsNote = Note(response?.Facts);
        OnPropertyChanged(string.Empty);
    }

    // Only a missing source is worth a line; a section this kind of subject doesn't have stays out of the way.
    private static string Note<T>(InspectorSection<T>? section) =>
        section is { Status: InspectorSectionStatus.Absent, Reason: { } reason } ? reason : string.Empty;

    private static string CrumbLabel(InspectorSubject subject) => subject.Label is { Length: > 0 } label ? label : "This object";

    private static string TitleOf(InspectorSubject subject, ObjectFacts? facts) =>
        subject.Label is { Length: > 0 } label ? label
        : facts?.Rule?.Name ?? facts?.Entry?.Headword ?? "This object";

    private static string SubtitleOf(InspectorSubject subject, ObjectFacts? facts)
    {
        var parts = new List<string>();
        var gloss = subject.Gloss ?? facts?.Senses.FirstOrDefault()?.Gloss;
        if (gloss is { Length: > 0 }) parts.Add($"‘{gloss}’");
        if (KindOf(subject, facts) is { Length: > 0 } kind) parts.Add(kind);
        return string.Join(" · ", parts);
    }

    private static IReadOnlyList<string> WhatItIsOf(InspectorSubject subject, ObjectFacts? facts)
    {
        var lines = new List<string>();
        if (facts?.Entry is { } entry && entry.Headword != subject.Label)
            lines.Add($"An allomorph of {entry.Headword}");
        if (facts?.GrammaticalInfo?.Category is { } category)
            lines.Add(facts.GrammaticalInfo.Kind == "stem" ? $"A {category.Name} stem" : $"Attaches to {category.Name}");
        if (facts?.Rule is { } rule && rule.Name != subject.Label) lines.Add(rule.Name);
        return lines;
    }

    // What the object is, in FieldWorks' words for its kind.
    private static string KindOf(InspectorSubject subject, ObjectFacts? facts) =>
        facts?.GrammaticalInfo is { } info ? GrammaticalInfoKind(info.Kind)
        : facts?.Rule is { } rule ? RuleKind(rule.Kind)
        : facts?.Entry?.MorphType is { Length: > 0 } type ? type
        : subject.Kind switch
        {
            InspectorSubjectKind.Rule => subject.TimingKey?.Kind switch
            {
                "phon_rule" => "Phonological rule",
                "morph_rule" => "Morphological rule",
                _ => "Rule",
            },
            InspectorSubjectKind.Slot => "Slot",
            InspectorSubjectKind.Environment => "Environment",
            InspectorSubjectKind.Feature => "Feature",
            InspectorSubjectKind.Warning => "Grammar warning",
            _ => string.Empty,
        };

    internal static string GrammaticalInfoKind(string kind) => kind switch
    {
        "stem" => "Stem",
        "inflectionalAffix" => "Inflectional affix",
        "derivationalAffix" => "Derivational affix",
        "unclassifiedAffix" => "Unclassified affix",
        "derivationalStep" => "Derivational step",
        _ => "Grammatical info",
    };

    internal static string RuleKind(string kind) => kind switch
    {
        "phonologicalRule" => "Phonological rule",
        "compoundRule" => "Compound rule",
        "affixRule" => "Affix",
        _ => "Rule",
    };
}

/// <summary>One step of the inspector's breadcrumb: where it opened, or an object followed from there.</summary>
/// <param name="Label">What the crumb reads.</param>
/// <param name="Subject">The object, or <see langword="null"/> for where the inspector opened.</param>
public sealed record InspectorCrumbViewModel(string Label, InspectorSubject? Subject)
{
    /// <summary>Whether this crumb is where the inspector opened, which closes it.</summary>
    public bool IsOrigin => Subject is null;

    public string AutomationName => IsOrigin ? $"Back to {Label}" : Label;
}

/// <summary>Some words in an inspector section: a heading that counts them, the few shown, and the rest on request.</summary>
public sealed partial class InspectorWordsViewModel : ObservableObject
{
    private InspectorWordsViewModel(string heading, string meanings, string notCounting,
        IReadOnlyList<InspectorWordViewModel> words, string empty)
    {
        Heading = heading;
        Meanings = meanings;
        NotCountingText = notCounting;
        Words = words;
        EmptyText = empty;
        ShowAllCommand = new RelayCommand(() => ShowsAll = true);
    }

    /// <summary>The words that use an object, counted by meaning, with the words left out for a disapproved analysis.</summary>
    public static InspectorWordsViewModel ForUses(ObjectUseWords uses, string name, Action<string> tryWord)
    {
        ArgumentNullException.ThrowIfNull(uses);
        var count = uses.Words.Count;
        var heading = count == 0 ? $"No word in your Selection uses {name}"
            : count == 1 ? $"1 word uses {name}" : $"{count:N0} words use {name}";
        return new InspectorWordsViewModel(heading, CountByMeaning(uses.ByMeaning),
            uses.NotCountingDisapproved > 0 ? $"Not counting {uses.NotCountingDisapproved:N0} disapproved" : string.Empty,
            [.. uses.Words.Select(word => new InspectorWordViewModel(word.Row, string.Empty, tryWord))], string.Empty);
    }

    /// <summary>The words an object ran in, each with its time, or a line saying PanGloss recorded none.</summary>
    public static InspectorWordsViewModel ForRanIn(ObjectUseWords? ranIn, Action<string> tryWord)
    {
        var words = ranIn?.Words ?? [];
        var calls = words.Sum(word => (long)(word.Calls ?? 0));
        var heading = words.Count == 0 ? string.Empty
            : (words.Count == 1 ? "Ran in 1 word" : $"Ran in {words.Count:N0} words") +
              (calls > 0 ? $" · {calls:N0} {(calls == 1 ? "call" : "calls")}" : string.Empty);
        return new InspectorWordsViewModel(heading, CountByMeaning(ranIn?.ByMeaning ?? []), string.Empty,
            [.. words.OrderByDescending(word => word.ElapsedNs ?? 0)
                .Select(word => new InspectorWordViewModel(word.Row, TimeText(word), tryWord))],
            "PanGloss recorded no time against it in the last Parse all words.");
    }

    public string Heading { get; }

    public bool HasHeading => Heading.Length > 0;

    /// <summary>How many of the words hold each meaning, such as "Lost 3 · Same 1".</summary>
    public string Meanings { get; }

    public bool HasMeanings => Meanings.Length > 0;

    /// <summary>"Not counting 2 disapproved" when words were left out for a disapproved analysis; otherwise empty.</summary>
    public string NotCountingText { get; }

    public bool HasNotCounting => NotCountingText.Length > 0;

    public IReadOnlyList<InspectorWordViewModel> Words { get; }

    /// <summary>What the section says when it has no words.</summary>
    public string EmptyText { get; }

    public bool IsEmpty => Words.Count == 0 && EmptyText.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown))]
    [NotifyPropertyChangedFor(nameof(CanShowAll))]
    private bool _showsAll;

    public IReadOnlyList<InspectorWordViewModel> Shown =>
        ShowsAll ? Words : Words.Take(InspectorViewModel.ShortList).ToArray();

    public bool CanShowAll => !ShowsAll && Words.Count > InspectorViewModel.ShortList;

    public string ShowAllText => $"Show all {Words.Count:N0}";

    public IRelayCommand ShowAllCommand { get; }

    private static string CountByMeaning(IReadOnlyList<ObjectUseMeaning> meanings) =>
        string.Join(" · ", meanings.Select(meaning => $"{meaning.Meaning} {meaning.Words:N0}"));

    private static string TimeText(ObjectUseWord word) => word.ElapsedNs is { } ns
        ? SpeedText.PerWord(ns / 1_000_000d) : word.Calls is { } calls ? $"{calls:N0} calls" : string.Empty;
}

/// <summary>One word in an inspector section: the word, what it means for the grammar, and a way to try it.</summary>
public sealed class InspectorWordViewModel(WordRow row, string detail, Action<string> tryWord)
{
    public string Word { get; } = row.Word;

    /// <summary>The word's meaning in the Matrix's words, such as Lost.</summary>
    public string Meaning { get; } = row.Meaning;

    public WordRowTone Tone { get; } = row.Tone;

    public bool IsFine => Tone == WordRowTone.Fine;
    public bool IsLook => Tone == WordRowTone.Look;
    public bool IsProblem => Tone == WordRowTone.Problem;

    /// <summary>The object's time in this word, for the words it ran in; otherwise empty.</summary>
    public string Detail { get; } = detail;

    public bool HasDetail => Detail.Length > 0;

    public string TryWordName => $"Try {Word} in Try a Word";

    public IRelayCommand TryWordCommand { get; } = new RelayCommand(() => tryWord(row.Word));
}

/// <summary>One grammar finding that names the inspected object.</summary>
/// <param name="Title">The finding's kind, as the Warnings page names it.</param>
/// <param name="Text">What PanGloss says is wrong.</param>
public sealed record InspectorWarningViewModel(string Title, string Text)
{
    public static InspectorWarningViewModel Of(GrammarWarning finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var title = finding.Group is { Length: > 0 } group ? group : finding.CodeLabel;
        var text = finding.Description is { Length: > 0 } description ? description : finding.Text;
        return new InspectorWarningViewModel(title, text == title ? string.Empty : text);
    }

    public bool HasText => Text.Length > 0;
}

/// <summary>
/// One thing FieldWorks says about the inspected object, such as its entry or one allomorph: a label, the value,
/// the FieldWorks tool that shows it, and, for another object of the entry, a way to inspect that one.
/// </summary>
public sealed record InspectorFactViewModel(string Label, string Value)
{
    /// <summary>The tool link's text, naming the tool, such as "Lexicon Edit ↗"; empty when there is no link.</summary>
    public string LinkText { get; init; } = string.Empty;

    public Uri? Link { get; init; }

    public bool HasLink => Link is not null;

    public string LinkName { get; init; } = string.Empty;

    /// <summary>Another object to inspect from here, such as a sibling allomorph; <see langword="null"/> for none.</summary>
    public InspectorSubject? InspectSubject { get; init; }

    public bool CanInspect => InspectSubject is not null;

    public bool CannotInspect => InspectSubject is null;

    /// <summary>The rows for <paramref name="facts"/>, entry first and allomorphs last, as Lexicon Edit orders them.</summary>
    public static IReadOnlyList<InspectorFactViewModel> Rows(ObjectFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var rows = new List<InspectorFactViewModel>();
        if (facts.Entry is { } entry)
            rows.Add(With(new InspectorFactViewModel("Entry",
                entry.MorphType is { Length: > 0 } type ? $"{entry.Headword} · {type}" : entry.Headword), entry.FieldWorks, entry.Headword));
        foreach (var sense in facts.Senses)
            rows.Add(With(new InspectorFactViewModel($"Sense {sense.Number}",
                sense.Gloss ?? sense.Definition ?? "No gloss"), sense.FieldWorks, $"sense {sense.Number}"));
        if (facts.GrammaticalInfo is { } info)
        {
            var kind = InspectorViewModel.GrammaticalInfoKind(info.Kind);
            rows.Add(With(new InspectorFactViewModel("Grammatical info",
                info.Category is { } category ? $"{kind} · {category.Name}" : kind), info.Category?.FieldWorks,
                info.Category?.Name ?? kind));
            if (info.ResultCategory is { } result)
                rows.Add(With(new InspectorFactViewModel("Makes", result.Name), result.FieldWorks, result.Name));
            foreach (var slot in info.Slots)
            {
                var templates = slot.Templates.Count == 0 ? string.Empty
                    : " in " + string.Join(", ", slot.Templates.Select(template => template.Name));
                rows.Add(With(new InspectorFactViewModel("Slot",
                    $"{slot.Name}{(slot.Optional ? " (optional)" : string.Empty)}{templates}"), slot.FieldWorks, slot.Name));
            }
            if (info.RequiredFeatures is { } needs)
                rows.Add(With(new InspectorFactViewModel("Needs", needs.Notation), needs.Values.FirstOrDefault()?.FieldWorks, needs.Notation));
            if (info.AddedFeatures is { } gives)
                rows.Add(With(new InspectorFactViewModel("Gives", gives.Notation), gives.Values.FirstOrDefault()?.FieldWorks, gives.Notation));
        }
        foreach (var allomorph in facts.Allomorphs)
        {
            var where = allomorph.Environments.Count == 0 ? "no environment"
                : string.Join(", ", allomorph.Environments.Select(environment => environment.Notation));
            var needs = allomorph.RequiredFeatures is { } features ? $" · needs {features.Notation}" : string.Empty;
            rows.Add(With(new InspectorFactViewModel(allomorph.IsAsked ? "This allomorph" : "Allomorph",
                $"{allomorph.Form} · {where}{needs}")
            {
                InspectSubject = allomorph.IsAsked ? null : InspectorSubject.Morpheme(allomorph.Id, null, allomorph.Form),
            }, allomorph.Environments.FirstOrDefault()?.FieldWorks, allomorph.Form));
        }
        if (facts.Rule is { } rule)
            rows.Add(With(new InspectorFactViewModel(InspectorViewModel.RuleKind(rule.Kind), rule.Name), rule.FieldWorks, rule.Name));
        return rows;
    }

    private static InspectorFactViewModel With(InspectorFactViewModel row, TraceFieldWorksTarget? target, string name) =>
        target is null || !Uri.TryCreate(target.Link, UriKind.Absolute, out var link) ? row : row with
        {
            LinkText = $"{target.ToolName} ↗",
            Link = link,
            LinkName = string.Create(CultureInfo.CurrentCulture, $"Open {name} in {target.ToolName}"),
        };
}
