using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.GuideQueries;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The query tables of one Guide section: a block for each <c>motif-query</c> in its Discussion, run as the section is shown
/// in a <see cref="GuideQueryContext"/>. Reading never saves a row; a block's Save stages only its query text.
/// </summary>
public sealed partial class GuideQueryTablesViewModel : ObservableObject
{
    private readonly GuideQueryContext _context;
    private readonly Func<string?> _projectPath;
    private readonly Func<GuideQueryRunRequest, CancellationToken, Task<CommandOutcome<FormsQueryResponse>>> _run;
    private readonly Action<string>? _openWord;
    private CancellationTokenSource? _cancellation;
    private int _generation;
    private string[] _signatures = [];

    public GuideQueryTablesViewModel(GuideQueryContext context, Func<string?> projectPath,
        Func<GuideQueryRunRequest, CancellationToken, Task<CommandOutcome<FormsQueryResponse>>> run, Action<string>? openWord)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(run);
        _context = context;
        _projectPath = projectPath;
        _run = run;
        _openWord = openWord;
    }

    /// <summary>Stages a query's new body in the section's pending changes (query id, body); returns why not, or null.</summary>
    public Func<string, string, Task<string?>>? SaveBody { get; set; }

    /// <summary>What a block says when it cannot be saved here, or null for the Guide's own wording.</summary>
    public string? SaveUnavailableReason { get; set; }

    /// <summary>The blocks of the section shown, in document order.</summary>
    public ObservableCollection<GuideQueryBlockViewModel> Blocks { get; } = [];

    /// <summary>What was found wrong with the section's blocks, one per line; empty when nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems), nameof(IsShown))]
    private string _problemsText = "";

    /// <summary>Whether <see cref="ProblemsText"/> says anything.</summary>
    public bool HasProblems => ProblemsText.Length > 0;

    /// <summary>Whether the section has any query block to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool _hasBlocks;

    /// <summary>Whether there is anything to show: a block, or a finding about the section's blocks.</summary>
    public bool IsShown => HasBlocks || HasProblems;

    /// <summary>The runs started by the last <see cref="Show"/>, so a caller can wait for them.</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>Shows the blocks of <paramref name="document"/> and runs each; null clears them.</summary>
    public void Show(GuideQueryDocument? document)
    {
        Clear();
        if (document is null || !document.HasBlocks) return;
        _signatures = Signatures(document);
        var blocks = document.Blocks;
        foreach (var query in blocks.Queries)
            Blocks.Add(new GuideQueryBlockViewModel(query.Id, query.Title, document.BodyOf(query.Span),
                blocks.Shows.Where(show => show.Query == query.Id).ToArray(), _context, _projectPath, _run, SaveBody is null ? null : Save, _openWord, query.Sort,
                SaveUnavailableReason));
        ProblemsText = string.Join("\n", blocks.Diagnostics.Select(item =>
            string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryProblemFormat, item.Line, item.Message)));
        HasBlocks = Blocks.Count > 0;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Running = Task.WhenAll(Blocks.ToArray().Select(block => block.RunAsync(cancellation.Token)));
    }

    /// <summary>
    /// Shows <paramref name="document"/> like <see cref="Show"/>, except that when its blocks and directives are the ones
    /// already shown, they run again where they are, so a person's unsaved edit, sort and filter stay.
    /// </summary>
    public void Refresh(GuideQueryDocument? document)
    {
        var signatures = Signatures(document);
        if (document is null || !document.HasBlocks || Blocks.Count == 0 || !signatures.SequenceEqual(_signatures))
        {
            Show(document);
            return;
        }
        _cancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Running = Task.WhenAll(Blocks.ToArray().Select(block => block.RunAsync(cancellation.Token)));
    }

    private static string[] Signatures(GuideQueryDocument? document) => document is null ? [] :
    [
        .. document.Blocks.Queries.Select(query => string.Join("\u001f", query.Id, query.Title, document.BodyOf(query.Span),
            string.Join("\u001e", document.Blocks.Shows.Where(show => show.Query == query.Id)
                .Select(show => $"{show.Show}/{show.Limit}/{string.Join(",", show.Columns ?? [])}")))),
    ];

    /// <summary>Drops the blocks and stops their runs.</summary>
    public void Clear()
    {
        _generation++;
        _cancellation?.Cancel();
        _cancellation = null;
        foreach (var block in Blocks) block.Stop();
        Blocks.Clear();
        _signatures = [];
        ProblemsText = "";
        HasBlocks = false;
        Running = Task.CompletedTask;
    }

    private Task<string?> Save(string id, string body) =>
        SaveBody?.Invoke(id, body) ?? Task.FromResult<string?>(MotifAppStrings.GuideQuerySaveUnavailable);
}
