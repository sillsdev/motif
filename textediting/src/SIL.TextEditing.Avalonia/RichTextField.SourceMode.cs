using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>Reported by <see cref="RichTextField.SourceModeChanged"/>: the field's mode, and a sentence for a screen reader to announce.</summary>
    public sealed class SourceModeChangedEventArgs : EventArgs
    {
        public SourceModeChangedEventArgs(bool isSourceMode, bool refused, string announcement)
        {
            IsSourceMode = isSourceMode;
            Refused = refused;
            Announcement = announcement;
        }

        /// <summary>True when the field is now in source mode.</summary>
        public bool IsSourceMode { get; }

        /// <summary>True when a return to rich mode was refused because the source has errors; the field stays in source mode.</summary>
        public bool Refused { get; }

        /// <summary>What happened, as a sentence: "Editing as Markdown", "Back to rich text", "The Markdown has 2 errors …".</summary>
        public string Announcement { get; }
    }

    // Source mode (ADR 0035): one field shown and edited as its source form (FieldWorks Markdown), in a second RichTextField that the field holds in place
    // of its own text, under a strip that says so. The source view's undo history is its own and lives until the return; what reaches the host is one undo
    // step made by the editor's ReplaceParagraphs, which prepares the paragraphs as paste does. While a source view is up the field's own input handlers do
    // nothing: the view's events bubble through the field, and the field must not edit its rich text from them.
    public partial class RichTextField
    {
        const string SourceWs = "source";
        const string SourceFont = "Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Courier New";

        ISourceForm? _sourceForm;
        SourceSession? _source;

        sealed class SourceSession
        {
            public RichTextField View = null!;
            public DockPanel Panel = null!;
            public TextBlock Status = null!;
            public string Original = string.Empty;
            public bool SingleParagraph;
            public bool WasTabStop;
            public bool WasFocusable;
            public readonly SimpleUndoStack Undo = new SimpleUndoStack();
        }

        /// <summary>The form the field can be edited in instead of rich text (ADR 0035): <c>FieldWorksMarkdownSourceForm</c>, given the host's Markdown options.
        /// Null, the default, offers no source mode (Ctrl+Shift+M does nothing). Changing it while the field is in source mode throws away the source.</summary>
        public ISourceForm? SourceForm
        {
            get => _sourceForm;
            set
            {
                if (ReferenceEquals(_sourceForm, value)) return;
                if (_source != null) DiscardSourceChanges();
                _sourceForm = value;
            }
        }

        /// <summary>True while the field shows its source form.</summary>
        public bool IsSourceMode => _source != null;

        /// <summary>The second field that shows the source while the field is in source mode, or null. A host reads its findings (<see cref="LanguageDiagnostics"/>)
        /// from it; it edits only its own text, never the host's.</summary>
        public RichTextField? SourceView => _source?.View;

        /// <summary>Raised on the field's thread when the field enters or leaves source mode, and when a return is refused, with a sentence a host gives its screen
        /// reader (Avalonia has no live region for the field to announce it itself).</summary>
        public event EventHandler<SourceModeChangedEventArgs>? SourceModeChanged;

        /// <summary>Shows the field as its source form and gives the source view focus. False, and nothing changes, when there is no <see cref="SourceForm"/>, the
        /// field is already in source mode, it is read-only or holds text that may not be edited, or the form cannot write its text.</summary>
        public bool EnterSourceMode()
        {
            var form = _sourceForm;
            if (form == null || _source != null || Editor.IsReadOnly || !CanEditAll()) return false;
            CloseCompletion();
            Editor.CloseUndoGroup();
            var paragraphs = Editor.Document.Paragraphs;
            bool single = Editor.SingleParagraph && paragraphs.Count == 1;
            var text = form.Write(paragraphs, single);
            if (text == null) return false;
            var session = new SourceSession { Original = text, SingleParagraph = single };
            session.View = CreateSourceView(text, form, single);
            session.Undo.Attach(session.View.Editor);
            session.Status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, IsVisible = false };
            session.Panel = BuildSourcePanel(form, session);
            // The field itself is not a place for focus while the view is up: Shift+Tab from the strip goes on to what comes before the field.
            session.WasTabStop = IsTabStop;
            session.WasFocusable = Focusable;
            _source = session;
            Editor.HeldForSource = true;
            LogicalChildren.Add(session.Panel);
            VisualChildren.Add(session.Panel);
            InvalidateMeasure();
            InvalidateVisual();
            session.View.Focus();
            IsTabStop = false;
            Focusable = false;
            RaiseSourceModeChanged(true, false, "Editing as " + form.Name);
            return true;
        }

        /// <summary>Returns the field to rich mode with what the source says. The source is read with the form's reader and checker; when it has an error the
        /// field stays in source mode, the findings stay marked, and the answer is false. Otherwise the field's paragraphs become what was read, as one undo step
        /// for the host ("Edit as Markdown"), the paragraphs the source left as they were untouched; a source that says what the field already holds commits
        /// nothing. Warnings never block. True when the field is in rich mode afterwards (also when it was not in source mode).</summary>
        public bool LeaveSourceMode()
        {
            var session = _source;
            var form = _sourceForm;
            if (session == null || form == null) return true;
            session.View.Editor.CloseUndoGroup();
            string text = session.View.Editor.Document.PlainText();
            if (text == session.Original)
            {
                CloseSource(session, focus: true);
                RaiseSourceModeChanged(false, false, "Back to rich text");
                return true;
            }
            SourceReading reading;
            try { reading = form.Read(text, session.SingleParagraph); }
            catch (Exception ex)
            {
                // A form that throws has not read the source: the person's Markdown stays, as for an error.
                return RefuseReturn(session, "The " + form.Name + " could not be read (" + ex.Message + "). Your " + form.Name + " is kept; throw it away to go back.");
            }
            if (reading.HasErrors)
            {
                int errors = reading.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                session.View.RestartLanguageAnalysis();
                return RefuseReturn(session, "The " + form.Name + " has " + errors + (errors == 1 ? " error" : " errors")
                    + ". Fix what is marked, or throw away your " + form.Name + " changes.");
            }
            // The host made the field read-only meanwhile: keep the person's Markdown rather than lose it on the way back.
            if (Editor.IsReadOnly || !CanEditAll())
                return RefuseReturn(session, "The field cannot be edited now. Copy your " + form.Name + ", or throw away your " + form.Name + " changes.");
            Editor.HeldForSource = false;
            Editor.CloseUndoGroup();
            try { Editor.ReplaceParagraphs(reading.Paragraphs, form.AreEquivalent, "Edit as " + form.Name); }
            catch (Exception ex)
            {
                Editor.HeldForSource = true;
                return RefuseReturn(session, "The " + form.Name + " could not be put back (" + ex.Message + "). Your " + form.Name + " is kept.");
            }
            CloseSource(session, focus: true);
            RaiseSourceModeChanged(false, false, "Back to rich text");
            return true;
        }

        bool RefuseReturn(SourceSession session, string message)
        {
            session.Status.Text = message;
            session.Status.Foreground = StatusBrush();
            session.Status.IsVisible = true;
            session.View.Focus();
            RaiseSourceModeChanged(true, true, message);
            return false;
        }

        /// <summary>The colour of the strip's error line: the system's window text in a high contrast theme, else the theme's error text colour, else a red
        /// that reads on the theme's page.</summary>
        IBrush StatusBrush() => SystemBrush(SystemColour.WindowText) ??
            ThemeBrush(ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark ? new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0xA4)) : new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)),
                "SystemFillColorCriticalBrush", "SystemControlErrorTextForegroundBrush", "SystemErrorTextColor");

        /// <summary>Throws the source away and returns the field to rich mode as it was before; the host receives nothing. Always offered (ADR 0035).</summary>
        public void DiscardSourceChanges()
        {
            var session = _source;
            if (session == null) return;
            CloseSource(session, focus: true);
            RaiseSourceModeChanged(false, false, "Back to rich text; the " + (_sourceForm?.Name ?? "source") + " changes were thrown away");
        }

        bool CanEditAll()
        {
            var paragraphs = Editor.Document.Paragraphs;
            if (paragraphs.Count == 0) return false;
            return Editor.CanEditRange(new TextPosition(0, 0), new TextPosition(paragraphs.Count - 1, paragraphs[paragraphs.Count - 1].Length));
        }

        void RaiseSourceModeChanged(bool source, bool refused, string announcement) =>
            SourceModeChanged?.Invoke(this, new SourceModeChangedEventArgs(source, refused, announcement));

        void CloseSource(SourceSession session, bool focus)
        {
            bool hadFocus = session.View.IsKeyboardFocusWithin || session.Panel.IsKeyboardFocusWithin;
            _source = null;
            Editor.HeldForSource = false;
            IsTabStop = session.WasTabStop;
            Focusable = session.WasFocusable;
            session.View.LanguageService = null;
            VisualChildren.Remove(session.Panel);
            LogicalChildren.Remove(session.Panel);
            InvalidateMeasure();
            InvalidateVisual();
            if (focus && (hadFocus || IsKeyboardFocusWithin)) Focus();
        }

        RichTextField CreateSourceView(string text, ISourceForm form, bool single)
        {
            var document = new Document { Normalization = Editor.Document.Normalization };
            foreach (var line in text.Split('\n'))
            {
                var paragraph = new Paragraph { DefaultWs = SourceWs };
                if (line.Length > 0) paragraph.Runs.Add(new Run { Ws = SourceWs, Text = line });
                document.Paragraphs.Add(paragraph);
            }
            double size = Editor.Document.Paragraphs.Count > 0 ? WritingSystems.Get(Editor.Document.Paragraphs[0].DefaultWs).Size : WritingSystemInfo.DefaultFallbackSize;
            var view = new RichTextField(document)
            {
                WritingSystems = new WritingSystemRegistry().Add(SourceWs, new WritingSystemInfo(SourceWs, SourceFont, size)),
                PreferredWidth = PreferredWidth,
                TokenStyles = new TokenStyleMap().Set(SourceTokenKinds.Muted, new TokenStyle(null, null, 0.55)),
            };
            if (_foreground != null) view.Foreground = _foreground;
            if (_background != null) view.Background = _background;
            view.LanguageIdlePost = LanguageIdlePost;
            view.LanguageService = new SourceFormLanguageService(form, single);
            AutomationProperties.SetName(view, form.Name + " source");
            view.UndoRequested += () => _source?.Undo.Undo();
            view.RedoRequested += () => _source?.Undo.Redo();
            view.LeaveRequested += (o, e) => LeaveRequested?.Invoke(this, e);
            // The view's context menu is the host's: forwarded from this field, about the view (its findings, its selection), so the host's Cut, Copy,
            // Paste and a finding's fix go to the view.
            view.ContextMenuRequested += (o, e) =>
            {
                var at = view.TranslatePoint(e.Position, this) ?? e.Position;
                ContextMenuRequested?.Invoke(this, new ContextMenuRequestedEventArgs(e.Trigger, at, e.Info) { Field = view });
            };
            view.AddHandler(KeyDownEvent, (o, e) =>
            {
                if (IsSourceToggle(e.Key, e.KeyModifiers)) { e.Handled = true; LeaveSourceMode(); }
            }, RoutingStrategies.Tunnel);
            return view;
        }

        DockPanel BuildSourcePanel(ISourceForm form, SourceSession session)
        {
            var label = new TextBlock { Text = "Editing as " + form.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 12, 0) };
            var back = StripButton("Back to rich text", () => LeaveSourceMode());
            var discard = StripButton("Throw away my " + form.Name + " changes", DiscardSourceChanges);
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(label);
            row.Children.Add(back);
            row.Children.Add(discard);
            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(row);
            stack.Children.Add(session.Status);
            var strip = new Border
            {
                Child = stack,
                Padding = new Thickness(6, 3),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = Brushes.Gray,
            };
            AutomationProperties.SetName(strip, "Editing as " + form.Name);
            DockPanel.SetDock(strip, Dock.Top);
            var panel = new DockPanel { LastChildFill = true };
            panel.Children.Add(strip);
            panel.Children.Add(session.View);
            return panel;
        }

        /// <summary>A button that needs no theme: its template is built here, so a host with no styles (a WinForms host) still shows it, and a host's theme does
        /// not change it. It has Avalonia's button peer, so it can be invoked by a screen reader, and Enter and Space press it.</summary>
        static Button StripButton(string text, Action click)
        {
            var button = new Button
            {
                Margin = new Thickness(0, 0, 8, 0),
                Template = new FuncControlTemplate<Button>((b, scope) => new Border
                {
                    Padding = new Thickness(6, 1),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Gray,
                    CornerRadius = new CornerRadius(3),
                    Background = Brushes.Transparent,
                    Child = new TextBlock { Text = text },
                }),
            };
            AutomationProperties.SetName(button, text);
            button.Click += (o, e) => click();
            button.GotFocus += (o, e) => { if (button.GetVisualChildren().FirstOrDefault() is Border b) b.BorderThickness = new Thickness(2); };
            button.LostFocus += (o, e) => { if (button.GetVisualChildren().FirstOrDefault() is Border b) b.BorderThickness = new Thickness(1); };
            return button;
        }

        static bool IsSourceToggle(Key key, KeyModifiers modifiers) =>
            key == Key.M && (modifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift) && (modifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) == 0;

        /// <summary>The field's own key handling while a source view is up: the toggle returns to rich mode; every other key is the view's.</summary>
        bool HandleSourceKey(Key key, KeyModifiers modifiers)
        {
            if (IsSourceToggle(key, modifiers)) { LeaveSourceMode(); return true; }
            return _source!.View.HandleKey(key, modifiers);
        }

        Size MeasureSource(Size availableSize)
        {
            var panel = _source!.Panel;
            double w = double.IsInfinity(availableSize.Width) ? PreferredWidth : availableSize.Width;
            panel.Measure(new Size(w, availableSize.Height));
            return new Size(w, panel.DesiredSize.Height);
        }

        void FocusSourceView()
        {
            var view = _source?.View;
            if (view != null && !view.IsKeyboardFocusWithin) OwnDispatcher.Post(() => { if (_source?.View == view) view.Focus(); });
        }
    }
}
