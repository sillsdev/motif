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

namespace SIL.Motif.App.ViewModels;

internal sealed record ShortcutEntry(string Group, string Keys, string Action, string SearchText);

internal enum SettingsGroup
{
    Display,
    KeyboardShortcuts,
    Parsing,
    WritingSystems,
    HelpAndReports,
    About,
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

internal sealed class WritingSystemSettingsRow
{
    public WritingSystemSettingsRow(WritingSystemDisplay writingSystem, TextStyles resolver,
        bool isAlsoInVernacular = false)
    {
        WritingSystem = writingSystem;
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

    public WritingSystemDisplay WritingSystem { get; }
    public string Id => WritingSystem.Id;
    public string Name => string.IsNullOrWhiteSpace(WritingSystem.Name) ? WritingSystem.Id : WritingSystem.Name;
    public string Abbreviation => WritingSystem.Abbreviation;
    public bool HasAbbreviation => !string.IsNullOrWhiteSpace(Abbreviation);
    public bool HasTag => !string.IsNullOrWhiteSpace(Id) && !string.Equals(Id, Abbreviation, StringComparison.Ordinal);
    public bool IsDefault => WritingSystem.IsDefault;
    public bool ShowDefault => IsDefault && !IsAlsoInVernacular;
    public bool IsAlsoInVernacular { get; }
    public string AlsoInVernacularText => "Also in Vernacular, with the same settings.";
    public string FontFamily => ResolveStyleFont("Normal").FontFamily;
    public string FontLabel => string.IsNullOrWhiteSpace(FontFamily) ? "None set" : FontFamily;
    public string Features => string.IsNullOrWhiteSpace(ResolveStyleFont("Normal").FontFeatures)
        ? "None" : ResolveStyleFont("Normal").FontFeatures;
    public string Direction => WritingSystem.RightToLeft ? "Right to left" : "Left to right";
    public string StyleSizes => string.Join(" · ", WritingSystem.StyleSizes
        .OrderBy(pair => string.Equals(pair.Key, "Normal", StringComparison.Ordinal) ? 0 : 1)
        .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
        .Select(pair => $"{pair.Value.ToString("0.##", CultureInfo.InvariantCulture)} pt ({pair.Key})"));
    public bool HasStyleSizes => !string.IsNullOrEmpty(StyleSizes);
    public IReadOnlyList<string> MissingFontFamilies { get; }
    public bool IsFontMissing => MissingFontFamilies.Count > 0;
    public string MissingFontCaption => MissingFontFamilies.Count == 0 ? string.Empty :
        $"Not installed: {string.Join(" · ", MissingFontFamilies)}. Motif uses an available fallback.";
    public TextAlignment PreviewTextAlignment => WritingSystem.RightToLeft ? TextAlignment.Right : TextAlignment.Left;

    private WritingSystemStyleFont ResolveStyleFont(string styleName) =>
        WritingSystem.StyleFonts.GetValueOrDefault(styleName) ??
        new WritingSystemStyleFont(WritingSystem.FontFamily, WritingSystem.FontFeatures);
}

internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUserPreferencesStore _preferences;
    private readonly WorkspaceShellViewModel _workspace;
    private readonly ApplicationFacts _applicationFacts;
    private readonly ShortcutEntry[] _shortcutCatalog;
    private readonly ProblemReport _windowReport;
    private readonly bool _panGlossExecutableFound;
    private readonly OverviewPageModel _overview;
    private string? _writingSystemTargetId;
    private SetupViewModel? _observedSetup;
    private bool _updatingEstimate;
    private decimal? _lastGoodStepLimit;
    private decimal? _lastGoodTimeLimitSeconds;
    private decimal? _stepLimitBeforeNoStep;
    private decimal? _timeLimitBeforeNoStep;
    private bool _lastGoodNoStepLimit;
    private string _stepLimitRefusal = string.Empty;
    private bool _suppressPreferenceWrites;

    public SettingsViewModel(WorkspaceShellViewModel workspace, IUserPreferencesStore? preferences = null,
        ApplicationFacts? applicationFacts = null)
    {
        _workspace = workspace;
        _overview = workspace.PageModel<OverviewPageModel>();
        _preferences = preferences ?? new MemoryUserPreferencesStore();
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
        ResetPreferencesCommand = new RelayCommand(ResetPreferences, () => _preferences.State == PreferenceState.Refused);
        SelectGroupCommand = new RelayCommand<SettingsGroup>(group => SelectGroup(group));
        ClearShortcutSearchCommand = new RelayCommand(() => ShortcutSearch = string.Empty,
            () => !string.IsNullOrEmpty(ShortcutSearch));
        SelectThemeCommand = new RelayCommand<WindowThemeChoice>(choice => ThemeChoice = choice);
        GroupOptions = new ObservableCollection<SettingsGroupOption>([
            new(SettingsGroup.Display, "Display"),
            new(SettingsGroup.KeyboardShortcuts, "Keyboard shortcuts"),
            new(SettingsGroup.Parsing, "Parsing"),
            new(SettingsGroup.WritingSystems, "Writing systems"),
            new(SettingsGroup.HelpAndReports, "Help and problem reports"),
            new(SettingsGroup.About, "About Motif"),
        ]);
        _selectedGroup = MapSettingsGroup(saved.LastSettingsGroup);
        RefreshNavigationOptions(SelectedGroup);
        ApplyTheme();
        _preferences.Changed += OnPreferencesChanged;
        _workspace.Selection.PropertyChanged += OnSelectionChanged;
        _overview.PropertyChanged += OnOverviewChanged;
        _workspace.Context.Assess.PropertyChanged += OnAssessChanged;
        _workspace.Context.PropertyChanged += OnContextChanged;
        ObserveSetup(_workspace.Context.Setup);
        RefreshWritingSystems();
        IsUsingEstimate = _observedSetup?.CurrentSelectionLimits.TimeMode != SelectionTimeLimitMode.Explicit;
        RememberGoodValues();
    }

    public IRelayCommand ZoomInCommand { get; }
    public IRelayCommand ZoomOutCommand { get; }
    public IRelayCommand ResetZoomCommand { get; }
    public IRelayCommand ResetPreferencesCommand { get; }
    public IRelayCommand<SettingsGroup> SelectGroupCommand { get; }
    public IRelayCommand<WindowThemeChoice> SelectThemeCommand { get; }
    public IRelayCommand ClearShortcutSearchCommand { get; }
    public ObservableCollection<SettingsGroupOption> GroupOptions { get; }
    public ObservableCollection<WritingSystemSettingsRow> VernacularWritingSystems { get; } = [];
    public ObservableCollection<WritingSystemSettingsRow> AnalysisWritingSystems { get; } = [];
    public TextStyles WritingSystemTextStyles => _workspace.Context.TextStyles;
    public ObservableCollection<ShortcutEntry> ShortcutEntries { get; } = [];
    public bool ShortcutRegistryAvailable => true;
    public string PreferenceProblemText { get; private set; } = string.Empty;
    public bool HasPreferenceProblem => !string.IsNullOrEmpty(PreferenceProblemText);
    public bool ShowResetPreferences => _preferences.State == PreferenceState.Refused;
    public string ThemeSystemLabel => OperatingSystem.IsWindows() ? "Match Windows"
        : OperatingSystem.IsMacOS() ? "Match macOS" : "Match system";
    public string HelpCaption => "Help for the page you are on.";
    public string ReportCaption => "You see the whole report before anything leaves this computer. " +
        "Opening the issue form doesn't submit it.";
    public string ZoomShortcutHint => string.Join(" · ", _shortcutCatalog
        .Where(entry => entry.Action is "Zoom in" or "Zoom out" or "Reset zoom")
        .Select(entry => $"{entry.Action}: {entry.Keys}"));
    public string MotifVersion => _windowReport.MotifVersion;
    public string PanGlossVersion => _windowReport.PanGlossVersion;
    public string OperatingSystemText => _windowReport.OperatingSystem;
    public string DataRoot => _applicationFacts.ManagedDataRoot;
    public string DataFolderActionText => OperatingSystem.IsWindows() ? "Show in Explorer"
        : OperatingSystem.IsMacOS() ? "Show in Finder" : "Open folder";
    public string ParsingLead => $"These limits are saved with {ProjectName}'s Selection. Setup chose them; " +
        "you can change them here or under Texts › Analysis options.";
    public string ProjectName => !string.IsNullOrWhiteSpace(_overview.Overview?.ProjectName)
        ? _overview.Overview.ProjectName
        : !string.IsNullOrWhiteSpace(_workspace.Context.Setup?.ProjectName)
            ? _workspace.Context.Setup.ProjectName
            : Path.GetFileNameWithoutExtension(_workspace.Context.ProjectPath ?? _workspace.ProjectName);
    public bool HasProject => _workspace.HasProject;
    public bool HasSavedSelection => _workspace.Context.Setup?.CanRunDefaultSelection == true;
    public bool HasProjectWithoutSelection => HasProject && !HasSavedSelection;
    public bool PanGlossNotFound => !_panGlossExecutableFound;
    public bool ShortcutSearchHasText => !string.IsNullOrWhiteSpace(ShortcutSearch);
    public bool HasShortcutMatches => ShortcutEntries.Count > 0;
    public bool ShowNoShortcutMatches => ShortcutRegistryAvailable && ShortcutSearchHasText && !HasShortcutMatches;
    public string NoShortcutMatchesText => $"No shortcut matches \"{ShortcutSearch}\".";
    public bool IsDisplaySelected => SelectedGroup == SettingsGroup.Display;
    public bool IsKeyboardShortcutsSelected => SelectedGroup == SettingsGroup.KeyboardShortcuts;
    public bool IsParsingSelected => SelectedGroup == SettingsGroup.Parsing;
    public bool IsWritingSystemsSelected => SelectedGroup == SettingsGroup.WritingSystems;
    public bool IsHelpAndReportsSelected => SelectedGroup == SettingsGroup.HelpAndReports;
    public bool IsAboutSelected => SelectedGroup == SettingsGroup.About;
    public string GroupParsingLabel => HasProject ? $"Parsing for {ProjectName}" : "Parsing";
    public string WritingSystemsHeading => HasProject ? $"Writing systems in {ProjectName}" : "Writing systems";
    public string WritingSystemsLead => $"Motif draws each word with the font, direction and size {ProjectName} gives it. To change them, use FieldWorks.";
    public bool HasWritingSystems => VernacularWritingSystems.Count + AnalysisWritingSystems.Count > 0;
    public bool HasVernacularWritingSystems => VernacularWritingSystems.Count > 0;
    public bool HasAnalysisWritingSystems => AnalysisWritingSystems.Count > 0;
    public bool ShowWritingSystemsEmptyState => HasProject && !HasWritingSystems;
    public string WritingSystemsEmptyText => _overview.Overview is null
        ? "Refresh to see the writing systems saved in FieldWorks."
        : "No writing systems were recorded in the Baseline.";
    public bool HasMissingWritingSystemFont => VernacularWritingSystems.Concat(AnalysisWritingSystems)
        .Any(row => row.IsFontMissing);
    public bool ShowSettingsFooter => IsParsingSelected || IsWritingSystemsSelected && HasProject;
    public string StepLimitRefusal => _stepLimitRefusal.Length > 0
        ? _stepLimitRefusal : _workspace.Selection.StepLimitValidationMessage ?? string.Empty;
    public string TimeLimitRefusal { get => _timeLimitRefusal; private set => SetProperty(ref _timeLimitRefusal, value); }
    private string _timeLimitRefusal = string.Empty;
    public bool HasStepLimitRefusal => !string.IsNullOrEmpty(StepLimitRefusal);
    public bool HasTimeLimitRefusal => !string.IsNullOrEmpty(TimeLimitRefusal);
    public bool HasUncommittedParsingLimitChanges =>
        IsNoStepLimit != _lastGoodNoStepLimit || StepLimit != _lastGoodStepLimit ||
        TimeLimitSeconds != _lastGoodTimeLimitSeconds;
    public string StepLimitEstimateText => _workspace.Context.Setup?.StepLimitEstimateText ??
        "The estimate uses parser statistics from the last parse when available.";
    public bool IsTimeLimitEnabled => HasProject && HasSavedSelection && !IsNoStepLimit;
    public bool IsNoStepLimit
    {
        get => _workspace.Selection.PerWordStepLimitUnbounded;
        set
        {
            if (_workspace.Selection.PerWordStepLimitUnbounded == value) return;
            if (value)
            {
                _stepLimitBeforeNoStep = StepLimit ?? _lastGoodStepLimit ??
                    _workspace.Context.Setup?.StepLimitSteps ?? StepCap.DefaultSteps;
                _timeLimitBeforeNoStep = _workspace.Selection.PerWordTimeLimitSeconds;
            }
            _workspace.Selection.PerWordStepLimitUnbounded = value;
            if (value)
            {
                _workspace.Selection.PerWordTimeLimitSeconds = null;
                IsUsingEstimate = true;
            }
            else
            {
                var timeLimitToRestore = _timeLimitBeforeNoStep;
                _workspace.Selection.PerWordStepLimit = _stepLimitBeforeNoStep ?? _lastGoodStepLimit ??
                    _workspace.Context.Setup?.StepLimitSteps ?? StepCap.DefaultSteps;
                _timeLimitBeforeNoStep = null;
                _stepLimitBeforeNoStep = null;
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
    public decimal? StepLimit
    {
        get => IsNoStepLimit
            ? _stepLimitBeforeNoStep ?? _lastGoodStepLimit ?? _workspace.Context.Setup?.StepLimitSteps
            : _workspace.Selection.PerWordStepLimit;
        set
        {
            if (_workspace.Selection.PerWordStepLimit == value) return;
            _workspace.Selection.PerWordStepLimit = value;
            if (IsUsingEstimate) ApplyEstimate();
        }
    }
    public decimal? TimeLimitSeconds
    {
        get => _workspace.Selection.PerWordTimeLimitSeconds;
        set
        {
            if (_workspace.Selection.PerWordTimeLimitSeconds == value) return;
            _workspace.Selection.PerWordTimeLimitSeconds = value;
            if (!IsNoStepLimit) IsUsingEstimate = false;
            TimeLimitRefusal = !IsNoStepLimit && (value is null or <= 0)
                ? "Enter a per-word time limit greater than zero." : string.Empty;
            OnPropertyChanged(nameof(HasTimeLimitRefusal));
        }
    }
    public bool IsUsingMotifEstimate => IsUsingEstimate;

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
    private string _copyVersionsText = "Copy versions";

    public double ZoomScale => ZoomPercent / 100d;
    public string ZoomLabel => $"{ZoomPercent}%";
    public bool IsSystemThemeSelected => ThemeChoice == WindowThemeChoice.System;
    public bool IsLightThemeSelected => ThemeChoice == WindowThemeChoice.Light;
    public bool IsDarkThemeSelected => ThemeChoice == WindowThemeChoice.Dark;
    public int LightThemeTabIndex => IsLightThemeSelected ? 0 : -1;
    public int DarkThemeTabIndex => IsDarkThemeSelected ? 0 : -1;
    public int SystemThemeTabIndex => IsSystemThemeSelected ? 0 : -1;
    public bool ShowZoomReset => ZoomPercent != 100;
    public bool ShowUseEstimate => !IsUsingEstimate && !IsNoStepLimit;
    public string ReportVersionsText => _windowReport.ToVersionText();
    public string ParsingFooter => _workspace.Context.Assess.IsActive
        ? _workspace.Context.Setup?.RunningStepLimitText is { Length: > 0 } text
            ? text : "The parse running now keeps the limits it started with."
        : "Used from the next parse. Results already on screen keep the limits they were parsed with.";

    public SettingsGroup[] Groups => Enum.GetValues<SettingsGroup>();
    public SettingsGroupOption? SelectedNavigationEntry
    {
        get => GroupOptions.FirstOrDefault(option => option.Group == SelectedGroup);
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
        OnPropertyChanged(nameof(ShowZoomReset));
    }

    partial void OnThemeChoiceChanged(WindowThemeChoice value)
    {
        ApplyTheme();
        Persist(preferences => preferences with { Theme = ToUserTheme(value) });
    }

    partial void OnSelectedGroupChanged(SettingsGroup value)
    {
        OnPropertyChanged(nameof(IsDisplaySelected));
        OnPropertyChanged(nameof(IsKeyboardShortcutsSelected));
        OnPropertyChanged(nameof(IsParsingSelected));
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

    partial void OnShortcutSearchChanged(string value)
    {
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

    partial void OnIsUsingEstimateChanged(bool value) => OnPropertyChanged(nameof(ShowUseEstimate));

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
        if (_workspace.Selection.StepLimitValidationMessage is not null || !IsNoStepLimit &&
            (StepLimit is null or <= 0 || decimal.Truncate(StepLimit.Value) != StepLimit.Value))
        {
            CancelParsingLimitEdit();
            _stepLimitRefusal = "The per-word step limit must be a positive whole number.";
            OnPropertyChanged(nameof(StepLimitRefusal));
            OnPropertyChanged(nameof(HasStepLimitRefusal));
            return WindowRefusal.Plain(_stepLimitRefusal);
        }
        if (!IsNoStepLimit && !IsUsingEstimate && (TimeLimitSeconds is null or <= 0))
        {
            CancelParsingLimitEdit();
            TimeLimitRefusal = "Enter a per-word time limit greater than zero.";
            OnPropertyChanged(nameof(HasTimeLimitRefusal));
            return WindowRefusal.Plain(TimeLimitRefusal);
        }

        var stepLimit = IsNoStepLimit ? StepCap.Unbounded
            : StepLimit is > 0 and var steps && decimal.Truncate(steps) == steps && steps <= long.MaxValue
                ? new StepCap(decimal.ToInt64(steps)) : StepCap.Default;
        SelectionParsingLimits limits;
        if (IsNoStepLimit || IsUsingEstimate)
        {
            limits = SelectionLimitPolicy.Estimated(stepLimit);
        }
        else if (TimeLimitSeconds is { } seconds && seconds > 0 &&
                 seconds * 1000m <= int.MaxValue && decimal.Round(seconds * 1000m) >= 1m)
        {
            limits = SelectionLimitPolicy.Explicit(stepLimit,
                decimal.ToInt32(decimal.Round(seconds * 1000m, 0, MidpointRounding.AwayFromZero)));
        }
        else
        {
            CancelParsingLimitEdit();
            TimeLimitRefusal = "Enter a per-word time limit from 0.001 seconds to 2,147,483.647 seconds.";
            OnPropertyChanged(nameof(HasTimeLimitRefusal));
            return WindowRefusal.Plain(TimeLimitRefusal);
        }
        var refusal = await _workspace.Context.Setup!.SaveParsingLimitsAsync(limits).ConfigureAwait(true);
        if (refusal is null)
        {
            TimeLimitRefusal = string.Empty;
            _stepLimitRefusal = string.Empty;
            RememberGoodValues();
        }
        else
        {
            CancelParsingLimitEdit();
            TimeLimitRefusal = refusal.Sentence;
        }
        OnPropertyChanged(nameof(HasTimeLimitRefusal));
        OnPropertyChanged(nameof(StepLimitRefusal));
        OnPropertyChanged(nameof(HasStepLimitRefusal));
        OnPropertyChanged(nameof(ParsingFooter));
        OnPropertyChanged(nameof(StepLimitEstimateText));
        OnPropertyChanged(nameof(ProjectName));
        return refusal;
    }

    public void MarkVersionsCopied()
    {
        CopyVersionsText = "Copied";
        _ = ResetCopiedLabelAsync();
    }

    public void UseMotifEstimate()
    {
        IsUsingEstimate = true;
        ApplyEstimate();
        OnPropertyChanged(nameof(ShowUseEstimate));
    }

    public void CancelParsingLimitEdit()
    {
        _timeLimitBeforeNoStep = null;
        _workspace.Selection.PerWordStepLimitUnbounded = _lastGoodNoStepLimit;
        _workspace.Selection.PerWordStepLimit = _lastGoodStepLimit;
        _workspace.Selection.PerWordTimeLimitSeconds = _lastGoodTimeLimitSeconds;
        _workspace.Context.Setup?.SyncParsingLimitsFromSelection();
        IsUsingEstimate = _workspace.Context.Setup?.CurrentSelectionLimits.TimeMode !=
            SelectionTimeLimitMode.Explicit;
        RememberGoodValues();
    }

    private void RememberGoodValues()
    {
        _lastGoodNoStepLimit = _workspace.Selection.PerWordStepLimitUnbounded;
        if (_workspace.Selection.StepLimitValidationMessage is null && _workspace.Selection.PerWordStepLimit is > 0)
            _lastGoodStepLimit = _workspace.Selection.PerWordStepLimit;
        if (_workspace.Selection.PerWordTimeLimitSeconds is > 0)
            _lastGoodTimeLimitSeconds = _workspace.Selection.PerWordTimeLimitSeconds;
        else _lastGoodTimeLimitSeconds = null;
        _stepLimitRefusal = string.Empty;
        TimeLimitRefusal = string.Empty;
        OnPropertyChanged(nameof(StepLimitRefusal));
        OnPropertyChanged(nameof(HasStepLimitRefusal));
        OnPropertyChanged(nameof(HasTimeLimitRefusal));
        OnPropertyChanged(nameof(HasUncommittedParsingLimitChanges));
    }

    private async Task ResetCopiedLabelAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        CopyVersionsText = "Copy versions";
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
        foreach (var option in GroupOptions)
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
            VernacularWritingSystems.Add(new WritingSystemSettingsRow(system,
                _workspace.Context.TextStyles));
        var vernacularIds = systems.Where(system => system.Kind == WritingSystemKind.Vernacular)
            .Select(system => system.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var system in systems.Where(system => system.Kind == WritingSystemKind.Analysis)
                     .OrderBy(system => system.Position))
            AnalysisWritingSystems.Add(new WritingSystemSettingsRow(system,
                _workspace.Context.TextStyles, vernacularIds.Contains(system.Id)));
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
        }
        finally
        {
            _suppressPreferenceWrites = false;
        }
        UpdatePreferenceProblem();
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
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
        SIL.Motif.App.Services.SettingsGroup.WritingSystems => SettingsGroup.WritingSystems,
        SIL.Motif.App.Services.SettingsGroup.Help => SettingsGroup.HelpAndReports,
        SIL.Motif.App.Services.SettingsGroup.About => SettingsGroup.About,
        _ => SettingsGroup.Display,
    };

    private static SIL.Motif.App.Services.SettingsGroup MapUserSettingsGroup(SettingsGroup group) => group switch
    {
        SettingsGroup.KeyboardShortcuts => SIL.Motif.App.Services.SettingsGroup.Shortcuts,
        SettingsGroup.Parsing => SIL.Motif.App.Services.SettingsGroup.Parsing,
        SettingsGroup.WritingSystems => SIL.Motif.App.Services.SettingsGroup.WritingSystems,
        SettingsGroup.HelpAndReports => SIL.Motif.App.Services.SettingsGroup.Help,
        SettingsGroup.About => SIL.Motif.App.Services.SettingsGroup.About,
        _ => SIL.Motif.App.Services.SettingsGroup.Display,
    };

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SelectionViewModel.PerWordStepLimit) or
            nameof(SelectionViewModel.PerWordStepLimitUnbounded))
        {
            _workspace.Context.Setup?.SyncParsingLimitsFromSelection();
            var accepted = _workspace.Context.Setup?.CurrentSelectionLimits.PerWordStepLimit;
            var current = _workspace.Selection.PerWordStepLimitUnbounded ? StepCap.Unbounded
                : _workspace.Selection.PerWordStepLimit is { } steps && steps is > 0 and <= long.MaxValue &&
                  decimal.Truncate(steps) == steps ? new StepCap(decimal.ToInt64(steps)) : null;
            if ((accepted is null || accepted != current) && IsUsingEstimate)
            {
                ApplyEstimate();
            }
            OnPropertyChanged(nameof(StepLimit));
            OnPropertyChanged(nameof(IsNoStepLimit));
            OnPropertyChanged(nameof(StepLimitRefusal));
            OnPropertyChanged(nameof(HasStepLimitRefusal));
            OnPropertyChanged(nameof(IsTimeLimitEnabled));
            OnPropertyChanged(nameof(StepLimitEstimateText));
        }
        if (args.PropertyName == nameof(SelectionViewModel.PerWordTimeLimitSeconds))
        {
            OnPropertyChanged(nameof(TimeLimitSeconds));
            OnPropertyChanged(nameof(ShowUseEstimate));
        }
        OnPropertyChanged(nameof(HasUncommittedParsingLimitChanges));
        OnPropertyChanged(nameof(ParsingFooter));
    }

    private void OnSetupChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SetupViewModel.CanRunDefaultSelection) or nameof(SetupViewModel.ProjectPath))
        {
            OnPropertyChanged(nameof(HasSavedSelection));
            OnPropertyChanged(nameof(HasProjectWithoutSelection));
            OnPropertyChanged(nameof(IsTimeLimitEnabled));
        }
        if (args.PropertyName == nameof(SetupViewModel.CurrentSelectionLimits))
        {
            IsUsingEstimate = _workspace.Context.Setup?.CurrentSelectionLimits.TimeMode !=
                SelectionTimeLimitMode.Explicit;
            if (IsUsingEstimate) ApplyEstimate();
            else RememberGoodValues();
        }
        if (args.PropertyName is nameof(SetupViewModel.StepLimitEstimateText) or
            nameof(SetupViewModel.EstimatedPerWordLimitMs) or nameof(SetupViewModel.RunningStepLimitText))
        {
            OnPropertyChanged(nameof(StepLimitEstimateText));
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
        if (args.PropertyName == nameof(WorkspaceContext.Setup))
        {
            ObserveSetup(_workspace.Context.Setup);
            IsUsingEstimate = _observedSetup?.CurrentSelectionLimits.TimeMode != SelectionTimeLimitMode.Explicit;
            if (IsUsingEstimate) ApplyEstimate();
            else RememberGoodValues();
        }
        if (args.PropertyName is nameof(WorkspaceContext.ProjectPath) or nameof(WorkspaceContext.HasProject) or
            nameof(WorkspaceContext.ProjectName) or nameof(WorkspaceContext.Setup))
        {
            OnPropertyChanged(nameof(ProjectName));
            OnPropertyChanged(nameof(HasProject));
            OnPropertyChanged(nameof(HasSavedSelection));
            OnPropertyChanged(nameof(HasProjectWithoutSelection));
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
            foreach (var option in GroupOptions.Where(option => option.Group == SettingsGroup.Parsing))
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

}
