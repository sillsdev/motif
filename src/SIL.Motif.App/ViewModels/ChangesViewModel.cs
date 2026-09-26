using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

/// <summary>The kinds of change a person can collect about a word's analyses, before any is written.</summary>
public static class ChangeKinds
{
    /// <summary>Approve the analysis the person selected from one word's reading list.</summary>
    public const string Approve = "approve";

    /// <summary>Reject the analysis, so FieldWorks never offers it as a guess.</summary>
    public const string Reject = "reject";

    /// <summary>Take back a human opinion, leaving the analysis a candidate again.</summary>
    public const string Candidate = "candidate";

    /// <summary>Mark the word's spelling as incorrect in FieldWorks.</summary>
    public const string IncorrectSpelling = "incorrect-spelling";

    /// <summary>Send the parser's reading to FieldWorks as a new candidate analysis.</summary>
    public const string AddCandidate = "add-candidate";

    /// <summary>The words a change of <paramref name="kind"/> is listed with.</summary>
    public static string LabelOf(string kind) => kind switch
    {
        Approve => "Approve",
        Reject => "Reject",
        Candidate => "Back to candidate",
        IncorrectSpelling => "Incorrect spelling",
        AddCandidate => "Add as candidate",
        _ => kind,
    };

}

/// <summary>
/// The observable view of pending changes and each change's current fit with the FieldWorks project.
/// </summary>
public sealed partial class ChangesViewModel : ObservableObject, IProjectStateParticipant
{
    private readonly ICommandClient _client;
    private readonly List<string> _collectionNotices = [];
    private int _projectGeneration;
    public ChangesViewModel(ICommandClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        RemoveCommand = new AsyncRelayCommand<ChangeViewModel>(RemoveAsync);
        Items.CollectionChanged += (_, _) => Raise();
    }

    public ObservableCollection<ChangeViewModel> Items { get; } = [];

    /// <summary>The project these changes belong to, or <see langword="null"/> before one is open.</summary>
    public string? ProjectPath { get; private set; }

    /// <summary>Loads the pending changes belonging to <paramref name="projectPath"/>.</summary>
    public async Task OpenProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ProjectPath = projectPath;
        Reset();
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>How many changes wait.</summary>
    public int Count => Items.Count;

    public IAsyncRelayCommand<ChangeViewModel> RemoveCommand { get; }

    public PendingChangesSnapshot Snapshot { get; private set; } = new(null, "none", [], []);

    public Refusal? LastRefusal { get; private set; }

    /// <summary>The last refusal in the window's words, with the command's own account under Details.</summary>
    public WindowRefusal? ShownRefusal => LastRefusal is { } refusal ? WindowRefusal.From(refusal) : null;

    /// <summary>Replacement and skipped-word results from the current collection action.</summary>
    public string? CollectionNotice => _collectionNotices.Count == 0
        ? null : string.Join(" ", _collectionNotices);

    public void BeginCollection()
    {
        _collectionNotices.Clear();
        OnPropertyChanged(nameof(CollectionNotice));
    }

    public bool HasError => LastRefusal is not null;

