using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using SIL.TextEditing.Tests;
using Xunit;
using Xunit.Abstractions;
using SIL.TextEditing.Avalonia;

namespace SIL.TextEditing.Playground.Tests
{
    public class LazyLayoutTests
    {
        readonly ITestOutputHelper _output;
        public LazyLayoutTests(ITestOutputHelper output) { _output = output; }

        sealed class View : IDisposable
        {
            public readonly RichTextField Field;
            public readonly ScrollViewer Scroll;
            public readonly Window Window;

            public View(int count, bool varied = false, bool stacked = false)
            {
                var document = new Document();
                for (int i = 0; i < count; i++)
                {
                    var paragraph = new Paragraph { DefaultWs = "en", Key = "key" + i };
                    paragraph.Runs.Add(new Run { Ws = "en", Text = "paragraph " + i + " " +
                        (varied && i % 3 == 0 ? new string('w', 400) : "some text") });
                    document.Paragraphs.Add(paragraph);
                }
                Field = new RichTextField(document) { WritingSystems = new WritingSystemRegistry { FallbackSize = 16 } };
                Control content = Field;
                if (stacked)
                {
                    Field.Margin = new Thickness(6);
                    content = new StackPanel { Children = { Field } };
                }
                Scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                Window = new Window { Width = 320, Height = 240, Content = Scroll };
                Window.Show();
                Headless.Pump();
            }

            public void Draw()
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using (Window.CaptureRenderedFrame()) { }
                Headless.Pump();
            }

            public double ScreenY(double y) => Field.TranslatePoint(new Point(0, y), Window)!.Value.Y;
            public void Dispose() => Window.Close();
        }

        [Fact]
        public void Opening_and_drawing_five_hundred_paragraphs_builds_only_the_same_visible_layouts_as_a_short_text() => Headless.Run(() =>
        {
            using (var shortText = new View(40))
            using (var longText = new View(500))
            {
                shortText.Draw();
                longText.Draw();
                _output.WriteLine("Layouts built: short = " + shortText.Field.LayoutCount + ", 500 paragraphs = " + longText.Field.LayoutCount);
                Assert.InRange(longText.Field.LayoutCount, 1, 39);
                Assert.Equal(shortText.Field.LayoutCount, longText.Field.LayoutCount);
                long before = longText.Field.LayoutCount;
                longText.Field.ParagraphLayoutFor(400);
                Assert.Equal(before + 1, longText.Field.LayoutCount);
            }
        });

        [Fact]
        public void Scrolling_to_estimates_and_correcting_an_earlier_height_keeps_the_first_visible_paragraph_in_place() => Headless.Run(() =>
        {
            using (var view = new View(500, true))
            {
                var field = view.Field;
                view.Scroll.Offset = new Vector(0, field.ParagraphTop(250));
                Headless.Pump();
                view.Draw();
                Assert.InRange(field.LayoutCount, 2, 100);
                int first = field.PointToPosition(new Point(10, field.VisibleBounds.Top + 1)).Paragraph;
                Assert.InRange(first, 230, 270);
                Headless.Pump();
                double before = view.ScreenY(field.ParagraphTop(first));
                field.ParagraphLayoutFor(150);
                Headless.Pump();
                Assert.InRange(view.ScreenY(field.ParagraphTop(first)), before - 1, before + 1);
                long count = field.LayoutCount;
                field.ParagraphLayoutFor(350);
                Assert.Equal(count + 1, field.LayoutCount);
            }
        });

        [Fact]
        public void Returning_home_from_the_end_arranges_anchor_corrections_before_bringing_the_caret_into_view() => Headless.Run(() =>
        {
            using (var view = new View(500, true))
            {
                var field = view.Field;
                field.Focus();
                field.HandleKey(Key.End, KeyModifiers.Control);
                Headless.Pump();
                field.HandleKey(Key.Home, KeyModifiers.Control);
                Headless.Pump();
                Assert.Equal(new TextPosition(0, 0), field.Editor.Caret);
                Assert.True(field.ParagraphLayoutFor(0).Lines.Count > 1);
                AssertCaretVisible(field);
                Assert.InRange(field.LayoutCount, 1, 100);
            }
        });

