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
    [NotifyPropertyChangedFor(nameof(WarningText), nameof(HasWarnings), nameof(MedianWorkText), nameof(HasMedianWork), nameof(AccessibleName), nameof(HasSelectionSummary), nameof(SelectionSummary))]
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

    /// <summary>Whether the assistant is connected, or null when connection status is unavailable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAssistantState), nameof(AssistantStateText), nameof(AccessibleName))]
    private bool? _assistantConnected;

    /// <summary>Whether the connection status is known.</summary>
    public bool HasAssistantState => AssistantConnected is not null;

    /// <summary>The known connection status.</summary>
    public string AssistantStateText => AssistantConnected switch
    {
        true => MotifAppStrings.HealthAssistantConnected,
        false => MotifAppStrings.HealthAssistantDisconnected,
        null => string.Empty,
    };

    /// <summary>The health face's accessible name, including its available facts.</summary>
    public string AccessibleName => string.Join(", ", new[] { MotifAppStrings.Health, WarningText, AssistantStateText }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>The warning count, absent when no count is recorded.</summary>
    public string WarningText => !_context.Evidence.IsStale && Overview is { IsStale: false, Warnings.Count: { } count }
        ? PluralText.Format(MotifAppStrings.HealthWarningsPattern, count) : string.Empty;

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
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(MedianWorkText));
        OnPropertyChanged(nameof(HasMedianWork));
        OnPropertyChanged(nameof(AccessibleName));
        OnPropertyChanged(nameof(HasSelectionSummary));
        OnPropertyChanged(nameof(SelectionSummary));
    }
}