    public string? AssessmentId { get; set; }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (ProjectPath is not { } path) return;
        await ReloadAsync(path, _projectGeneration, cancellationToken).ConfigureAwait(true);
    }

    public async Task PutAsync(ChangeIntent change, CancellationToken cancellationToken = default)
    {
        if (ProjectPath is not { } path) throw new InvalidOperationException("Open a project before collecting changes.");
        await PutAsync(change, path, _projectGeneration, cancellationToken).ConfigureAwait(true);
    }

    private async Task PutAsync(
        ChangeIntent change, string path, int generation, CancellationToken cancellationToken)
    {
        var outcome = await _client.PutPendingChangeAsync(new PutPendingChangeRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision, change),
            cancellationToken).ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken);
    }

    public async Task RecheckAsync(CancellationToken cancellationToken = default)
    {
        if (ProjectPath is not { } path) return;
        var generation = _projectGeneration;
        var outcome = await _client.RecheckPendingChangesAsync(new RecheckPendingChangesRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision), cancellationToken)
            .ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken).ConfigureAwait(true);
    }

    public async Task AddAsync(string kind, CompareWordViewModel word,
        WorkspacePage originPage = WorkspacePage.Texts)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (ProjectPath is not { } path) throw new InvalidOperationException("Open a project before collecting changes.");
        var generation = _projectGeneration;
        var readings = kind == ChangeKinds.AddCandidate ? word.ReadingChoices :
            word.SelectedReading is { } selected ? [selected] : [];
        if (kind == ChangeKinds.IncorrectSpelling)
        {
            await AddOneAsync(kind, word, null, originPage, path, generation).ConfigureAwait(true);
            return;
        }
        Refusal? firstRefusal = null;
        foreach (var reading in readings)
        {
            await AddOneAsync(kind, word, reading, originPage, path, generation).ConfigureAwait(true);
            firstRefusal ??= LastRefusal;
        }
        if (!IsCurrentProject(path, generation)) return;
        if (firstRefusal is not null)
        {
            LastRefusal = firstRefusal;
            OnPropertyChanged(nameof(LastRefusal));
            OnPropertyChanged(nameof(ShownRefusal));
            OnPropertyChanged(nameof(HasError));
        }
    }

    public async Task AddFromTextAsync(string kind, ResultsTokenViewModel token, ResultsReadingViewModel? reading = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, kind, "", token.Form,
            AssessmentId, reading?.Analysis, DisplayReading: reading?.Text,
            ReadingIndex: reading?.Index)).ConfigureAwait(true);
    }

    /// <summary>Adds an Approve change for one stored analysis without a parser reading.</summary>
    /// <param name="word">The word form that owns the analysis.</param>
    /// <param name="storedAnalysisId">The canonical ID of the stored analysis.</param>
    /// <param name="displayReading">The analysis text shown in Review changes.</param>
    /// <param name="originPage">The page that collected this change.</param>
    public async Task ApproveStoredAnalysisAsync(string word, string storedAnalysisId,
        string displayReading, WorkspacePage originPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedAnalysisId);
        ArgumentNullException.ThrowIfNull(displayReading);
        await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, ChangeKinds.Approve, "", word,
            StoredAnalysisId: storedAnalysisId, DisplayReading: displayReading,
            OriginPage: originPage.ToString())).ConfigureAwait(true);
    }

    private async Task AddOneAsync(string kind, CompareWordViewModel word, CompareReadingChoice? choice,
        WorkspacePage originPage, string path, int generation)
    {
        if (!IsCurrentProject(path, generation)) return;
        await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, kind, "", word.Word,
                AssessmentId, choice?.Reading, DisplayReading: choice?.Label ?? word.FirstReading,
                ReadingIndex: choice?.Index, OriginPage: originPage.ToString()), path, generation,
            CancellationToken.None).ConfigureAwait(true);
    }

    private async Task RemoveAsync(ChangeViewModel? change)
    {
        if (change is null) return;
        if (ProjectPath is not { } path) return;
        var generation = _projectGeneration;
        var outcome = await _client.RemovePendingChangeAsync(new RemovePendingChangeRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision, change.ChangeId),
            CancellationToken.None).ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, CancellationToken.None);
    }

    private async Task ReloadAfterConflictAsync(
        Refusal conflict, string path, int generation, CancellationToken cancellationToken)
    {
        await ReloadAsync(path, generation, cancellationToken);
        if (!IsCurrentProject(path, generation)) return;
        if (LastRefusal is not null) return;
        LastRefusal = conflict;
        OnPropertyChanged(nameof(LastRefusal));
        OnPropertyChanged(nameof(ShownRefusal));
        OnPropertyChanged(nameof(HasError));
    }

    public void Reset()
    {
        _projectGeneration++;
        Items.Clear();
        Snapshot = new PendingChangesSnapshot(null, "none", [], []);
        LastRefusal = null;
        BeginCollection();
        AssessmentId = null;
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(LastRefusal));
        OnPropertyChanged(nameof(ShownRefusal));
        OnPropertyChanged(nameof(HasError));
        Raise();
    }

    void IProjectStateParticipant.ClearProject()
    {
        ProjectPath = null;
        Reset();
    }

    Task IProjectStateParticipant.OpenProjectAsync(string projectPath, CancellationToken cancellationToken) =>
        OpenProjectAsync(projectPath, cancellationToken);

    private async Task ReloadAsync(string path, int generation, CancellationToken cancellationToken)
    {
        var outcome = await _client.LoadPendingChangesAsync(new PendingChangesRequest(
            path, MotifProductVersion.CurrentText), cancellationToken).ConfigureAwait(true);
        Accept(outcome, path, generation);
    }

    private bool IsCurrentProject(string path, int generation) =>
        generation == _projectGeneration && string.Equals(path, ProjectPath, StringComparison.Ordinal);

    private void Accept(CommandOutcome<PendingChangesSnapshot> outcome, string path, int generation)
    {
        if (!IsCurrentProject(path, generation)) return;
        LastRefusal = outcome.Refusal;
        OnPropertyChanged(nameof(LastRefusal));
        OnPropertyChanged(nameof(ShownRefusal));
        OnPropertyChanged(nameof(HasError));
        if (outcome.Value is not { } snapshot) return;
        Snapshot = snapshot;
        OnPropertyChanged(nameof(Snapshot));
        if (snapshot.ReplacedChangeId is not null)
            AddCollectionNotice("Replaced an earlier pending change.");
        if (snapshot.CancelledChangeId is not null)
            AddCollectionNotice("Cancelled the pending choice.");
        if (snapshot.SkippedWord is not null)
            AddCollectionNotice("Skipped one word because a choice is already pending.");
        Items.Clear();
        foreach (var change in snapshot.Changes)
        {
            var fit = snapshot.FitSummary.FirstOrDefault(item => item.ChangeId == change.ChangeId);
            Items.Add(new ChangeViewModel(change.Kind, change.Word,
                change.DisplayReading ?? "", change.ChangeId, fit, change.Analyses, change.OriginPage));
        }
        Raise();
    }

    ProjectOpenStage IProjectStateParticipant.OpenStage => ProjectOpenStage.Independent;

    private void AddCollectionNotice(string notice)
    {
        if (!_collectionNotices.Contains(notice, StringComparer.Ordinal))
            _collectionNotices.Add(notice);
        OnPropertyChanged(nameof(CollectionNotice));
    }

    public bool HasItems => Items.Count > 0;

    /// <summary>How many changes wait, as the Texts page's strip says it.</summary>
    public string CountText => Items.Count == 1 ? "1 change not applied yet" : $"{Items.Count:N0} changes not applied yet";

    private void Raise()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CountText));
    }
}

