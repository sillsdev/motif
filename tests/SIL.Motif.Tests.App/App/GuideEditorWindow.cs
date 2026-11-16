using Avalonia.Controls;
using SIL.Motif.App.Compare;
using SIL.Motif.App.Views.Guide;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;
using SIL.Motif.App.Preview;
using SIL.Motif.Host.Gdr;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A test window that holds a <see cref="GuideSectionEditor"/> beside a list of its sections, as the Grammar Guide page
/// does, and answers reads at once so a test needs no waiting. It carries the editor's own surface through, so the editor
/// is tested as the page embeds it without a page model in the way.
/// </summary>
internal sealed class GuideEditorWindow : Window
{
    private readonly GuideSectionEditor _editor;
    private readonly ListBox _list;

    public GuideEditorWindow(IReadOnlyList<GdrSummary> records, Func<string, GdrShowResponse?> show, IGdrEditStore? store = null,
        Func<Task<bool>>? confirmStartAgain = null, string? projectName = null, Func<CompareWindow, Task<CompareOutcome?>>? compare = null,
        Func<string, string>? writingSystemName = null, Func<string, BaselineToken?, CommandOutcome<GdrPathLink>>? linkToPath = null)
    {
        Width = 900;
        Height = 560;
        _editor = new GuideSectionEditor(records, id => Task.FromResult(show(id)), store, confirmStartAgain, projectName, compare,
            writingSystemName, linkToPath is null ? null : (path, baseline) => Task.FromResult(linkToPath(path, baseline)));
        _list = new ListBox { ItemsSource = records.Select(record => record.Id).ToArray(), Width = 160 };
        _list.SelectionChanged += (_, _) =>
        {
            if (!_selecting && _list.SelectedItem is string id) _editor.Show(id);
        };
        _editor.SelectionRefused += (_, id) => SelectQuietly(id);
        var dock = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        dock.Children.Add(_list);
        dock.Children.Add(_editor);
        Content = dock;
    }

    private bool _selecting;

    private void SelectQuietly(string? id)
    {
        _selecting = true;
        try { _list.SelectedItem = id; }
        finally { _selecting = false; }
    }

    public GuideSectionEditor Editor => _editor;
    public RichTextField? Field => _editor.Field;
    public GdrDiscussionSession? Session => _editor.Session;
    public IReadOnlyDictionary<string, GdrDiscussionEdit> PendingEdits => _editor.PendingEdits;
    public event EventHandler<GdrDiscussionEdit?> PendingEditChanged
    {
        add => _editor.PendingEditChanged += value;
        remove => _editor.PendingEditChanged -= value;
    }
    public string StatusText => _editor.StatusText;
    public string HeadingText => _editor.HeadingText;
    public bool IsStaleStripShown => _editor.IsStaleStripShown;
    public bool IsUnappliedStripShown => _editor.IsUnappliedStripShown;
    public Task Loading => _editor.Loading;
    public Task Showing => _editor.Showing;
    public string TargetText => _editor.TargetText;
    public string MarkdownText { get => _editor.MarkdownText; set => _editor.MarkdownText = value; }
    public bool IsMarkdownOpen => _editor.IsMarkdownOpen;
    public IReadOnlyList<string> ParagraphStyleChoices => _editor.ParagraphStyleChoices;
    public IReadOnlyList<string> CharacterStyleChoices => _editor.CharacterStyleChoices;
    public IReadOnlyList<string> WritingSystemChoices => _editor.WritingSystemChoices;
    public IReadOnlyList<string> LinkChoices => _editor.LinkChoices;
    public string LinkPath { get => _editor.LinkPath; set => _editor.LinkPath = value; }
    public string LinkPathHint => _editor.LinkPathHint;
    public Task LinkPathResolving => _editor.LinkPathResolving;

    public void Show(string id)
    {
        SelectQuietly(id);
        _editor.Show(id);
    }

    public void Show(GdrShowResponse response) => _editor.Show(response);
    public Task<bool> ApplyAsync() => _editor.ApplyAsync();
    public Task<bool> CompareAsync() => _editor.CompareAsync();
    public Task<bool> StartAgainFromFieldWorksAsync() => _editor.StartAgainFromFieldWorksAsync();
    public bool ApplyParagraphStyle(string style) => _editor.ApplyParagraphStyle(style);
    public bool ApplyCharacterStyle(string? style) => _editor.ApplyCharacterStyle(style);
    public bool ToggleSourceMode() => _editor.ToggleSourceMode();
    public bool ToggleBold() => _editor.ToggleBold();
    public bool ToggleItalic() => _editor.ToggleItalic();
    public bool ToggleUnderline() => _editor.ToggleUnderline();
    public bool SetWritingSystem(string tag) => _editor.SetWritingSystem(tag);
    public bool InsertLink(string recordId) => _editor.InsertLink(recordId);
    public Task<bool> InsertLinkToPathAsync(string path) => _editor.InsertLinkToPathAsync(path);
    public bool ShowTarget(ContextInfo place) => _editor.ShowTarget(place);
    public Task<bool> OpenMarkdownAsync() => _editor.OpenMarkdownAsync();
    public Task<bool> SaveMarkdownAsync() => _editor.SaveMarkdownAsync();
    public Task<bool> UndoMarkdownAsync() => _editor.UndoMarkdownAsync();
    public bool BackToFields() => _editor.BackToFields();
    public void DiscardMarkdown() => _editor.DiscardMarkdown();
    public Task<bool> ReloadMarkdownAsync() => _editor.ReloadMarkdownAsync();
}