        [Fact]
        public void Asking_for_the_caret_rectangle_realizes_its_paragraph_without_realizing_its_prefix() => Headless.Run(() =>
        {
            using (var view = new View(500))
            {
                var field = view.Field;
                field.Editor.SetCaret(new TextPosition(350, 4));
                field.InvalidateAllLayouts();
                long before = field.LayoutCount;
                var rect = field.CaretRect();
                Assert.Equal(before + 1, field.LayoutCount);
                Assert.True(rect.X > 0 && rect.Y > 0 && rect.Height > 0);
                var local = field.ParagraphLayoutFor(350).CaretRect(4);
                Assert.Equal(local.Y + field.ParagraphTop(350), rect.Y, 3);
            }
        });

        [Fact]
        public void End_home_page_moves_and_selection_into_estimates_keep_the_caret_visible_without_laying_out_the_document() => Headless.Run(() =>
        {
            using (var view = new View(500, true))
            {
                var field = view.Field;
                field.Focus();
                field.HandleKey(Key.End, KeyModifiers.Control);
                Headless.Pump();
                Assert.Equal(499, field.Editor.Caret.Paragraph);
                AssertCaretVisible(field);
                double screen = view.ScreenY(field.CaretRect().Y);
                field.ParagraphLayoutFor(150);
                Headless.Pump();
                Assert.InRange(view.ScreenY(field.CaretRect().Y), screen - 1, screen + 1);
                field.HandleKey(Key.Home, KeyModifiers.Control);
                Headless.Pump();
                Assert.Equal(new TextPosition(0, 0), field.Editor.Caret);
                AssertCaretVisible(field);
                field.HandleKey(Key.PageDown, KeyModifiers.None);
                Headless.Pump();
                Assert.True(field.Editor.Caret.CompareTo(new TextPosition(0, 0)) > 0);
                AssertCaretVisible(field);
                field.Editor.SetCaret(new TextPosition(320, 4), true);
                Headless.Pump();
                Assert.True(field.Editor.HasSelection);
                Assert.Equal(320, field.Editor.Caret.Paragraph);
                AssertCaretVisible(field);
                Assert.InRange(field.LayoutCount, 1, 120);
            }
        });