/// <summary>One collected change: what should happen to one word, and what it held when the change was chosen.</summary>
public sealed class ChangeViewModel(string kind, string word, string reading,
    string? changeId = null, ChangeFit? fit = null, IReadOnlyList<ReviewAnalysis>? analyses = null,
    string? originPage = null)
{
    public WorkspacePage OriginPage { get; } = Enum.TryParse<WorkspacePage>(originPage, out var page) &&
        page != WorkspacePage.Review ? page : WorkspacePage.Texts;
    public string ChangeId { get; } = changeId ?? CanonicalId.Mint().Value;
    public ChangeFit? Fit { get; } = fit;
    public string FitStatus => Fit is null ? string.Empty : Fit.StillFits
        ? "Still fits the project." : "No longer fits the current project. Remove this change before review.";
    public bool IsNoLongerFits => Fit is { StillFits: false };
    public string Kind { get; } = kind;
    public string Label { get; } = ChangeKinds.LabelOf(kind);
    public string ReviewLabel => kind == ChangeKinds.Approve && analyses is { Count: > 1 }
        ? $"Approve 1 of {analyses.Count} analyses" : Label;
    public string Word { get; } = word;

    /// <summary>The parser's reading, for a change that sends it to FieldWorks or judges it.</summary>
    public string Reading { get; } = reading;

    /// <summary>The morphs and glosses of each reading, with the chosen reading marked.</summary>
    public IReadOnlyList<ReviewAnalysisViewModel> Analyses { get; } = analyses?
        .Select(analysis => new ReviewAnalysisViewModel(analysis, kind)).ToArray() ?? [];

    public bool HasAnalyses => Analyses.Count > 0;


    public string Summary => $"{Word}: {Label}";
}

/// <summary>A reading as the Review page displays its morphs, prior opinion and proposed opinion.</summary>
public sealed class ReviewAnalysisViewModel
{
    public ReviewAnalysisViewModel(ReviewAnalysis analysis, string changeKind)
    {
        Morphs = analysis.Reading.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Touched = analysis.Touched;
        ParserBuilt = !analysis.Stored;
        Opinion = analysis.Touched ? changeKind switch
        {
            ChangeKinds.Approve => ReadingGradeLabels.Of(ReadingGrade.Approved),
            ChangeKinds.Reject => ReadingGradeLabels.Of(ReadingGrade.Disapproved),
            ChangeKinds.Candidate or ChangeKinds.AddCandidate => ReadingGradeLabels.Of(ReadingGrade.Candidate),
            _ => analysis.Opinion,
        } : analysis.Opinion is ReadingGrade.Approved or ReadingGrade.Disapproved or ReadingGrade.Candidate
            ? ReadingGradeLabels.Of(analysis.Opinion)
            : "Not stored yet";
    }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public bool Touched { get; }
    public bool ParserBuilt { get; }
    public string Opinion { get; }
    public string Source => ParserBuilt ? "Parser reading" : "Stored analysis";
}
