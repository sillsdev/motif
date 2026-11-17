using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Stored health facts and the shared Diagnostics choice for the window's health strip.</summary>
public sealed partial class HealthStripViewModel : ObservableObject
{
    private readonly WorkspaceContext _context;

    public HealthStripViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        context.PropertyChanged += OnContextChanged;
        context.Evidence.PropertyChanged += OnEvidenceChanged;
        TurnOnDiagnosticsCommand = new RelayCommand(() => context.Diagnostics = true);
    }

    /// <summary>The last available stored Overview, or null while it is unavailable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarningText), nameof(BarText), nameof(CompactText), nameof(HasWarnings), nameof(MedianWorkText), nameof(HasMedianWork), nameof(AccessibleName), nameof(HasSelectionSummary), nameof(SelectionSummary))]
    private OverviewResponse? _overview;

    partial void OnOverviewChanged(OverviewResponse? value)
    {
        void Refresh()
        {
            if (ReferenceEquals(Overview, value) && (value is not null || _context.ProjectPath is null))
                _context.TextStyles.ReplaceContextIfChanged(value?.WritingSystems ?? []);
        }
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    internal void ClearProject()
    {
        Overview = null;
        OnOverviewChanged(null);
    }

    /// <summary>The health face's accessible name, including its available facts.</summary>
    public string AccessibleName => string.Join(", ", new[] { MotifAppStrings.Health, WarningText }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// The findings by level in the Diagnostics summary's words, absent when no count is recorded. The Overview's
    /// total counts every level, so showing it as "warnings" overstated the page's errors and warnings.
    /// </summary>
    public string WarningText => !_context.Evidence.IsStale && Overview is { IsStale: false, Warnings: { Count: { } count } warnings }
        ? warnings is { ErrorCount: { } errors, WarningCount: { } warned, InformationCount: { } information }
            ? DiagnosticFindingText.Breakdown(errors, warned, information)
            : PluralText.Format(MotifAppStrings.WarningsFindingsPattern, count)
        : string.Empty;

    /// <summary>
    /// The status bar's short form of <see cref="WarningText"/>: errors and warnings, which are the ones that ask
    /// something of a person. The information count is in the popup and on the Diagnostics page.
    /// </summary>
    public string BarText => !_context.Evidence.IsStale && Overview is { IsStale: false, Warnings: { Count: { } count } warnings }
        ? warnings is { ErrorCount: { } errors, WarningCount: { } warned }
            ? DiagnosticFindingText.Attention(errors, warned)
            : PluralText.Format(MotifAppStrings.WarningsFindingsPattern, count)
        : string.Empty;

    /// <summary>
    /// The count a narrow status bar keeps beside "Health": just the errors, or the findings total when the
    /// Overview records no per-level counts.
    /// </summary>
    public string CompactText => !_context.Evidence.IsStale && Overview is { IsStale: false, Warnings: { Count: { } count } warnings }
        ? warnings is { ErrorCount: { } errors }
            ? PluralText.Format(MotifAppStrings.WarningsErrorsPattern, errors)
            : PluralText.Format(MotifAppStrings.WarningsFindingsPattern, count)
        : string.Empty;

    /// <summary>Whether a warning count is available.</summary>
    public bool HasWarnings => !_context.Evidence.IsStale && Overview is { IsStale: false, Warnings.Count: not null };

    /// <summary>The actual recorded per-word median, shown in the health popup only.</summary>
    public string MedianWorkText => !_context.Evidence.IsStale && Overview is { IsStale: false, Timing.Work.MedianParserWork: { } median }
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.HealthMedianWorkFormat, median.ToString("N2", CultureInfo.CurrentCulture))
        : string.Empty;

    /// <summary>Whether a per-word median is recorded.</summary>
    public bool HasMedianWork => !_context.Evidence.IsStale && Overview is { IsStale: false, Timing.Work.MedianParserWork: not null };

    /// <summary>Whether the stored Overview resolved the saved Default Selection.</summary>
    public bool HasSelectionSummary => !_context.Evidence.IsStale && Overview is { IsStale: false, SelectionResolved: true };

    /// <summary>The number of Texts and words in the resolved Default Selection.</summary>
    public string SelectionSummary => HasSelectionSummary && Overview is { } overview
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProgressSelectionFormat,
            PluralText.Format(MotifAppStrings.ProjectCardTextsCountPattern, overview.SelectionTextCount),
            PluralText.Format(MotifAppStrings.WordCountPattern, overview.SelectionWordCount)) : string.Empty;

    /// <summary>Whether Diagnostics can be turned on.</summary>
    public bool CanTurnOnDiagnostics => !_context.Diagnostics;

    /// <summary>Enables the shared and persisted Diagnostics choice.</summary>
    public IRelayCommand TurnOnDiagnosticsCommand { get; }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.Diagnostics)) OnPropertyChanged(nameof(CanTurnOnDiagnostics));
    }

    private void OnEvidenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectEvidence.ParseTimeAssessmentId) or
            nameof(ProjectEvidence.TimingOverrideAssessmentIds)) Overview = null;
        if (e.PropertyName != nameof(ProjectEvidence.IsStale)) return;
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(BarText));
        OnPropertyChanged(nameof(CompactText));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(MedianWorkText));
        OnPropertyChanged(nameof(HasMedianWork));
        OnPropertyChanged(nameof(AccessibleName));
        OnPropertyChanged(nameof(HasSelectionSummary));
        OnPropertyChanged(nameof(SelectionSummary));
    }
}