        static void AssertCaretVisible(RichTextField field)
        {
            var caret = field.CaretRect();
            var visible = field.VisibleBounds;
            Assert.True(caret.Y >= visible.Top - 1 && caret.Bottom <= visible.Bottom + 1,
                "caret " + caret + " outside " + visible);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Scrolling_to_the_bottom_keeps_the_last_paragraph_visible_as_estimates_grow(bool stacked) => Headless.Run(() =>
        {
            using (var view = new View(500, true, stacked))
            {
                var field = view.Field;
                view.Scroll.Offset = new Vector(0, view.Scroll.Extent.Height);
                Headless.Pump();
                var layout = field.ParagraphLayoutFor(499);
                double bottom = field.ParagraphTop(499) + layout.Height;
                Assert.InRange(bottom, field.VisibleBounds.Bottom - 3, field.VisibleBounds.Bottom);
                Assert.InRange(field.LayoutCount, 1, 100);
            }
        });

        [Fact]
        public void Splitting_and_joining_a_distant_paragraph_keep_height_entries_aligned() => Headless.Run(() =>
        {
            using (var view = new View(500, true))
            {
                var field = view.Field;
                field.Focus();
                field.Editor.SetCaret(new TextPosition(320, 4));
                Headless.Pump();
                field.HandleKey(Key.Enter, KeyModifiers.None);
                Headless.Pump();
                Assert.Equal(501, field.Editor.Document.Paragraphs.Count);
                Assert.Equal(321, field.Editor.Caret.Paragraph);
                AssertCaretVisible(field);
                field.HandleKey(Key.Back, KeyModifiers.None);
                Headless.Pump();
                Assert.Equal(500, field.Editor.Document.Paragraphs.Count);
                Assert.Equal(320, field.Editor.Caret.Paragraph);
                AssertCaretVisible(field);
                Assert.InRange(field.LayoutCount, 1, 100);
            }
        });

        [Fact]
        public void Moving_up_into_an_unrealized_tall_paragraph_lands_on_its_last_line() => Headless.Run(() =>
        {
            using (var view = new View(500, true))
            {
                var field = view.Field;
                field.Editor.SetCaret(new TextPosition(301, 0));
                field.HandleKey(Key.Up, KeyModifiers.None);
                Assert.Equal(300, field.Editor.Caret.Paragraph);
                var layout = field.ParagraphLayoutFor(300);
                Assert.Equal(layout.Lines.Count - 1, layout.LineIndexOf(field.Editor.Caret.Offset));
                Assert.InRange(field.LayoutCount, 1, 100);
            }
        });

        sealed class Clipboard : IClipboardBackend
        {
            public string Text = string.Empty;
            public Task<IReadOnlyList<string>> SetAsync(string plainText, IReadOnlyList<KeyValuePair<string, object>> forms)
            {
                Text = plainText;
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }
            public Task<object?> GetAsync(string formatName, ClipboardPayloadKind kind) => Task.FromResult<object?>(null);
            public Task<string?> GetTextAsync() => Task.FromResult<string?>(Text);
        }

        [Fact]
        public void Find_select_all_copy_and_accessibility_read_estimated_paragraphs_without_realizing_the_text_between() => Headless.Run(async () =>
        {
            using (var view = new View(500))
            {
                var field = view.Field;
                field.Focus();
                var clipboard = new Clipboard();
                field.ClipboardBackend = clipboard;
                long opening = field.LayoutCount;
                Assert.True(field.Find(new FindOptions { Pattern = "paragraph 350 " }));
                Headless.Pump();
                Assert.Equal(350, field.Editor.Caret.Paragraph);
                AssertCaretVisible(field);
                Assert.InRange(field.LayoutCount - opening, 1, 60);
                field.HandleKey(Key.A, KeyModifiers.Control);
                Headless.Pump();
                long selected = field.LayoutCount;
                await field.CopyAsync(false);
                var expected = field.Editor.Document.PlainText();
                Assert.Equal(expected, clipboard.Text);
                var peer = ControlAutomationPeer.CreatePeerForElement(field);
                Assert.Equal(string.Join("\r\n", field.Editor.Document.Paragraphs.Select(p => p.Text)), ((IValueProvider)peer).Value);
                Assert.Equal(selected, field.LayoutCount);
                view.Draw();
                Assert.InRange(field.LayoutCount, 1, 150);
            }
        });

        [Fact]
        public void Width_style_and_host_edits_rebuild_visible_layouts_and_leave_distant_paragraphs_unrealized() => Headless.Run(() =>
        {
            using (var view = new View(500, true))
            {
                var field = view.Field;
                var old = field.ParagraphLayoutFor(0);
                view.Window.Width = 420;
                Headless.Pump();
                Assert.NotSame(old, field.ParagraphLayoutFor(0));
                old = field.ParagraphLayoutFor(0);
                field.InvalidateAllLayouts();
                Headless.Pump();
                Assert.NotSame(old, field.ParagraphLayoutFor(0));
                field.Editor.Document.Paragraphs[350].Runs[0].Text += " changed";
                Headless.Pump();
                long before = field.LayoutCount;
                field.ParagraphLayoutFor(350);
                Assert.Equal(before + 1, field.LayoutCount);
                Assert.InRange(field.LayoutCount, 1, 100);
            }
        });

        [Fact]
        public void Spelling_uses_the_visible_estimated_range_without_laying_out_its_prefix() => Headless.Run(() =>
        {
            using (var view = new View(500))
            {
                var field = view.Field;
                field.SpellIdlePost = a => { };
                field.SpellChecking = new FakeSpellHost("paragraph", "some", "text");
                view.Scroll.Offset = new Vector(0, field.ParagraphTop(300));
                Headless.Pump();
                long before = field.LayoutCount;
                field.CheckSpellingStep();
                Assert.InRange(field.LayoutCount - before, 0, 2);
                Assert.Empty(field.SpellingMarks(150));
                Assert.InRange(field.LayoutCount, 1, 100);
            }
        });
    }
}
