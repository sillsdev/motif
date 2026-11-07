using System.Linq;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    public class FormatUndoObjectClipboardTests
    {
        static bool IsBold(Run r) => r.Props.TryGetInt(TextPropertyNumbers.Bold, out var v) && v == TextPropertyNumbers.ToggleOn;

        [Fact]
        public void Bold_toggles_on_the_selection_keeps_paragraph_ids_and_toggles_back_to_one_run() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var ids = s.Editor.Document.Paragraphs.Select(p => p.Key).ToList();
                int runsBefore = s.Editor.Document.Paragraphs[0].Runs.Count;

                s.Select(0, 4, 8);
                s.Click(PlaygroundView.Names.Bold);
                var p0 = s.Editor.Document.Paragraphs[0];
                Assert.Equal(runsBefore + 2, p0.Runs.Count);
                Assert.Equal("word", p0.Runs.Single(IsBold).Text);
                Assert.Contains("ws=en \"word\" props{3=1@3}", s.Inspector);
                Assert.Equal(ids, s.Editor.Document.Paragraphs.Select(p => p.Key).ToList());
                Assert.Contains("selection 0:4 -> 0:8", s.Inspector);

                s.Click(PlaygroundView.Names.Bold);
                Assert.Equal(runsBefore, s.Editor.Document.Paragraphs[0].Runs.Count);
                Assert.DoesNotContain("props{3=1@3}", s.Inspector);
            }
        });

        [Fact]
        public void Bold_with_ctrl_b_italic_and_underline_use_their_property_numbers_and_need_a_selection() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Caret(0, 2);
                s.Click(PlaygroundView.Names.Bold);
                Assert.Contains("bold needs a selection", s.Log);

                s.Select(0, 0, 3);
                s.Press(Key.B, KeyModifiers.Control);
                s.Click(PlaygroundView.Names.Italic);
                s.Click(PlaygroundView.Names.Underline);
                Assert.Contains("ws=en \"The\" props{2=1@3, 3=1@3, 5=3@3}", s.Inspector);
            }
        });

        [Fact]
        public void Formatting_keeps_unknown_properties_on_the_runs_it_touches() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Opaque))
            {
                s.Select(1, 0, s.ParagraphText(1).Length);
                s.Click(PlaygroundView.Names.Bold);
                Assert.Contains("9001=42@1", s.Inspector);
                Assert.Contains("3=1@3, 9003=7", s.Inspector);
                Assert.Contains("9002=\"a;b=c<&>é\"", s.Inspector);
                s.Click(PlaygroundView.Names.Bold);
                Assert.Contains("9001=42@1", s.Inspector);
                Assert.Contains("props{9003=7}", s.Inspector);
                Assert.DoesNotContain("3=1", s.Inspector.Replace("9003=7", string.Empty));
            }
        });

        [Fact]
        public void Undo_and_redo_buttons_drive_the_host_owned_stack_for_typing_and_for_bold() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                string original = s.ParagraphText(3);
                s.Caret(3, 0);
                s.Type("ZZ");
                Assert.Equal("ZZ" + original, s.ParagraphText(3));
                Assert.Contains("undo 1 done, 0 undone", s.Inspector);

                s.Click(PlaygroundView.Names.Undo);
                Assert.Equal(original, s.ParagraphText(3));
                Assert.Contains("undo 0 done, 1 undone", s.Inspector);
                Assert.Contains("undo requested (button): undid type", s.Log);

                s.Click(PlaygroundView.Names.Redo);
                Assert.Equal("ZZ" + original, s.ParagraphText(3));
                Assert.Contains("undo 1 done, 0 undone", s.Inspector);

                s.Select(3, 0, 2);
                s.Click(PlaygroundView.Names.Bold);
                Assert.Contains("props{3=1@3}", s.Inspector);
                s.Click(PlaygroundView.Names.Undo);
                Assert.DoesNotContain("props{3=1@3}", s.Inspector);
                s.Click(PlaygroundView.Names.Redo);
                Assert.Contains("props{3=1@3}", s.Inspector);
            }
        });

        [Fact]
        public void Ctrl_z_in_the_field_is_logged_as_an_undo_request_and_undoes_through_the_host() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                s.Type("abc");
                s.Press(Key.Z, KeyModifiers.Control);
                Assert.Equal(string.Empty, s.ParagraphText(0));
                Assert.Contains("undo requested (Ctrl+Z in the field)", s.Log);
                s.Press(Key.Y, KeyModifiers.Control);
                Assert.Equal("abc", s.ParagraphText(0));
                Assert.Contains("redo requested (Ctrl+Y in the field)", s.Log);
            }
        });

        [Fact]
        public void Inserting_the_picture_adds_one_object_run_and_backspace_removes_it_whole() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Picture))
            {
                int last = s.Editor.Document.Paragraphs.Count - 1;
                string text = s.ParagraphText(last);
                s.Caret(last, text.Length);
                s.Click(PlaygroundView.Names.Picture);
                var p = s.Editor.Document.Paragraphs[last];
                Assert.Equal(text.Length + 1, p.Length);
                Assert.Single(p.Runs, r => r.IsObject);
                Assert.Contains("obj=", s.Inspector.Substring(s.Inspector.LastIndexOf("[" + last + "]")));
                Assert.Contains("picture inserted", s.Log);

                s.Press(Key.Back);
                p = s.Editor.Document.Paragraphs[last];
                Assert.Equal(text, p.Text);
                Assert.DoesNotContain(p.Runs, r => r.IsObject);
            }
        });

        [Fact]
        public void Backspace_and_delete_remove_a_picture_inside_rtl_text_whole() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Picture))
            {
                int rtl = s.Editor.Document.Paragraphs.FindIndex(p => p.Rtl);
                int at = s.ParagraphText(rtl).IndexOf(Run.ObjectReplacement);
                int length = s.ParagraphText(rtl).Length;

                s.Caret(rtl, at + 1);
                s.Press(Key.Back);
                Assert.Equal(length - 1, s.ParagraphText(rtl).Length);
                Assert.DoesNotContain(Run.ObjectReplacement, s.ParagraphText(rtl));
                Assert.DoesNotContain("obj=", s.Inspector.Substring(s.Inspector.IndexOf("[" + rtl + "]"), s.Inspector.IndexOf("[" + (rtl + 1) + "]") - s.Inspector.IndexOf("[" + rtl + "]")));

                s.Click(PlaygroundView.Names.Undo);
                Assert.Equal(at, s.ParagraphText(rtl).IndexOf(Run.ObjectReplacement));
                s.Caret(rtl, at);
                s.Press(Key.Delete);
                Assert.DoesNotContain(Run.ObjectReplacement, s.ParagraphText(rtl));
                Assert.Equal(length - 1, s.ParagraphText(rtl).Length);
            }
        });

        [Fact]
        public void Copy_and_paste_keep_runs_writing_systems_and_unknown_properties() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.Opaque))
            {
                int last = s.Editor.Document.Paragraphs.Count - 1;
                var source = s.Editor.Document.Paragraphs[1];
                int pastedFrom = source.Text.IndexOf("unknown");
                int pastedTo = source.Text.IndexOf(" et du");
                s.Select(1, pastedFrom, pastedTo);
                s.Click(PlaygroundView.Names.Copy);
                await s.WaitClipboard();
                Assert.Contains("clipboard: copied 1 paragraph(s)", s.Log);

                s.Caret(last, s.ParagraphText(last).Length);
                s.Click(PlaygroundView.Names.Paste);
                await s.WaitClipboard();
                Assert.Contains("clipboard: pasted", s.Log);

                var target = s.Editor.Document.Paragraphs[last];
                Assert.EndsWith(source.Text.Substring(pastedFrom, pastedTo - pastedFrom), target.Text);
                var inserted = target.Runs.Where(r => r.Props.IntProps.ContainsKey(9001) || r.Props.IntProps.ContainsKey(9003)).ToList();
                Assert.Equal(2, inserted.Count);
                Assert.Equal(new IntPropValue(1, 42), inserted[0].Props.IntProps[9001]);
                Assert.Equal("a;b=c<&>é", inserted[0].Props.StrProps[9002]);
                Assert.Equal(7, inserted[1].Props.IntProps[9003].Value);
                Assert.True(IsBold(inserted[1]));
                Assert.Contains("9001=42@1", s.Inspector.Substring(s.Inspector.IndexOf("[" + last + "]")));
            }
        });

        [Fact]
        public void Copy_and_paste_across_paragraphs_adds_paragraphs_and_cut_removes_the_text() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                int count = s.Editor.Document.Paragraphs.Count;
                s.Editor.SetCaret(new TextPosition(0, 4));
                s.Editor.SetCaret(new TextPosition(1, 6), true);
                string expected = s.Editor.Document.PlainText();
                s.Click(PlaygroundView.Names.Copy);
                await s.WaitClipboard();
                Assert.Contains("clipboard: copied 2 paragraph(s)", s.Log);

                s.Caret(count - 1, 0);
                s.Click(PlaygroundView.Names.Paste);
                await s.WaitClipboard();
                Assert.Equal(count + 1, s.Editor.Document.Paragraphs.Count);
                Assert.Contains("paragraphs " + (count + 1), s.Inspector);
                Assert.Equal(s.ParagraphText(0).Substring(4), s.ParagraphText(count - 1));
                Assert.Contains(s.Editor.Document.Paragraphs[count].Runs, r => r.Ws == "ar");
                Assert.NotEqual(expected, s.Editor.Document.PlainText());

                s.Select(2, 0, 3);
                string before = s.ParagraphText(2);
                s.Click(PlaygroundView.Names.Cut);
                await s.WaitClipboard();
                Assert.Equal(before.Substring(3), s.ParagraphText(2));
                Assert.Contains("clipboard: cut", s.Log);
            }
        });
    }
}
