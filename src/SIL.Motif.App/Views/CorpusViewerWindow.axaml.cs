using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Corpus;

namespace SIL.Motif.App.Views;

/// <summary>Shows project Corpus Documents with interim Wordform boundaries.</summary>
public sealed partial class CorpusViewerWindow : Window
{
    private readonly string _project;
    private CorpusViewerDocument? _document;
    private bool _ready;
    private readonly CorpusDocumentSelection _selection = new();
    private ListBox CorpusListControl => this.FindControl<ListBox>("Corpora")!;
    private ListBox DocumentListControl => this.FindControl<ListBox>("Documents")!;
    private ListBox WordformListControl => this.FindControl<ListBox>("Wordforms")!;
    private ListBox ContextListControl => this.FindControl<ListBox>("Contexts")!;
    private NativeWebView WebViewerControl => this.FindControl<NativeWebView>("Viewer")!;
    private TextBlock ErrorControl => this.FindControl<TextBlock>("Error")!;


    /// <summary>Opens the Corpus viewer for an already selected project.</summary>
    public CorpusViewerWindow(string project)
    {
        _project = project;
        AvaloniaXamlLoader.Load(this);
        var result = CorpusCommands.ListCorpora(new ListCorporaRequest(project, MotifProductVersion.CurrentText));
        if (result.Succeeded) CorpusListControl.ItemsSource = result.Value!.Corpora.Select(c => new CorpusChoice(c)).ToArray();
        else ErrorControl.Text = result.Refusal!.Message;
        WebViewerControl.NavigationCompleted += async (_, e) =>
        {
            _ready = e.IsSuccess;
            if (_ready) await DisplayDocumentAsync();
        };
        Opened += (_, _) =>
        {
            using var stream = typeof(CorpusViewerWindow).Assembly.GetManifestResourceStream("Motif.CorpusViewer.js")!;
            using var reader = new StreamReader(stream);
            var bundle = reader.ReadToEnd();
            WebViewerControl.NavigateToString("<!doctype html><html><head><meta charset=\"utf-8\"><style>.corpus-word{text-decoration:underline}</style></head>" +
                "<body><div id=\"document\"></div><script>" + bundle + "</script></body></html>",
                new Uri("https://localhost/"));
        };
    }

    private async void OnCorpusSelected(object? sender, SelectionChangedEventArgs e)
    {
        DocumentListControl.ItemsSource = null;
        WordformListControl.ItemsSource = null;
        ContextListControl.ItemsSource = null;
        _document = null;
        await _selection.SwitchAsync(
            async () => { if (_ready) await WebViewerControl.InvokeScript("window.clearCorpusDocument() "); },
            () => CorpusListControl.SelectedItem is CorpusChoice corpus
                ? CorpusCommands.ShowCorpus(new ShowCorpusRequest(_project, MotifProductVersion.CurrentText, corpus.Value.CorpusId)) : null,
            result =>
            {
                if (result is null) return;
                if (result.Succeeded) DocumentListControl.ItemsSource = result.Value!.Documents.Select(d => new DocumentChoice(d)).ToArray();
                else ErrorControl.Text = result.Refusal!.Message;
            });
    }

    private async void OnDocumentSelected(object? sender, SelectionChangedEventArgs e)
    {
        WordformListControl.ItemsSource = null;
        ContextListControl.ItemsSource = null;
        _document = null;
        await _selection.SwitchAsync(
            async () => { if (_ready) await WebViewerControl.InvokeScript("window.clearCorpusDocument() "); },
            () => CorpusListControl.SelectedItem is CorpusChoice corpus && DocumentListControl.SelectedItem is DocumentChoice document
                ? CorpusCommands.OpenDocument(new OpenCorpusDocumentRequest(_project, MotifProductVersion.CurrentText,
                    corpus.Value.CorpusId, document.Value.DocumentId)) : null,
            result =>
            {
                if (result is null) return;
                if (!result.Succeeded) { ErrorControl.Text = result.Refusal!.Message; return; }
                ErrorControl.Text = "";
                _document = result.Value;
                WordformListControl.ItemsSource = _document!.Wordforms.Select(w => new WordChoice(w)).ToArray();
            });
        await DisplayDocumentAsync();
    }

    private async Task DisplayDocumentAsync()
    {
        if (!_ready || _document is null) return;
        var json = JsonSerializer.Serialize(new { text = _document.Text, sourceName = MotifAppStrings.CorpusSource, phrases = MotifAppStrings.CorpusEditorPhrases,
            spans = _document.Boundaries.Select(s => new { utf16Start = s.Utf16Start, utf16Length = s.Utf16Length }) });
        await WebViewerControl.InvokeScript($"window.showCorpusDocument({json})");
    }

    private void OnWordformSelected(object? sender, SelectionChangedEventArgs e)
    {
        ContextListControl.ItemsSource = null;
        if (_document is null || WordformListControl.SelectedItem is not WordChoice word) return;
        var text = _document.Text;
        ContextListControl.ItemsSource = word.Value.Occurrences.Select(span =>
        {
            var start = Math.Max(0, span.Utf16Start - 60);
            var end = Math.Min(text.Length, span.Utf16Start + span.Utf16Length + 60);
            return new ContextChoice(span, text[start..end].Replace('\n', ' '));
        }).ToArray();
    }

    private async void OnContextSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ContextListControl.SelectedItem is not ContextChoice choice) return;
        await WebViewerControl.InvokeScript($"window.showCorpusOccurrence({choice.Span.Utf16Start},{choice.Span.Utf16Length})");
    }

    private sealed record CorpusChoice(CorpusListItem Value) { public override string ToString() => Value.CorpusId; }
    private sealed record DocumentChoice(CorpusDocumentView Value) { public override string ToString() => Value.Title; }
    private sealed record WordChoice(CorpusViewerWordform Value)
    { public override string ToString() => $"{Value.Wordform} ({Value.Frequency})"; }
    private sealed record ContextChoice(CorpusWordSpan Span, string Text) { public override string ToString() => Text; }
}
