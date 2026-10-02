using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.ViewModels;

public sealed partial class TraceWordViewModel
{
    private ITraceViewPreferences? _viewPreferences;
    private IReadOnlyList<TraceStepViewModel> _expertTree = [];

    [ObservableProperty]
    private bool _isExpert;

    [ObservableProperty]
    private bool _expertWholeTree = true;

    public IRelayCommand<TraceStepViewModel> SelectExpertStepCommand { get; private set; } = null!;

    public IRelayCommand<bool> SetExpertCommand { get; private set; } = null!;

    private void InitializeExpert(ITraceViewPreferences? preferences)
    {
        IsExpert = preferences?.IsExpert == true;
        _viewPreferences = preferences;
        SelectExpertStepCommand = new RelayCommand<TraceStepViewModel>(step => SelectedStep = step);
        SetExpertCommand = new RelayCommand<bool>(value => IsExpert = value);
    }

    public IReadOnlyList<TraceCandidateViewModel> ExpertAttempts => Candidates;

    public bool ShowsPlainTrace => HasResult && !IsExpert;
    public bool ShowsExpertTrace => HasResult && IsExpert;

    partial void OnIsExpertChanged(bool value)
    {
        NotifyExpertVisibility();
        if (_viewPreferences is not null) _viewPreferences.IsExpert = value;
    }

    partial void OnExpertWholeTreeChanged(bool value) => NotifyExpertRows();
    partial void OnSelectedCandidateChanged(TraceCandidateViewModel? value)
    {
        OnPropertyChanged(nameof(PlainSteps));
        OnPropertyChanged(nameof(PlainFoldText));
        NotifyExpertRows();
    }
    partial void OnSelectedStepChanged(TraceStepViewModel? value)
    {
        if (value is not null) value.IsSelected = true;
        foreach (var step in _expertTree.Concat(Candidates.SelectMany(candidate => candidate.Steps)))
            step.IsSelected = value?.RecordedStep.StepId == step.RecordedStep.StepId;
        OnPropertyChanged(nameof(PlainSteps));
        OnPropertyChanged(nameof(PlainFoldText));
        OnPropertyChanged(nameof(PlainSelectedStep));
        OnPropertyChanged(nameof(ExpertReadableText));
        OnPropertyChanged(nameof(ExpertRawRecord));
        OnPropertyChanged(nameof(ExpertEnvironmentTokens));
        OnPropertyChanged(nameof(HasExpertEnvironment));
    }

    private void RebuildExpert()
    {
        _expertTree = Roots.SelectMany(Flatten).ToArray();
        OnPropertyChanged(nameof(PlainSteps));
        OnPropertyChanged(nameof(PlainFoldText));
        OnPropertyChanged(nameof(PlainSelectedStep));
        OnPropertyChanged(nameof(ExpertAttempts));
        NotifyExpertVisibility();
        NotifyExpertRows();
    }

    private void NotifyExpertVisibility()
    {
        OnPropertyChanged(nameof(ShowsPlainTrace));
        OnPropertyChanged(nameof(ShowsExpertTrace));
    }

