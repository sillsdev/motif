using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.Commands.Catalog;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;
using TextDocument = SIL.TextEditing.Document;

namespace SIL.Motif.App.Preview;

/// <summary>
/// A developer-only window that shows SIL.TextEditing's rich text field with sample text. It exists to try the
/// library inside Motif; the sample writing systems are defined here and nothing in it reads a project.
/// </summary>
public sealed class TextEditorPreviewWindow : Window
{
    /// <summary>The start-up argument that opens this window when the developer flag is set.</summary>
    public const string StartupArgument = "--text-editor-preview";

    private static readonly (string Tag, string Role)[] SampleWritingSystems =
    [
        ("en", "analysis"),
        ("fr", "analysis"),
        ("ar", "vernacular"),
        ("swh", "vernacular"),
    ];

    private readonly TextBlock _caretLabel = new();

    /// <summary>Creates the window with its sample text.</summary>
    public TextEditorPreviewWindow()
    {
        Title = "Text editor preview (developer only)";
        Width = 720;
        Height = 480;

        Field = new RichTextField(CreateSampleDocument())
        {
            WritingSystems = CreateSampleWritingSystems(),
            ShowCaretAlways = true,
        };
        Field.Editor.ActiveWsChanged += (_, _) => ShowCaretWritingSystem();
        Field.Editor.CaretMoved += ShowCaretWritingSystem;
        ShowCaretWritingSystem();

        var intro = new TextBlock
        {
            Text = "Sample text in two analysis (en, fr) and two vernacular (ar, swh) writing systems. " +
                   "Sample data only; no project is open.",
            TextWrapping = TextWrapping.Wrap,
        };
        var border = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                Content = Field,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            },
        };
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(_caretLabel, Dock.Bottom);
        panel.Children.Add(intro);
        panel.Children.Add(_caretLabel);
        panel.Children.Add(border);
        Content = panel;
        Opened += (_, _) => Field.Focus();
    }

    /// <summary>The field the window shows.</summary>
    public RichTextField Field { get; }

    /// <summary>The text of the caret label, such as <c>Writing system at the caret: ar (vernacular)</c>.</summary>
    public string CaretLabelText => _caretLabel.Text ?? string.Empty;

    /// <summary>Whether <paramref name="developerFlag"/> and <paramref name="args"/> ask for this window.</summary>
    public static bool IsRequested(string? developerFlag, IEnumerable<string> args) =>
        CommandSurfacePolicy.FromEnvironment(developerFlag).DeveloperCommandsEnabled &&
        args.Contains(StartupArgument, StringComparer.Ordinal);

    /// <summary>Opens the window when <see cref="IsRequested"/> says so, and returns it, or <see langword="null"/>.</summary>
    public static TextEditorPreviewWindow? ShowIfRequested(IEnumerable<string> args)
    {
        if (!IsRequested(Environment.GetEnvironmentVariable(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable), args))
            return null;
        var window = new TextEditorPreviewWindow();
        window.Show();
        return window;
    }

    private void ShowCaretWritingSystem()
    {
        var tag = Field.Editor.ActiveWs;
        var role = Array.Find(SampleWritingSystems, ws => ws.Tag == tag).Role ?? "other";
        _caretLabel.Text = $"Writing system at the caret: {tag} ({role})";
    }

    private static WritingSystemRegistry CreateSampleWritingSystems() =>
        new WritingSystemRegistry()
            .Add(WritingSystemRegistry.Define("en", "Andika", 16))
            .Add(WritingSystemRegistry.Define("fr", "Andika", 16))
            .Add(WritingSystemRegistry.Define("ar", "Segoe UI", 18, rtl: true))
            .Add(WritingSystemRegistry.Define("swh", "Andika", 16));

    private static TextDocument CreateSampleDocument()
    {
        var document = new TextDocument();
        document.Paragraphs.Add(Sample("en", "en", "A word and its gloss: ", "swh", "kitabu", "en", " means book."));
        document.Paragraphs.Add(Sample("fr", "fr", "En français: ", "swh", "mtoto", "fr", " veut dire enfant."));
        document.Paragraphs.Add(Sample("swh", "swh", "Mixed: ", "ar", "كتاب جديد", "swh", " nyumba ", "ar", "مرحبا بالعالم", "en", " end"));
        return document;
    }

    // Arguments after the default writing system are (writing system, text) pairs, one per run.
    private static Paragraph Sample(string defaultWs, params string[] wsAndText)
    {
        var paragraph = new Paragraph { DefaultWs = defaultWs };
        for (var i = 0; i + 1 < wsAndText.Length; i += 2)
            paragraph.Runs.Add(new Run { Ws = wsAndText[i], Text = wsAndText[i + 1] });
        return paragraph;
    }
}
