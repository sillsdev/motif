using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Help;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Preferences;

namespace SIL.Motif.App.ViewModels;

internal sealed record ShortcutEntry(string Group, string Keys, string Action, string SearchText)
{
    /// <summary>The action with the page or context in which its gesture works.</summary>
    public string ScopedAction => $"{Group} · {Action}";
}

internal enum SettingsGroup
{
    Display,
    KeyboardShortcuts,
    Parsing,
    ProposalChecks,
    WritingSystems,
    HelpAndReports,
    About,
    Texts,
}

internal enum WindowThemeChoice
{
    System,
    Light,
    Dark,
}

internal sealed partial class SettingsGroupOption : ObservableObject
{
    public SettingsGroupOption(SettingsGroup group, string title)
    {
        Group = group;
        Title = title;
    }

    public SettingsGroup Group { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _hasWarning;
}

/// <summary>Stable preference keys for the Settings disclosures; never localized text.</summary>
internal static class SettingsExpanderKeys
{
    public const string ShortcutList = "Settings.ShortcutList";
    public const string ReportIncludes = "Settings.ReportIncludes";
    public const string VernacularDetails = "Settings.VernacularDetails";
    public const string AnalysisDetails = "Settings.AnalysisDetails";
    public const string ShowHandoff = "Settings.ShowHandoff";
}

internal sealed class WritingSystemSettingsRow : ObservableObject
{
    public WritingSystemSettingsRow(WritingSystemDisplay writingSystem, TextStyles resolver,
        bool isAlsoInVernacular = false, bool detailsExpanded = false, Action<bool>? onDetailsExpanded = null)
    {
        WritingSystem = writingSystem;
        _detailsExpanded = detailsExpanded;
        _onDetailsExpanded = onDetailsExpanded;
        var normal = resolver.Resolve(TextStyleRequest.Linguistic(writingSystem.Id));
        FontSourceText = !normal.RequestedFontInstalled ? MotifAppStrings.FontSourceFallback :
            resolver.RequestedFontIsBundled(normal.RequestedFontFamily)
                ? MotifAppStrings.FontSourceBundled : MotifAppStrings.FontSourceInstalled;
        IsAlsoInVernacular = isAlsoInVernacular;
        var missingFonts = isAlsoInVernacular ? [] : writingSystem.StyleFonts
            .Select(pair => (pair.Key, pair.Value.FontFamily))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.FontFamily) &&
                !resolver.Resolve(TextStyleRequest.Linguistic(writingSystem.Id, pair.Key)).RequestedFontInstalled)
            .Select(pair => pair.FontFamily)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        MissingFontFamilies = missingFonts;
    }

    private readonly Action<bool>? _onDetailsExpanded;
    private bool _detailsExpanded;

    /// <summary>Whether the Details disclosure is open; every change is reported so it can be remembered.</summary>
    public bool DetailsExpanded
    {
        get => _detailsExpanded;
        set
        {
            if (_detailsExpanded == value) return;
            _detailsExpanded = value;
            OnPropertyChanged();
            _onDetailsExpanded?.Invoke(value);
        }
    }

    public WritingSystemDisplay WritingSystem { get; }
    public string Id => WritingSystem.Id;
    public string Name => string.IsNullOrWhiteSpace(WritingSystem.Name) ? WritingSystem.Id : WritingSystem.Name;
    public string DetailsAccessibleName => string.Format(MotifAppStrings.WritingSystemDetailsAccessibleNameFormat, Name);
    public string Abbreviation => WritingSystem.Abbreviation;
    public bool HasAbbreviation => !string.IsNullOrWhiteSpace(Abbreviation);
    public bool HasTag => !string.IsNullOrWhiteSpace(Id) && !string.Equals(Id, Abbreviation, StringComparison.Ordinal);
    public bool IsDefault => WritingSystem.IsDefault;
    public bool ShowDefault => IsDefault && !IsAlsoInVernacular;
    public bool IsAlsoInVernacular { get; }
    public string AlsoInVernacularText => MotifAppStrings.AlsoInVernacular;
    public string FontFamily => ResolveStyleFont("Normal").FontFamily;
    public string FontSourceText { get; }
    public string FontLabel => string.IsNullOrWhiteSpace(FontFamily) ? MotifAppStrings.FontNoneSet : FontFamily;
    public string Features => string.IsNullOrWhiteSpace(ResolveStyleFont("Normal").FontFeatures)
        ? MotifAppStrings.FontFeaturesNone : ResolveStyleFont("Normal").FontFeatures;
    public string Direction => WritingSystem.RightToLeft ? MotifAppStrings.DirectionRightToLeft : MotifAppStrings.DirectionLeftToRight;
    public string StyleSizes => string.Join(" · ", WritingSystem.StyleSizes
        .OrderBy(pair => string.Equals(pair.Key, "Normal", StringComparison.Ordinal) ? 0 : 1)
        .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
        .Select(pair => string.Format(MotifAppStrings.StyleSizeFormat,
            pair.Value.ToString("0.##", CultureInfo.InvariantCulture), pair.Key)));
    public bool HasStyleSizes => !string.IsNullOrEmpty(StyleSizes);
    public IReadOnlyList<string> MissingFontFamilies { get; }
    public bool IsFontMissing => MissingFontFamilies.Count > 0;
    public string MissingFontCaption => MissingFontFamilies.Count == 0 ? string.Empty :
        string.Format(MotifAppStrings.MissingFontCaptionFormat,
            string.Join(" · ", MissingFontFamilies));
    public TextAlignment PreviewTextAlignment => WritingSystem.RightToLeft ? TextAlignment.Right : TextAlignment.Left;

    private WritingSystemStyleFont ResolveStyleFont(string styleName) =>
        WritingSystem.StyleFonts.GetValueOrDefault(styleName) ??
        new WritingSystemStyleFont(WritingSystem.FontFamily, WritingSystem.FontFeatures);
}

internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUserPreferencesStore _preferences;
    private readonly IDefaultProposalChecksStore _defaultChecks;
    private StoredAnalysisComparison _storedAnalyses;
    private readonly WorkspaceShellViewModel _workspace;
    private readonly ApplicationFacts _applicationFacts;
    private readonly ShortcutEntry[] _shortcutCatalog;
    private readonly ProblemReport _windowReport;
    private readonly bool _panGlossExecutableFound;
    private readonly OverviewPageModel _overview;
    private string? _writingSystemTargetId;
    private SetupViewModel? _observedSetup;
    private bool _updatingEstimate;
    private decimal? _lastGoodAssessBudgetSeconds = SelectionParsingLimits.DefaultAssessBudgetSeconds;
    private decimal? _assessBudgetSeconds = SelectionParsingLimits.DefaultAssessBudgetSeconds;
    private decimal? _lastGoodParserWorkLimit;
    private decimal? _lastGoodTimeLimitSeconds;
    private decimal? _parserWorkLimitBeforeNoStep;
    private decimal? _timeLimitBeforeNoStep;
    private bool _lastGoodNoParserWorkLimit;
    private string _parserWorkLimitRefusal = string.Empty;
    private bool _keepsParserWorkLimit;
    private decimal? _keptParserWorkLimit;
    private bool _keepsTimeLimit;
    private bool _keepsNoParserWorkLimit;
    private bool _keptNoParserWorkLimit;
    private decimal? _keptTimeLimit;
    private bool _suppressPreferenceWrites;

    public SettingsViewModel(WorkspaceShellViewModel workspace, IUserPreferencesStore? preferences = null,
        ApplicationFacts? applicationFacts = null)
    {
        _workspace = workspace;
        _overview = workspace.PageModel<OverviewPageModel>();
        _preferences = preferences ?? new MemoryUserPreferencesStore();
        _defaultChecks = workspace.Context.DefaultProposalChecks;
        _storedAnalyses = _defaultChecks.StoredAnalyses;
        _applicationFacts = applicationFacts ?? ApplicationFacts.Current;
        var culture = CultureInfo.CurrentUICulture;
        _shortcutCatalog = KeyboardShortcutRegistry.Entries.Select(entry => new ShortcutEntry(
            entry.Scope, entry.Gesture, entry.Action,
            entry.Gesture + " " + string.Join(" ", KeyboardShortcutRegistry.Bindings
                .Where(binding => binding.Row == entry)
                .Select(binding => binding.Gesture.Key + " " +
                    KeyboardShortcutCatalog.FormatKeyName(binding.Gesture.Key, culture)))))
            .ToArray();
        _windowReport = ProblemReport.ForWindowAction(_applicationFacts);
        _panGlossExecutableFound = _applicationFacts.ParserAvailable;
        var saved = _preferences.Current;
        _zoomPercent = saved.ZoomPercent;
        _themeChoice = ToWindowTheme(saved.Theme);
        PreferenceProblemText = _preferences.Problem?.Message ?? string.Empty;
        ZoomInCommand = new RelayCommand(ZoomIn, () => ZoomPercent < ZoomPolicy.Percentages[^1]);
        ZoomOutCommand = new RelayCommand(ZoomOut, () => ZoomPercent > ZoomPolicy.Percentages[0]);
        ResetZoomCommand = new RelayCommand(() => ZoomPercent = 100, () => ZoomPercent != 100);
        ResetThemeCommand = new RelayCommand(() => ThemeChoice = WindowThemeChoice.System, () => CanResetTheme);
        ResetStoredAnalysesCommand = new RelayCommand(() => ChooseStoredAnalyses(StoredAnalysisComparison.Budget),
            () => CanResetStoredAnalyses);
        ResetParserWorkLimitCommand = new AsyncRelayCommand(ResetParserWorkLimitAsync, () => CanResetParserWorkLimit);
        ResetPreferencesCommand = new RelayCommand(ResetPreferences, () => _preferences.State == PreferenceState.Refused);
        UseAllTextsCommand = new AsyncRelayCommand(UseAllTextsCommandAsync,
            () => CanUseAllTexts);
        SelectGroupCommand = new RelayCommand<SettingsGroup>(group => SelectGroup(group));
        ChooseStoredAnalysesCommand = new RelayCommand<StoredAnalysisComparison>(ChooseStoredAnalyses);
        ClearShortcutSearchCommand = new RelayCommand(() => ShortcutSearch = string.Empty,
            () => !string.IsNullOrEmpty(ShortcutSearch));
        SelectThemeCommand = new RelayCommand<WindowThemeChoice>(choice => ThemeChoice = choice);
        PageGroupOptions = new ObservableCollection<SettingsGroupOption>([
            new(SettingsGroup.Display, MotifAppStrings.Display),
            new(SettingsGroup.KeyboardShortcuts, MotifAppStrings.KeyboardShortcuts),
            new(SettingsGroup.Parsing, MotifAppStrings.Parsing),
            new(SettingsGroup.ProposalChecks, MotifAppStrings.DefaultProposalChecks),
            new(SettingsGroup.WritingSystems, MotifAppStrings.WritingSystems),
            new(SettingsGroup.Texts, MotifAppStrings.Texts),
            new(SettingsGroup.HelpAndReports, MotifAppStrings.HelpAndProblemReports),
            new(SettingsGroup.About, MotifAppStrings.AboutMotif),
        ]);
        _selectedGroup = MapSettingsGroup(saved.LastSettingsGroup);
        _shortcutListShown = Remembered(SettingsExpanderKeys.ShortcutList);
        _reportIncludesExpanded = Remembered(SettingsExpanderKeys.ReportIncludes);
        _showHandoffByCopyingFiles = Remembered(SettingsExpanderKeys.ShowHandoff);
        RefreshNavigationOptions(SelectedGroup);
        ApplyTheme();
        _preferences.Changed += OnPreferencesChanged;
        _workspace.Selection.PropertyChanged += OnSelectionChanged;
        _overview.PropertyChanged += OnOverviewChanged;
        _workspace.Context.Assess.PropertyChanged += OnAssessChanged;
        _workspace.Context.PropertyChanged += OnContextChanged;
        ObserveSetup(_workspace.Context.Setup);
        RefreshWritingSystems();
        RefreshShortcutList();
        IsUsingEstimate = _observedSetup?.CurrentSelectionLimits.TimeMode != SelectionTimeLimitMode.Explicit;
        RememberGoodValues();
    }

    public IRelayCommand ZoomInCommand { get; }
    public IRelayCommand ZoomOutCommand { get; }
    public IRelayCommand ResetZoomCommand { get; }
    public IRelayCommand ResetThemeCommand { get; }
    public IRelayCommand ResetStoredAnalysesCommand { get; }
    public IAsyncRelayCommand ResetParserWorkLimitCommand { get; }
    public bool CanResetTheme => ThemeChoice != WindowThemeChoice.System;
    public bool CanResetStoredAnalyses => !IsStoredAnalysesBudget;
    public bool CanResetParserWorkLimit => HasSavedSelection && (IsNoParserWorkLimit || ParserWorkLimitValue != ParserWorkLimit.DefaultWork);

    public IRelayCommand ResetPreferencesCommand { get; }
    public IAsyncRelayCommand UseAllTextsCommand { get; }
    public IRelayCommand<SettingsGroup> SelectGroupCommand { get; }
    public IRelayCommand<WindowThemeChoice> SelectThemeCommand { get; }
    public IRelayCommand ClearShortcutSearchCommand { get; }
    public ObservableCollection<SettingsGroupOption> PageGroupOptions { get; }
    public ObservableCollection<WritingSystemSettingsRow> VernacularWritingSystems { get; } = [];
    public ObservableCollection<WritingSystemSettingsRow> AnalysisWritingSystems { get; } = [];
    public TextStyles WritingSystemTextStyles => _workspace.Context.TextStyles;
    public ObservableCollection<ShortcutEntry> ShortcutEntries { get; } = [];
    public bool ShortcutRegistryAvailable => true;
    public string PreferenceProblemText { get; private set; } = string.Empty;
    public bool HasPreferenceProblem => !string.IsNullOrEmpty(PreferenceProblemText);
    public bool ShowResetPreferences => _preferences.State == PreferenceState.Refused;
    public string ThemeSystemLabel => OperatingSystem.IsWindows() ? MotifAppStrings.MatchWindows
        : OperatingSystem.IsMacOS() ? MotifAppStrings.MatchMacOS : MotifAppStrings.MatchSystem;
    public string HelpCaption => MotifAppStrings.HelpCaption;
    public string ReportCaption => MotifAppStrings.ReportCaption;
    public string ZoomShortcutHint => string.Join(" · ", _shortcutCatalog
        .Where(entry => entry.Action is "Zoom in" or "Zoom out" or "Reset zoom")
        .Select(entry => $"{entry.Action}: {entry.Keys}"));
    public string MotifVersion => _windowReport.MotifVersion;
    public string PanGlossVersion => _windowReport.PanGlossVersion;
    public string OperatingSystemText => _windowReport.OperatingSystem;
    public string DataRoot => _applicationFacts.ManagedDataRoot;
    public string DataRootDisplay => PathDisplay.Wrappable(DataRoot);
    public string DataFolderActionText => OperatingSystem.IsWindows() ? MotifAppStrings.ShowInExplorer
        : OperatingSystem.IsMacOS() ? MotifAppStrings.ShowInFinder : MotifAppStrings.OpenFolder;
    public string ParsingLead => string.Format(MotifAppStrings.ParsingLeadFormat, ProjectName);
    public string ProjectName => !string.IsNullOrWhiteSpace(_overview.Overview?.ProjectName)
        ? _overview.Overview.ProjectName
        : !string.IsNullOrWhiteSpace(_workspace.Context.Setup?.ProjectName)
            ? _workspace.Context.Setup.ProjectName
            : Path.GetFileNameWithoutExtension(_workspace.Context.ProjectPath ?? _workspace.ProjectName);
    public bool HasProject => _workspace.HasProject;
    public bool HasSavedSelection => _workspace.Context.Setup?.CanRunDefaultSelection == true;
    public bool HasProjectWithoutSelection => HasProject && !HasSavedSelection;
    public string TextSelectionSummary =>
        string.Format(MotifAppStrings.TextSelectionSummaryFormat,
            _workspace.Selection.ChosenTextIds.Count,
            PluralText.Format(MotifAppStrings.TextsSelectedPattern, _workspace.Selection.TextCount));
    public bool CanUseAllTexts => HasSavedSelection &&
        _workspace.Context.Setup?.SavedSelectionContainsAllTexts != true;
    public string TextSelectionRefusal { get; private set; } = string.Empty;
    public bool HasTextSelectionRefusal => !string.IsNullOrEmpty(TextSelectionRefusal);
    public bool PanGlossNotFound => !_panGlossExecutableFound;
    public bool ShortcutSearchHasText => !string.IsNullOrWhiteSpace(ShortcutSearch);
    public bool HasShortcutMatches => ShortcutEntries.Count > 0;
    public bool ShowNoShortcutMatches => ShortcutRegistryAvailable && ShortcutSearchHasText && !HasShortcutMatches;
    public string NoShortcutMatchesText => string.Format(MotifAppStrings.NoShortcutMatchesFormat, ShortcutSearch);
    public bool IsDisplaySelected => SelectedGroup == SettingsGroup.Display;
    public bool IsKeyboardShortcutsSelected => SelectedGroup == SettingsGroup.KeyboardShortcuts;
    public bool IsParsingSelected => SelectedGroup == SettingsGroup.Parsing;
    public bool IsProposalChecksSelected => SelectedGroup == SettingsGroup.ProposalChecks;
    public string ProposalChecksLead => MotifAppStrings.ProposalChecksLead;
    public bool IsStoredAnalysesBudget => _storedAnalyses == StoredAnalysisComparison.Budget;
    public bool IsStoredAnalysesAll => _storedAnalyses == StoredAnalysisComparison.All;
    public bool IsStoredAnalysesOff => _storedAnalyses == StoredAnalysisComparison.Off;
    public IRelayCommand<StoredAnalysisComparison> ChooseStoredAnalysesCommand { get; }
    public string StoredAnalysesRefusal { get; private set; } = string.Empty;
    public bool HasStoredAnalysesRefusal => !string.IsNullOrEmpty(StoredAnalysesRefusal);
    public bool IsWritingSystemsSelected => SelectedGroup == SettingsGroup.WritingSystems;
    public bool IsHelpAndReportsSelected => SelectedGroup == SettingsGroup.HelpAndReports;
    public bool IsAboutSelected => SelectedGroup == SettingsGroup.About;
    public string GroupParsingLabel => HasProject ? string.Format(MotifAppStrings.ParsingForProjectFormat, ProjectName) : MotifAppStrings.Parsing;
    public string WritingSystemsHeading => HasProject ? string.Format(MotifAppStrings.WritingSystemsInProjectFormat, ProjectName) : MotifAppStrings.WritingSystems;
    public string WritingSystemsLead => string.Format(MotifAppStrings.WritingSystemsLeadFormat, ProjectName);
    public bool HasWritingSystems => VernacularWritingSystems.Count + AnalysisWritingSystems.Count > 0;
    public bool HasVernacularWritingSystems => VernacularWritingSystems.Count > 0;
    public bool HasAnalysisWritingSystems => AnalysisWritingSystems.Count > 0;
    public bool ShowWritingSystemsEmptyState => HasProject && !HasWritingSystems;
    public string WritingSystemsEmptyText => _overview.Overview is null
        ? MotifAppStrings.WritingSystemsEmptyRefresh
        : MotifAppStrings.WritingSystemsEmptyBaseline;
    public bool HasMissingWritingSystemFont => VernacularWritingSystems.Concat(AnalysisWritingSystems)
        .Any(row => row.IsFontMissing);
    public bool ShowSettingsFooter => IsParsingSelected || IsWritingSystemsSelected && HasProject;
    public string ParserWorkLimitRefusal => _parserWorkLimitRefusal.Length > 0
        ? _parserWorkLimitRefusal : _workspace.Selection.ParserWorkLimitValidationMessage ?? string.Empty;
    public string TimeLimitRefusal { get => _timeLimitRefusal; private set => SetProperty(ref _timeLimitRefusal, value); }
    private string _timeLimitRefusal = string.Empty;
    public bool HasParserWorkLimitRefusal => !string.IsNullOrEmpty(ParserWorkLimitRefusal);
    public bool HasTimeLimitRefusal => !string.IsNullOrEmpty(TimeLimitRefusal);
    public string AssessBudgetRefusal { get => _assessBudgetRefusal; private set => SetProperty(ref _assessBudgetRefusal, value); }
    private string _assessBudgetRefusal = string.Empty;
    public bool HasAssessBudgetRefusal => !string.IsNullOrEmpty(AssessBudgetRefusal);
    public decimal? AssessBudgetSeconds
    {
        get => _assessBudgetSeconds;
        set
        {
            if (SetProperty(ref _assessBudgetSeconds, value))
                OnPropertyChanged(nameof(HasUncommittedParsingLimitChanges));
        }
    }
    public bool HasUncommittedParsingLimitChanges =>
        AssessBudgetSeconds != _lastGoodAssessBudgetSeconds ||
        IsNoParserWorkLimit != _lastGoodNoParserWorkLimit || ParserWorkLimitValue != _lastGoodParserWorkLimit ||
        TimeLimitSeconds != _lastGoodTimeLimitSeconds;
    public string ParserWorkLimitEstimateText => _workspace.Context.Setup?.ParserWorkLimitEstimateText ??
        string.Empty;
    public bool IsTimeLimitEnabled => HasProject && HasSavedSelection && !IsNoParserWorkLimit;
    public bool IsNoParserWorkLimit
    {
        get => _keepsNoParserWorkLimit ? _keptNoParserWorkLimit : _workspace.Selection.PerWordParserWorkLimitUnbounded;
        set
        {
            var hadKeptValue = _keepsNoParserWorkLimit;
            _keepsNoParserWorkLimit = false;
            if (_workspace.Selection.PerWordParserWorkLimitUnbounded == value)
            {
                if (hadKeptValue) ReleaseHeldToggle();
                return;
            }
            if (value && ShowsRefusedLimit)
            {
                _parserWorkLimitBeforeNoStep = ParserWorkLimitValue;
                _timeLimitBeforeNoStep = ExplicitTimeLimitToRestore();
                _keptNoParserWorkLimit = true;
                _keepsNoParserWorkLimit = true;
                NotifyKeptLimitsChanged();
                return;
            }
            if (value)
            {
                _parserWorkLimitBeforeNoStep = ParserWorkLimitValue ?? _lastGoodParserWorkLimit ??
                    _workspace.Context.Setup?.ParserWorkLimitValue ?? ParserWorkLimit.DefaultWork;
                _timeLimitBeforeNoStep = ExplicitTimeLimitToRestore();
            }
            _workspace.Selection.PerWordParserWorkLimitUnbounded = value;
            if (value)
            {
                _workspace.Selection.PerWordTimeLimitSeconds = null;
                IsUsingEstimate = true;
            }
            else
            {
                var timeLimitToRestore = _timeLimitBeforeNoStep;
                var stepToRestore = _parserWorkLimitBeforeNoStep;
                _workspace.Selection.PerWordParserWorkLimit = (ParserWorkLimitIsAcceptable(stepToRestore) ? stepToRestore : null) ??
                    _lastGoodParserWorkLimit ?? _workspace.Context.Setup?.ParserWorkLimitValue ?? ParserWorkLimit.DefaultWork;
                if (stepToRestore is not null && !ParserWorkLimitIsAcceptable(stepToRestore)) ParserWorkLimitValue = stepToRestore;
                _timeLimitBeforeNoStep = null;
                _parserWorkLimitBeforeNoStep = null;
                if (timeLimitToRestore is > 0)
                {
                    TimeLimitSeconds = timeLimitToRestore;
                    IsUsingEstimate = false;
                }
                else
                {
                    IsUsingEstimate = true;
                    ApplyEstimate();
                }
            }
            OnPropertyChanged(nameof(IsTimeLimitEnabled));
            OnPropertyChanged(nameof(ShowUseEstimate));
        }
    }
    public decimal? ParserWorkLimitValue
    {
        get => _keepsParserWorkLimit && !HidesRefusedWhileTicked(_keptParserWorkLimit, ParserWorkLimitIsAcceptable) ? _keptParserWorkLimit
            : IsNoParserWorkLimit
            ? (ParserWorkLimitIsAcceptable(_parserWorkLimitBeforeNoStep) ? _parserWorkLimitBeforeNoStep : null) ??
              _lastGoodParserWorkLimit ?? _workspace.Context.Setup?.ParserWorkLimitValue
            : _workspace.Selection.PerWordParserWorkLimit;
        set
        {
            if (WritesBackShownValueUnderHeldTick(value, ParserWorkLimitValue)) return;
            var hadKeptValue = _keepsParserWorkLimit;
            var wasRefused = hadKeptValue && !ParserWorkLimitIsAcceptable(_keptParserWorkLimit);
            _keepsParserWorkLimit = false;
            if (!ParserWorkLimitIsAcceptable(value))
            {
                _keptParserWorkLimit = value;
                _keepsParserWorkLimit = true;
                _parserWorkLimitRefusal = MotifAppStrings.ParserWorkLimitWholeNumberRefusal;
                KeepLimits(IsNoParserWorkLimit, value, TimeLimitSeconds);
                return;
            }
            if (_keepsTimeLimit && !TimeLimitIsAcceptable(_keptTimeLimit))
            {
                _parserWorkLimitRefusal = string.Empty;
                KeepLimits(IsNoParserWorkLimit, value, TimeLimitSeconds);
                return;
            }
            if (hadKeptValue)
            {
                _parserWorkLimitRefusal = string.Empty;
                NotifyKeptLimitsChanged();
            }
            if (_workspace.Selection.PerWordParserWorkLimit != value)
            {
                _workspace.Selection.PerWordParserWorkLimit = value;
                if (IsUsingEstimate) ApplyEstimate();
            }
            if (wasRefused && _keepsTimeLimit && !_updatingEstimate)
            {
                var held = _keptTimeLimit;
                _keepsTimeLimit = false;
                TimeLimitSeconds = held;
            }
            ReleaseHeldUntick();
        }
    }
    public decimal? TimeLimitSeconds
    {
        get => _keepsTimeLimit && !HidesRefusedWhileTicked(_keptTimeLimit, TimeLimitIsAcceptable) ? _keptTimeLimit
            : _workspace.Selection.PerWordTimeLimitSeconds;
        set
        {
            if (WritesBackShownValueUnderHeldTick(value, TimeLimitSeconds)) return;
            var hadKeptValue = _keepsTimeLimit;
            var wasRefused = hadKeptValue && !TimeLimitIsAcceptable(_keptTimeLimit);
            _keepsTimeLimit = false;
            if (!_updatingEstimate && !IsNoParserWorkLimit && !TimeLimitIsAcceptable(value))
            {
                _keptTimeLimit = value;
                _keepsTimeLimit = true;
                IsUsingEstimate = false;
                TimeLimitRefusal = value is null or <= 0
                    ? MotifAppStrings.TimeLimitPositiveRefusal : MotifAppStrings.TimeLimitRangeRefusal;
                KeepLimits(IsNoParserWorkLimit, ParserWorkLimitValue, value);
                return;
            }
            if (!_updatingEstimate && _keepsParserWorkLimit && !ParserWorkLimitIsAcceptable(_keptParserWorkLimit))
            {
                _keptTimeLimit = value;
                _keepsTimeLimit = _workspace.Selection.PerWordTimeLimitSeconds != value || EstimateChosenOverExplicit;
                TimeLimitRefusal = string.Empty;
                if (_keepsTimeLimit) IsUsingEstimate = false;
                NotifyKeptLimitsChanged();
                return;
            }
            if (hadKeptValue)
            {
                TimeLimitRefusal = string.Empty;
                NotifyKeptLimitsChanged();
            }
            if (_workspace.Selection.PerWordTimeLimitSeconds != value)
            {
                _workspace.Selection.PerWordTimeLimitSeconds = value;
                if (!IsNoParserWorkLimit) IsUsingEstimate = false;
                TimeLimitRefusal = !IsNoParserWorkLimit && (value is null or <= 0)
                    ? MotifAppStrings.TimeLimitPositiveRefusal : string.Empty;
                OnPropertyChanged(nameof(HasTimeLimitRefusal));
            }
            if (wasRefused && _keepsParserWorkLimit && !_updatingEstimate)
            {
                var held = _keptParserWorkLimit;
                _keepsParserWorkLimit = false;
                ParserWorkLimitValue = held;
            }
            ReleaseHeldUntick();
        }
    }
    public bool IsUsingMotifEstimate => IsUsingEstimate;

    // Estimate mode has no typed time to bring back; restoring the estimate as typed would save it as Explicit.
    private decimal? ExplicitTimeLimitToRestore() => IsUsingEstimate ? null : _workspace.Selection.PerWordTimeLimitSeconds;

    // A ticked field is disabled, so a refused number stays out of it until the tick goes.
    private bool HidesRefusedWhileTicked(decimal? held, Func<decimal?, bool> isAcceptable) => IsNoParserWorkLimit && !isAcceptable(held);

    private static bool ParserWorkLimitIsAcceptable(decimal? steps) =>
        steps is > 0 && steps <= long.MaxValue && decimal.Truncate(steps.Value) == steps;

    private static bool TimeLimitIsAcceptable(decimal? seconds) =>
        seconds is > 0 && seconds <= int.MaxValue / 1000m && decimal.Round(seconds.Value * 1000m) >= 1m;

    // The estimate is an unsaved choice only where the save holds an Explicit time.
    private bool EstimateChosenOverExplicit =>
        IsUsingEstimate && _workspace.Context.Setup?.CurrentSelectionLimits.TimeMode == SelectionTimeLimitMode.Explicit;

    private bool ShowsRefusedLimit =>
        _keepsParserWorkLimit && !ParserWorkLimitIsAcceptable(_keptParserWorkLimit) ||
        _keepsTimeLimit && !TimeLimitIsAcceptable(_keptTimeLimit);

    // The Selection never held the tick, so its side effects are undone from the saved state.
    private void ReleaseHeldToggle()
    {
        KeepLimits(IsNoParserWorkLimit, ParserWorkLimitValue, TimeLimitSeconds);
        if (!_keepsParserWorkLimit) _parserWorkLimitRefusal = string.Empty;
        if (!_keepsTimeLimit) TimeLimitRefusal = string.Empty;
        NotifyKeptLimitsChanged();
    }

    // A control echoing the shown number into a disabled field must not drop the typed number held behind it.
    private bool WritesBackShownValueUnderHeldTick(decimal? value, decimal? shown) =>
        _keepsNoParserWorkLimit && _keptNoParserWorkLimit && value == shown;

    // A held untick waits only on a refused value, so correcting the last one releases it.
    private void ReleaseHeldUntick()
    {
        if (_updatingEstimate || !_keepsNoParserWorkLimit || _keptNoParserWorkLimit || ShowsRefusedLimit) return;
        _keepsNoParserWorkLimit = false;
        _workspace.Selection.PerWordParserWorkLimitUnbounded = false;
        if (_keepsParserWorkLimit) _workspace.Selection.PerWordParserWorkLimit = _keptParserWorkLimit;
        if (_keepsTimeLimit) _workspace.Selection.PerWordTimeLimitSeconds = _keptTimeLimit;
        _keepsParserWorkLimit = false;
        _keepsTimeLimit = false;
        if (IsUsingEstimate) ApplyEstimate();
        NotifyKeptLimitsChanged();
    }

    // A refused value stays in its field; the Selection keeps the last accepted one.
    private void NotifyKeptLimitsChanged()
    {
        OnPropertyChanged(nameof(ParserWorkLimitValue));
        OnPropertyChanged(nameof(TimeLimitSeconds));
        OnPropertyChanged(nameof(IsNoParserWorkLimit));
        OnPropertyChanged(nameof(IsTimeLimitEnabled));
        OnPropertyChanged(nameof(ParserWorkLimitRefusal));
        OnPropertyChanged(nameof(HasParserWorkLimitRefusal));
        OnPropertyChanged(nameof(HasTimeLimitRefusal));
        OnPropertyChanged(nameof(ShowUseEstimate));
        OnPropertyChanged(nameof(HasUncommittedParsingLimitChanges));
    }

    // The one way saved limits return to the Selection; the saved mode is set first so no estimate overwrites them.
    private void RestoreSavedLimits()
    {
        _timeLimitBeforeNoStep = null;
        _parserWorkLimitBeforeNoStep = null;
        IsUsingEstimate = _workspace.Context.Setup?.CurrentSelectionLimits.TimeMode != SelectionTimeLimitMode.Explicit;
        _updatingEstimate = true;
        try
        {
            _workspace.Selection.PerWordParserWorkLimitUnbounded = _lastGoodNoParserWorkLimit;
            _workspace.Selection.PerWordParserWorkLimit = _lastGoodParserWorkLimit;
            _workspace.Selection.PerWordTimeLimitSeconds = _lastGoodTimeLimitSeconds;
        }
        finally
        {
            _updatingEstimate = false;
        }
        _workspace.Context.Setup?.SyncParsingLimitsFromSelection();
    }

    private void KeepUnsavedLimits() => KeepLimits(IsNoParserWorkLimit, ParserWorkLimitValue, TimeLimitSeconds);

    // Typed limits stay in their fields while the Selection holds only the saved ones.
    private void KeepLimits(bool noStep, decimal? step, decimal? time)
    {
        var stepMessage = _parserWorkLimitRefusal;
        var timeMessage = TimeLimitRefusal;
        var timeIsTyped = !IsUsingEstimate;
        var estimateChosen = EstimateChosenOverExplicit;
        RestoreSavedLimits();
        if (estimateChosen && _workspace.Context.Setup?.EstimatedPerWordLimitMs is > 0 and var milliseconds) time = milliseconds / 1000m;
        _parserWorkLimitRefusal = stepMessage;
        TimeLimitRefusal = timeMessage;
        if (noStep)
        {
            _parserWorkLimitRefusal = string.Empty;
            TimeLimitRefusal = string.Empty;
        }
        _keepsNoParserWorkLimit = noStep != _lastGoodNoParserWorkLimit;
        _keptNoParserWorkLimit = noStep;
        _keepsParserWorkLimit = !noStep && (step != _lastGoodParserWorkLimit || step is not null && !ParserWorkLimitIsAcceptable(step));
        _keptParserWorkLimit = step;
        _keepsTimeLimit = !noStep && (timeIsTyped || estimateChosen) && (time != _lastGoodTimeLimitSeconds || time is not null && !TimeLimitIsAcceptable(time));
        _keptTimeLimit = time;
        if (_keepsTimeLimit) IsUsingEstimate = estimateChosen;
        NotifyKeptLimitsChanged();
    }

    private void DiscardRefusedLimits()
    {
        if (_keepsParserWorkLimit || _keepsTimeLimit || _keepsNoParserWorkLimit || HasAssessBudgetRefusal) CancelParsingLimitEdit();
    }

    /// <summary>Whether the full shortcut list is open; the rows are built only when it is.</summary>
    [ObservableProperty]
    private bool _shortcutListShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutSearchHasText))]
    [NotifyPropertyChangedFor(nameof(NoShortcutMatchesText))]
    private string _shortcutSearch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomScale))]
    [NotifyPropertyChangedFor(nameof(ZoomLabel))]
    private int _zoomPercent = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemThemeSelected))]
    [NotifyPropertyChangedFor(nameof(IsLightThemeSelected))]
    [NotifyPropertyChangedFor(nameof(IsDarkThemeSelected))]
    [NotifyPropertyChangedFor(nameof(LightThemeTabIndex))]
    [NotifyPropertyChangedFor(nameof(DarkThemeTabIndex))]
    [NotifyPropertyChangedFor(nameof(SystemThemeTabIndex))]
    private WindowThemeChoice _themeChoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimeLimitEnabled))]
    private bool _isUsingEstimate = true;

    [ObservableProperty]
    private SettingsGroup _selectedGroup;

    [ObservableProperty]
    private bool _reportIncludesExpanded;

    [ObservableProperty]
    private string _copyVersionsText = MotifAppStrings.CopyVersions;

    public double ZoomScale => ZoomPercent / 100d;
    public string ZoomLabel => $"{ZoomPercent}%";
    public bool IsSystemThemeSelected => ThemeChoice == WindowThemeChoice.System;
    public bool IsLightThemeSelected => ThemeChoice == WindowThemeChoice.Light;
    public bool IsDarkThemeSelected => ThemeChoice == WindowThemeChoice.Dark;
    public int LightThemeTabIndex => IsLightThemeSelected ? 0 : -1;
    public int DarkThemeTabIndex => IsDarkThemeSelected ? 0 : -1;
    public int SystemThemeTabIndex => IsSystemThemeSelected ? 0 : -1;
    public bool CanResetZoom => ZoomPercent != 100;
    public bool ShowUseEstimate => !IsUsingEstimate && !IsNoParserWorkLimit;
    public string ReportVersionsText => _windowReport.ToVersionText();
    public string ParsingFooter => _workspace.Context.Assess.IsActive
        ? _workspace.Context.Setup?.RunningParserWorkLimitText is { Length: > 0 } text
            ? text : MotifAppStrings.ParsingFooterRunning
        : MotifAppStrings.ParsingFooterIdle;

    public SettingsGroup[] Groups => Enum.GetValues<SettingsGroup>();
    public SettingsGroupOption? SelectedNavigationEntry
    {
        get => PageGroupOptions.FirstOrDefault(option => option.Group == SelectedGroup);
        set
        {
            if (value is not null) OpenGroup(value.Group);
        }
    }

    partial void OnZoomPercentChanged(int value)
    {
        Persist(preferences => preferences with { ZoomPercent = value });
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        ResetZoomCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanResetZoom));
        NotifyResetChoices();
    }

    partial void OnThemeChoiceChanged(WindowThemeChoice value)
    {
        ApplyTheme();
        Persist(preferences => preferences with { Theme = ToUserTheme(value) });
        NotifyResetChoices();
    }

    partial void OnSelectedGroupChanged(SettingsGroup value)
    {
        if (value != SettingsGroup.Parsing) DiscardRefusedLimits();
        if (value == SettingsGroup.KeyboardShortcuts) ShowShortcutList();
        OnPropertyChanged(nameof(IsDisplaySelected));
        OnPropertyChanged(nameof(IsKeyboardShortcutsSelected));
        OnPropertyChanged(nameof(IsParsingSelected));
        OnPropertyChanged(nameof(IsProposalChecksSelected));
        OnPropertyChanged(nameof(IsWritingSystemsSelected));
        OnPropertyChanged(nameof(IsHelpAndReportsSelected));
        OnPropertyChanged(nameof(IsAboutSelected));
        OnPropertyChanged(nameof(ShowSettingsFooter));
        OnPropertyChanged(nameof(SelectedNavigationEntry));
        RefreshNavigationOptions(value);
        Persist(preferences => preferences with { LastSettingsGroup = MapUserSettingsGroup(value) });
        if (value != SettingsGroup.Parsing || !HasProject || !HasSavedSelection) return;
        _workspace.Context.Setup?.SyncParsingLimitsFromSelection();
        if (_timeLimitBeforeNoStep is null)
            IsUsingEstimate = _workspace.Context.Setup?.CurrentSelectionLimits.TimeMode !=
                SelectionTimeLimitMode.Explicit;
        if (IsUsingEstimate) ApplyEstimate();
        RememberGoodValues();
    }

    private void ChooseStoredAnalyses(StoredAnalysisComparison mode)
    {
        if (_storedAnalyses == mode) return;
        var previous = _storedAnalyses;
        try
        {
            _defaultChecks.SetStoredAnalyses(mode);
            _storedAnalyses = mode;
            StoredAnalysesRefusal = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _storedAnalyses = previous;
            StoredAnalysesRefusal = string.Format(MotifAppStrings.StoredAnalysesSaveRefusalFormat, WindowPathText.Scrub(exception.Message));
        }
        OnPropertyChanged(nameof(IsStoredAnalysesBudget));
        OnPropertyChanged(nameof(IsStoredAnalysesAll));
        OnPropertyChanged(nameof(IsStoredAnalysesOff));
        OnPropertyChanged(nameof(StoredAnalysesRefusal));
        OnPropertyChanged(nameof(HasStoredAnalysesRefusal));
        NotifyResetChoices();
    }

    partial void OnShortcutSearchChanged(string value)
    {
        if (ShortcutSearchHasText) ShowShortcutList();
        RefreshShortcutList();
        ClearShortcutSearchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowNoShortcutMatches));
    }

    public void OpenGroup(SettingsGroup group)
    {
        SelectedGroup = group;
        RefreshShortcutList();
    }

    public void SelectGroup(SettingsGroup? group)
    {
        if (group is { } selected) OpenGroup(selected);
    }

    public void OpenWritingSystem(string writingSystemId)
    {
        _writingSystemTargetId = writingSystemId;
        OpenGroup(SettingsGroup.WritingSystems);
    }

    public string? TakeWritingSystemTargetId()
    {
        var target = _writingSystemTargetId;
        _writingSystemTargetId = null;
        return target;
    }

    partial void OnIsUsingEstimateChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUseEstimate));
        NotifyResetChoices();
    }

    public void SelectTheme(WindowThemeChoice choice) => ThemeChoice = choice;

    public void MoveThemeChoice(int direction)
    {
        WindowThemeChoice[] choices = [WindowThemeChoice.Light, WindowThemeChoice.Dark, WindowThemeChoice.System];
        var current = Array.IndexOf(choices, ThemeChoice);
        ThemeChoice = choices[(current + direction + choices.Length) % choices.Length];
    }

    public void ZoomIn()
        => ZoomPercent = ZoomPolicy.Next(ZoomPercent);

    public void ZoomOut() => ZoomPercent = ZoomPolicy.Previous(ZoomPercent);

    public async Task<WindowRefusal?> CommitParsingLimitsAsync()
    {
        if (!HasProject || !HasSavedSelection) return null;
        if (AssessBudgetSeconds is null or < 60 or > 4294967 ||
            decimal.Truncate(AssessBudgetSeconds.Value) != AssessBudgetSeconds.Value)
        {
            AssessBudgetRefusal = MotifAppStrings.AssessBudgetRefusal;
            OnPropertyChanged(nameof(HasAssessBudgetRefusal));
            KeepUnsavedLimits();
            return WindowRefusal.Plain(AssessBudgetRefusal);
        }
        AssessBudgetRefusal = string.Empty;
        OnPropertyChanged(nameof(HasAssessBudgetRefusal));
        if (_workspace.Selection.ParserWorkLimitValidationMessage is not null || !IsNoParserWorkLimit &&
            !ParserWorkLimitIsAcceptable(ParserWorkLimitValue))
        {
            _parserWorkLimitRefusal = MotifAppStrings.ParserWorkLimitWholeNumberRefusal;
            KeepUnsavedLimits();
            OnPropertyChanged(nameof(ParserWorkLimitRefusal));
            OnPropertyChanged(nameof(HasParserWorkLimitRefusal));
            return WindowRefusal.Plain(_parserWorkLimitRefusal);
        }
        if (!IsNoParserWorkLimit && !IsUsingEstimate && (TimeLimitSeconds is null or <= 0))
        {
            TimeLimitRefusal = MotifAppStrings.TimeLimitPositiveRefusal;
            KeepUnsavedLimits();
            OnPropertyChanged(nameof(HasTimeLimitRefusal));
            return WindowRefusal.Plain(TimeLimitRefusal);
        }

        var parserWorkLimit = IsNoParserWorkLimit ? ParserWorkLimit.Unbounded
            : ParserWorkLimitValue is > 0 and var steps && decimal.Truncate(steps) == steps && steps <= long.MaxValue
                ? new ParserWorkLimit(decimal.ToInt64(steps)) : ParserWorkLimit.Default;
        SelectionParsingLimits limits;
        if (IsNoParserWorkLimit || IsUsingEstimate)
        {
            limits = SelectionLimitPolicy.Estimated(parserWorkLimit);
        }
        else if (TimeLimitSeconds is { } seconds && seconds > 0 &&
                 seconds * 1000m <= int.MaxValue && decimal.Round(seconds * 1000m) >= 1m)
        {
            limits = SelectionLimitPolicy.Explicit(parserWorkLimit,
                decimal.ToInt32(decimal.Round(seconds * 1000m, 0, MidpointRounding.AwayFromZero)));
        }
        else
        {
            TimeLimitRefusal = MotifAppStrings.TimeLimitRangeRefusal;
            KeepUnsavedLimits();
            OnPropertyChanged(nameof(HasTimeLimitRefusal));
            return WindowRefusal.Plain(TimeLimitRefusal);
        }
        limits = limits with { AssessBudgetSeconds = decimal.ToInt32(AssessBudgetSeconds!.Value) };
        var refusal = await _workspace.Context.Setup!.SaveParsingLimitsAsync(limits).ConfigureAwait(true);
        if (refusal is null)
        {
            TimeLimitRefusal = string.Empty;
            _parserWorkLimitRefusal = string.Empty;
            RememberGoodValues();
        }
        else
        {
            CancelParsingLimitEdit();
            TimeLimitRefusal = refusal.Sentence;
        }
        OnPropertyChanged(nameof(HasTimeLimitRefusal));
        OnPropertyChanged(nameof(ParserWorkLimitRefusal));
        OnPropertyChanged(nameof(HasParserWorkLimitRefusal));
        OnPropertyChanged(nameof(ParsingFooter));
        OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
        OnPropertyChanged(nameof(ProjectName));
        return refusal;
    }

    /// <summary>Selects all project Texts and saves that Selection.</summary>
    public async Task<WindowRefusal?> UseAllTextsAsync()
    {
        if (_observedSetup is null) return null;
        SetTextSelectionRefusal(string.Empty);
        var refusal = await _observedSetup.SelectAllTextsAsync().ConfigureAwait(true);
        SetTextSelectionRefusal(refusal?.Sentence ?? string.Empty);
        UseAllTextsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUseAllTexts));
        OnPropertyChanged(nameof(TextSelectionSummary));
        NotifyResetChoices();
        return refusal;
    }

    public void MarkVersionsCopied()
    {
        CopyVersionsText = MotifAppStrings.Copied;
        _ = ResetCopiedLabelAsync();
    }

    public void UseMotifEstimate()
    {
        IsUsingEstimate = true;
        ApplyEstimate();
        if (ShowsRefusedLimit) KeepUnsavedLimits();
        OnPropertyChanged(nameof(ShowUseEstimate));
    }

    public void CancelParsingLimitEdit()
    {
        RestoreSavedLimits();
        AssessBudgetSeconds = _lastGoodAssessBudgetSeconds;
        RememberGoodValues();
    }

    private void RememberGoodValues()
    {
        _lastGoodAssessBudgetSeconds = _workspace.Context.Setup?.CurrentSelectionLimits.AssessBudgetSeconds ?? SelectionParsingLimits.DefaultAssessBudgetSeconds;
        AssessBudgetSeconds = _lastGoodAssessBudgetSeconds;
        _lastGoodNoParserWorkLimit = _workspace.Selection.PerWordParserWorkLimitUnbounded;
        if (_workspace.Selection.ParserWorkLimitValidationMessage is null && _workspace.Selection.PerWordParserWorkLimit is > 0)
            _lastGoodParserWorkLimit = _workspace.Selection.PerWordParserWorkLimit;
        if (_workspace.Selection.PerWordTimeLimitSeconds is > 0)
            _lastGoodTimeLimitSeconds = _workspace.Selection.PerWordTimeLimitSeconds;
        else _lastGoodTimeLimitSeconds = null;
        ClearHeldLimits();
    }

    // Saved limits belong to the project that was open, so a change of project drops them and anything held.
    private void ForgetSavedLimits()
    {
        _lastGoodNoParserWorkLimit = false;
        _lastGoodParserWorkLimit = null;
        _lastGoodTimeLimitSeconds = null;
        _parserWorkLimitBeforeNoStep = null;
        _timeLimitBeforeNoStep = null;
        _lastGoodAssessBudgetSeconds = SelectionParsingLimits.DefaultAssessBudgetSeconds;
        AssessBudgetSeconds = SelectionParsingLimits.DefaultAssessBudgetSeconds;
        ClearHeldLimits();
    }

    private void ClearHeldLimits()
    {
        _keepsParserWorkLimit = false;
        _keepsTimeLimit = false;
        _keepsNoParserWorkLimit = false;
        _parserWorkLimitRefusal = string.Empty;
        TimeLimitRefusal = string.Empty;
        AssessBudgetRefusal = string.Empty;
        OnPropertyChanged(nameof(ParserWorkLimitValue));
        OnPropertyChanged(nameof(TimeLimitSeconds));
        OnPropertyChanged(nameof(IsNoParserWorkLimit));
        OnPropertyChanged(nameof(IsTimeLimitEnabled));
        OnPropertyChanged(nameof(ShowUseEstimate));
        OnPropertyChanged(nameof(HasAssessBudgetRefusal));
        OnPropertyChanged(nameof(ParserWorkLimitRefusal));
        OnPropertyChanged(nameof(HasParserWorkLimitRefusal));
        OnPropertyChanged(nameof(HasTimeLimitRefusal));
        OnPropertyChanged(nameof(HasUncommittedParsingLimitChanges));
    }

    private async Task ResetCopiedLabelAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        CopyVersionsText = MotifAppStrings.CopyVersions;
    }

    private void RefreshShortcutList()
    {
        ShortcutEntries.Clear();
        foreach (var entry in _shortcutCatalog.Where(entry =>
                     string.IsNullOrWhiteSpace(ShortcutSearch) ||
                     entry.SearchText.Contains(ShortcutSearch, StringComparison.CurrentCultureIgnoreCase) ||
                     entry.Action.Contains(ShortcutSearch, StringComparison.CurrentCultureIgnoreCase) ||
                     entry.Group.Contains(ShortcutSearch, StringComparison.CurrentCultureIgnoreCase)))
            ShortcutEntries.Add(entry);
        OnPropertyChanged(nameof(HasShortcutMatches));
        OnPropertyChanged(nameof(NoShortcutMatchesText));
        OnPropertyChanged(nameof(ShowNoShortcutMatches));
    }

    private void RefreshNavigationOptions(SettingsGroup selected)
    {
        foreach (var option in PageGroupOptions)
        {
            option.IsSelected = option.Group == selected;
            if (option.Group == SettingsGroup.Parsing) option.Title = GroupParsingLabel;
            if (option.Group == SettingsGroup.WritingSystems)
                option.HasWarning = HasMissingWritingSystemFont;
        }
    }

    private void OnOverviewChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(OverviewPageModel.Overview)) return;
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(GroupParsingLabel));
        OnPropertyChanged(nameof(ParsingLead));
        OnPropertyChanged(nameof(WritingSystemsHeading));
        OnPropertyChanged(nameof(WritingSystemsLead));
        RefreshWritingSystems();
    }

    private void RefreshWritingSystems()
    {
        var systems = _overview.Overview?.WritingSystems ?? [];
        VernacularWritingSystems.Clear();
        AnalysisWritingSystems.Clear();
        foreach (var system in systems.Where(system => system.Kind == WritingSystemKind.Vernacular)
                     .OrderBy(system => system.Position))
            VernacularWritingSystems.Add(new WritingSystemSettingsRow(system, _workspace.Context.TextStyles,
                detailsExpanded: Remembered(SettingsExpanderKeys.VernacularDetails),
                onDetailsExpanded: expanded => RememberDetails(SettingsExpanderKeys.VernacularDetails, VernacularWritingSystems, expanded)));
        var vernacularIds = systems.Where(system => system.Kind == WritingSystemKind.Vernacular)
            .Select(system => system.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var system in systems.Where(system => system.Kind == WritingSystemKind.Analysis)
                     .OrderBy(system => system.Position))
            AnalysisWritingSystems.Add(new WritingSystemSettingsRow(system,
                _workspace.Context.TextStyles, vernacularIds.Contains(system.Id),
                Remembered(SettingsExpanderKeys.AnalysisDetails),
                expanded => RememberDetails(SettingsExpanderKeys.AnalysisDetails, AnalysisWritingSystems, expanded)));
        OnPropertyChanged(nameof(HasWritingSystems));
        OnPropertyChanged(nameof(HasVernacularWritingSystems));
        OnPropertyChanged(nameof(HasAnalysisWritingSystems));
        OnPropertyChanged(nameof(ShowWritingSystemsEmptyState));
        OnPropertyChanged(nameof(WritingSystemsEmptyText));
        OnPropertyChanged(nameof(HasMissingWritingSystemFont));
        OnPropertyChanged(nameof(ShowSettingsFooter));
        RefreshNavigationOptions(SelectedGroup);
    }

    private void ApplyEstimate()
    {
        if (_updatingEstimate) return;
        _updatingEstimate = true;
        try
        {
            var milliseconds = _workspace.Context.Setup?.EstimatedPerWordLimitMs;
            TimeLimitSeconds = milliseconds is > 0 ? milliseconds.Value / 1000m : null;
        }
        finally
        {
            _updatingEstimate = false;
            IsUsingEstimate = true;
            OnPropertyChanged(nameof(ShowUseEstimate));
        }
    }

    private void ApplyTheme()
    {
        if (Application.Current is { } application)
            application.RequestedThemeVariant = ThemeChoice switch
            {
                WindowThemeChoice.Light => ThemeVariant.Light,
                WindowThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }

    partial void OnShortcutListShownChanged(bool value) => Remember(SettingsExpanderKeys.ShortcutList, value);

    partial void OnReportIncludesExpandedChanged(bool value) => Remember(SettingsExpanderKeys.ReportIncludes, value);

    [ObservableProperty]
    private bool _showHandoffByCopyingFiles;

    partial void OnShowHandoffByCopyingFilesChanged(bool value) => Remember(SettingsExpanderKeys.ShowHandoff, value);

    private bool Remembered(string key) => _preferences.GetExpanded(key) ?? false;

    private void Remember(string key, bool expanded)
    {
        if (_suppressPreferenceWrites) return;
        _preferences.SetExpanded(key, expanded);
        UpdatePreferenceProblem();
    }

    // A toggle applies to the whole list, so rows never disagree.
    private void RememberDetails(string key, IEnumerable<WritingSystemSettingsRow> rows, bool expanded)
    {
        if (_suppressPreferenceWrites) return;
        Remember(key, expanded);
        var suppressed = _suppressPreferenceWrites;
        _suppressPreferenceWrites = true;
        try { foreach (var row in rows.ToArray()) row.DetailsExpanded = expanded; }
        finally { _suppressPreferenceWrites = suppressed; }
    }

    // Opening for the user's own action (group, search) is not a remembered choice.
    private void ShowShortcutList()
    {
        var suppressed = _suppressPreferenceWrites;
        _suppressPreferenceWrites = true;
        try { ShortcutListShown = true; }
        finally { _suppressPreferenceWrites = suppressed; }
    }

    private void Persist(Func<UserPreferences, UserPreferences> edit)
    {
        if (_suppressPreferenceWrites) return;
        _preferences.Update(edit);
        UpdatePreferenceProblem();
    }

    private void ResetPreferences()
    {
        _preferences.Reset();
        _suppressPreferenceWrites = true;
        try
        {
            ZoomPercent = UserPreferences.Defaults.ZoomPercent;
            ThemeChoice = WindowThemeChoice.System;
            SelectedGroup = SettingsGroup.Display;
            ShowHandoffByCopyingFiles = false;
        }
        finally
        {
            _suppressPreferenceWrites = false;
        }
        UpdatePreferenceProblem();
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        // A delayed save reports a failure from a thread-pool thread.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPreferencesChanged(sender, e));
            return;
        }

        UpdatePreferenceProblem();
        ResetPreferencesCommand.NotifyCanExecuteChanged();
    }

    private void UpdatePreferenceProblem()
    {
        PreferenceProblemText = _preferences.Problem?.Message ?? string.Empty;
        OnPropertyChanged(nameof(PreferenceProblemText));
        OnPropertyChanged(nameof(HasPreferenceProblem));
        OnPropertyChanged(nameof(ShowResetPreferences));
        ResetPreferencesCommand.NotifyCanExecuteChanged();
    }

    private static WindowThemeChoice ToWindowTheme(UserTheme theme) => theme switch
    {
        UserTheme.Light => WindowThemeChoice.Light,
        UserTheme.Dark => WindowThemeChoice.Dark,
        _ => WindowThemeChoice.System,
    };

    private static UserTheme ToUserTheme(WindowThemeChoice theme) => theme switch
    {
        WindowThemeChoice.Light => UserTheme.Light,
        WindowThemeChoice.Dark => UserTheme.Dark,
        _ => UserTheme.System,
    };

    private static SettingsGroup MapSettingsGroup(SIL.Motif.App.Services.SettingsGroup group) => group switch
    {
        SIL.Motif.App.Services.SettingsGroup.Shortcuts => SettingsGroup.KeyboardShortcuts,
        SIL.Motif.App.Services.SettingsGroup.Parsing => SettingsGroup.Parsing,
        SIL.Motif.App.Services.SettingsGroup.ProposalChecks => SettingsGroup.ProposalChecks,
        SIL.Motif.App.Services.SettingsGroup.WritingSystems => SettingsGroup.WritingSystems,
        SIL.Motif.App.Services.SettingsGroup.Texts => SettingsGroup.Texts,
        SIL.Motif.App.Services.SettingsGroup.Help => SettingsGroup.HelpAndReports,
        SIL.Motif.App.Services.SettingsGroup.About => SettingsGroup.About,
        _ => SettingsGroup.Display,
    };

    private static SIL.Motif.App.Services.SettingsGroup MapUserSettingsGroup(SettingsGroup group) => group switch
    {
        SettingsGroup.KeyboardShortcuts => SIL.Motif.App.Services.SettingsGroup.Shortcuts,
        SettingsGroup.Parsing => SIL.Motif.App.Services.SettingsGroup.Parsing,
        SettingsGroup.ProposalChecks => SIL.Motif.App.Services.SettingsGroup.ProposalChecks,
        SettingsGroup.WritingSystems => SIL.Motif.App.Services.SettingsGroup.WritingSystems,
        SettingsGroup.Texts => SIL.Motif.App.Services.SettingsGroup.Texts,
        SettingsGroup.HelpAndReports => SIL.Motif.App.Services.SettingsGroup.Help,
        SettingsGroup.About => SIL.Motif.App.Services.SettingsGroup.About,
        _ => SIL.Motif.App.Services.SettingsGroup.Display,
    };

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SelectionViewModel.ChosenTextIds) or
            nameof(SelectionViewModel.AreAllTextsSelected) or nameof(SelectionViewModel.HasTexts) or
            nameof(SelectionViewModel.TextCount))
        {
            OnPropertyChanged(nameof(TextSelectionSummary));
            OnPropertyChanged(nameof(CanUseAllTexts));
            UseAllTextsCommand.NotifyCanExecuteChanged();
            NotifyResetChoices();
        }
        if (args.PropertyName is nameof(SelectionViewModel.PerWordParserWorkLimit) or
            nameof(SelectionViewModel.PerWordParserWorkLimitUnbounded))
        {
            _workspace.Context.Setup?.SyncParsingLimitsFromSelection();
            var accepted = _workspace.Context.Setup?.CurrentSelectionLimits.PerWordParserWorkLimit;
            var current = _workspace.Selection.PerWordParserWorkLimitUnbounded ? ParserWorkLimit.Unbounded
                : _workspace.Selection.PerWordParserWorkLimit is { } steps && steps is > 0 and <= long.MaxValue &&
                  decimal.Truncate(steps) == steps ? new ParserWorkLimit(decimal.ToInt64(steps)) : null;
            if ((accepted is null || accepted != current) && IsUsingEstimate)
            {
                ApplyEstimate();
            }
            OnPropertyChanged(nameof(ParserWorkLimitValue));
            OnPropertyChanged(nameof(IsNoParserWorkLimit));
            OnPropertyChanged(nameof(ParserWorkLimitRefusal));
            OnPropertyChanged(nameof(HasParserWorkLimitRefusal));
            OnPropertyChanged(nameof(IsTimeLimitEnabled));
            OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
        }
        if (args.PropertyName == nameof(SelectionViewModel.PerWordTimeLimitSeconds))
        {
            OnPropertyChanged(nameof(TimeLimitSeconds));
            OnPropertyChanged(nameof(ShowUseEstimate));
        }
        OnPropertyChanged(nameof(HasUncommittedParsingLimitChanges));
        OnPropertyChanged(nameof(ParsingFooter));
        NotifyResetChoices();
    }

    private void OnSetupChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SetupViewModel.ProjectPath)) ForgetSavedLimits();
        if (args.PropertyName is nameof(SetupViewModel.CanRunDefaultSelection) or nameof(SetupViewModel.ProjectPath))
        {
            OnPropertyChanged(nameof(HasSavedSelection));
            OnPropertyChanged(nameof(HasProjectWithoutSelection));
            OnPropertyChanged(nameof(IsTimeLimitEnabled));
            OnPropertyChanged(nameof(CanUseAllTexts));
            UseAllTextsCommand.NotifyCanExecuteChanged();
            NotifyResetChoices();
        }
        if (args.PropertyName == nameof(SetupViewModel.CurrentSelectionLimits))
        {
            IsUsingEstimate = _workspace.Context.Setup?.CurrentSelectionLimits.TimeMode !=
                SelectionTimeLimitMode.Explicit;
            if (IsUsingEstimate) ApplyEstimate();
            if (_observedSetup?.ProjectPath is not null) RememberGoodValues();
        }
        if (args.PropertyName is nameof(SetupViewModel.ParserWorkLimitEstimateText) or
            nameof(SetupViewModel.EstimatedPerWordLimitMs) or nameof(SetupViewModel.RunningParserWorkLimitText))
        {
            OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
            OnPropertyChanged(nameof(ParsingFooter));
            if (IsUsingEstimate) ApplyEstimate();
        }
    }

    private void OnAssessChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AssessViewModel.IsActive)) OnPropertyChanged(nameof(ParsingFooter));
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspaceContext.CurrentPage) &&
            _workspace.Context.CurrentPage != WorkspacePage.Settings) DiscardRefusedLimits();
        if (args.PropertyName == nameof(WorkspaceContext.Setup))
        {
            ObserveSetup(_workspace.Context.Setup);
            IsUsingEstimate = _observedSetup?.CurrentSelectionLimits.TimeMode != SelectionTimeLimitMode.Explicit;
            if (IsUsingEstimate) ApplyEstimate();
            RememberGoodValues();
        }
        if (args.PropertyName is nameof(WorkspaceContext.ProjectPath) or nameof(WorkspaceContext.HasProject) or
            nameof(WorkspaceContext.ProjectName) or nameof(WorkspaceContext.Setup))
        {
            OnPropertyChanged(nameof(ProjectName));
            OnPropertyChanged(nameof(HasProject));
            OnPropertyChanged(nameof(HasSavedSelection));
            OnPropertyChanged(nameof(HasProjectWithoutSelection));
            OnPropertyChanged(nameof(CanUseAllTexts));
            UseAllTextsCommand.NotifyCanExecuteChanged();
            NotifyResetChoices();
            OnPropertyChanged(nameof(IsTimeLimitEnabled));
            OnPropertyChanged(nameof(GroupParsingLabel));
            OnPropertyChanged(nameof(ParsingLead));
            OnPropertyChanged(nameof(WritingSystemsHeading));
            OnPropertyChanged(nameof(WritingSystemsLead));
            OnPropertyChanged(nameof(ShowWritingSystemsEmptyState));
            OnPropertyChanged(nameof(WritingSystemsEmptyText));
            OnPropertyChanged(nameof(ShowSettingsFooter));
            OnPropertyChanged(nameof(IsTimeLimitEnabled));
            OnPropertyChanged(nameof(ParsingFooter));
            foreach (var option in PageGroupOptions.Where(option => option.Group == SettingsGroup.Parsing))
                option.Title = GroupParsingLabel;
        }
    }

    private void ObserveSetup(SetupViewModel? setup)
    {
        if (ReferenceEquals(_observedSetup, setup)) return;
        if (_observedSetup is not null) _observedSetup.PropertyChanged -= OnSetupChanged;
        _observedSetup = setup;
        if (_observedSetup is not null) _observedSetup.PropertyChanged += OnSetupChanged;
    }

    private void NotifyResetChoices()
    {
        OnPropertyChanged(nameof(CanResetTheme));
        OnPropertyChanged(nameof(CanResetStoredAnalyses));
        OnPropertyChanged(nameof(CanResetParserWorkLimit));
        ResetThemeCommand.NotifyCanExecuteChanged();
        ResetStoredAnalysesCommand.NotifyCanExecuteChanged();
        ResetParserWorkLimitCommand.NotifyCanExecuteChanged();
    }

    private async Task ResetParserWorkLimitAsync()
    {
        IsNoParserWorkLimit = false;
        ParserWorkLimitValue = ParserWorkLimit.DefaultWork;
        await CommitParsingLimitsAsync().ConfigureAwait(true);
    }

    private async Task UseAllTextsCommandAsync()
    {
        await UseAllTextsAsync().ConfigureAwait(true);
    }

    private void SetTextSelectionRefusal(string value)
    {
        if (TextSelectionRefusal == value) return;
        TextSelectionRefusal = value;
        OnPropertyChanged(nameof(TextSelectionRefusal));
        OnPropertyChanged(nameof(HasTextSelectionRefusal));
    }

}