    private static IEnumerable<TraceStepViewModel> Flatten(TraceStepViewModel step)
    {
        yield return step;
        foreach (var child in step.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }

    private void NotifyExpertRows()
    {
        OnPropertyChanged(nameof(ExpertEvents));
        OnPropertyChanged(nameof(ExpertPhonologicalEvents));
        OnPropertyChanged(nameof(ExpertScopeText));
        OnPropertyChanged(nameof(HasExpertPhonologicalEvents));
    }

    /// <summary>Compact recorded events; context-only levels and lookups stay available in Expert.</summary>
    public IReadOnlyList<TraceStepViewModel> PlainSteps
    {
        get
        {
            var attempt = SelectedCandidate ?? Candidates.FirstOrDefault(candidate => candidate.Succeeded)
                ?? ClosestAttempts.FirstOrDefault();
            var path = attempt?.Steps ?? _expertTree;
            var addresses = SIL.Motif.Commands.Queries.TraceBuildingStory.Steps(path.Select(step => step.RecordedStep).ToArray())
                .Select(step => step.StepId).ToHashSet(StringComparer.Ordinal);
            var building = path.Where(step => addresses.Contains(step.RecordedStep.StepId)).ToList();
            if (building.Count == 0) building.AddRange(path.Where(step => step.KindText is not ("Rule level" or "Word")));
            foreach (var step in building) step.IsSeparatePlainEvent = false;
            if (SelectedStep is { } selected && building.All(step => step.RecordedStep.StepId != selected.RecordedStep.StepId))
            {
                selected.IsSeparatePlainEvent = true;
                building.Add(selected);
            }
            return building;
        }
    }

    /// <summary>The measured number of additional recorded events available in Expert.</summary>
    public string PlainFoldText
    {
        get
        {
            var shown = PlainSteps.Select(step => step.RecordedStep.StepId).ToHashSet(StringComparer.Ordinal);
            var count = _expertTree.Count(step => step.Type != "WordAnalysis" && !shown.Contains(step.RecordedStep.StepId));
            return count > 0 ? $"{count:N0} more rule steps · open in Expert" : string.Empty;
        }
    }

    /// <summary>The Plain row for the selected recorded event, including events selected in Expert.</summary>
    public TraceStepViewModel? PlainSelectedStep
    {
        get => PlainSteps.FirstOrDefault(step => step.RecordedStep.StepId == SelectedStep?.RecordedStep.StepId);
        set
        {
            // A filtered Plain list must not clear Expert's selected event.
            if (value is not null && value.RecordedStep.StepId != SelectedStep?.RecordedStep.StepId)
                SelectedStep = value;
        }
    }

    /// <summary>Recorded events in parser traversal order; lineage uses only the returned ancestor path.</summary>
    public IReadOnlyList<TraceStepViewModel> ExpertEvents => ExpertWholeTree ? _expertTree : SelectedCandidate?.Steps ?? [];

    public IReadOnlyList<TraceStepViewModel> ExpertPhonologicalEvents => ExpertEvents
        .Where(step => step.Type.StartsWith("PhonologicalRule", StringComparison.Ordinal)).ToArray();

    public bool HasExpertPhonologicalEvents => ExpertPhonologicalEvents.Count > 0;
    public bool HasExpertEnvironment => ExpertEnvironmentTokens.Count > 0;

    public string ExpertScopeText => ExpertWholeTree
        ? "Showing every step"
        : SelectedCandidate is null ? string.Empty
        : $"Showing attempt {SelectedCandidate.AttemptId}'s recorded path.";

    public string ExpertReadableText => SelectedStep is not { } step ? "Choose a recorded event."
        : $"{step.KindText}{(step.Source is { Length: > 0 } source ? $" ({source})" : string.Empty)}: {step.RecordedOutcomeText}. {step.RecordedExplanationText}";

    /// <summary>The selected producer record, preserving unknown fields without duplicating its descendants.</summary>
    public string ExpertRawRecord
    {
        get
        {
            var json = Result?.DiagnosticJson;
            if (SelectedStep is not { } step || string.IsNullOrWhiteSpace(json)) return "Parser record not available";
            var node = JsonNode.Parse(json)?["trace"];
            foreach (var index in step.RecordedStep.StepId.Split('.').Skip(1))
                node = node?["children"]?[int.Parse(index, System.Globalization.CultureInfo.InvariantCulture)];
            if (node is not JsonObject record) return "Parser record not available";
            var copy = new JsonObject(record.Where(pair => pair.Key != "children")
                .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
            return copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>Recorded operands remain opaque because the trace does not establish authored environment text.</summary>
    public IReadOnlyList<EnvironmentToken> ExpertEnvironmentTokens
    {
        get
        {
            var environment = SelectedStep?.RecordedStep.FailureEvidence?.Environment ?? SelectedStep?.FailureEnvironment;
            return environment is { Length: > 0 }
                ? [new EnvironmentToken(environment, "Authored environment notation unavailable", true)] : [];
        }
    }
}
