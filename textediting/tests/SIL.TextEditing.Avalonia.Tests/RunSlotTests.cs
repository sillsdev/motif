using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.TextFormatting;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Every run Avalonia puts on a line is a slice of the paragraph's own text (or an object run that says where it is), so a run's
    /// place in the paragraph is always known exactly. <c>ParagraphLayout.RunSlots</c> had an inexact fallback for a run that was not (removed);
    /// these cases (mirrored brackets, surrogate pairs, combining marks, tabs, no-break spaces, hard breaks, pictures, labels, an ellipsis, a
    /// composition, shifted runs, justified lines, every width from narrow to wide) never reach one.</summary>
    public class RunSlotTests
    {
        static readonly string Break = DocumentEditor.LineSeparator.ToString();

        static Paragraph Styled(string ws, params string[] pieces)
        {
            var p = Para(ws, new string[0]);
            for (int i = 0; i < pieces.Length; i++)
            {
                var run = new Run { Ws = ws, Text = pieces[i] };
                if (i % 3 == 1) run.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn).SetInt(TextPropertyNumbers.Offset, 3000, 1);
                if (i % 3 == 2) run.Props.SetInt(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn).SetInt(TextPropertyNumbers.FontSize, 18000, 1).SetInt(TextPropertyNumbers.BackColor, 0x99FFFF);
                p.Runs.Add(run);
            }
            return p;
        }

        static IEnumerable<Paragraph> Paragraphs()
        {
            const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly. ";
            yield return Styled("en", English, English, English);
            yield return Styled("en", "Tabs\tand no-break spaces\there ", English, "(brackets) [and] {braces} \"quotes\" ", English);
            yield return Styled("en", "Emoji \U0001F600\U0001F44D\U0001F468‍\U0001F469‍\U0001F467 and é combining ạ̈ marks ", English, "\U0001F600 ", English);
            yield return Styled("en", "first" + Break + "second line is somewhat longer than the first" + Break, English);
            yield return Styled("ar", "هذه فقرة عربية (بين قوسين) وفيها [أقواس] و«علامات» وأرقام 123 و English words ", "طويلة بما يكفي لتلتف", " على عدة أسطر ", "with (mirrored) brackets inside ", "وتنتهي هنا");
            yield return Styled("en", "Mixed ابج دهو (in) the middle ", "and زحط يكل (more) ", "English after that so the lines wrap, ", "ثم عربي ");
            var picture = Styled("en", "Before the picture ", "and after it ", English);
            picture.Runs.Insert(1, Run.Object("en", "\u0008pic"));
            yield return picture;
            var arabicPicture = Styled("ar", "قبل الصورة ", "وبعدها ", "نص عربي طويل جدا يكفي ليلتف على أسطر");
            arabicPicture.Runs.Insert(1, Run.Object("ar", "\u0008pic"));
            arabicPicture.Rtl = true;
            yield return arabicPicture;
        }

        static Document Document(int align, bool label, int maxLines)
        {
            var paragraphs = Paragraphs().ToArray();
            foreach (var p in paragraphs)
            {
                if (p.Runs[0].Ws == "ar") p.Rtl = true;
                p.Props.SetInt(TextPropertyNumbers.Align, align);
                if (label) p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 10).SetInt(TextPropertyNumbers.LeadingIndent, 36000, 1).SetInt(TextPropertyNumbers.FirstIndent, -24000, 1);
                if (maxLines > 0) p.Props.SetInt(TextPropertyNumbers.MaxLines, maxLines);
            }
            return Doc(paragraphs);
        }

        [Theory]
        [InlineData(0, false, 0, false)]
        [InlineData(5, false, 0, false)]
        [InlineData(5, true, 0, true)]
        [InlineData(0, false, 2, false)]
        [InlineData(5, false, 2, true)]
        [InlineData(1, true, 0, true)]
        public void Every_run_of_every_line_is_a_slice_of_the_paragraph_text(int align, bool label, int maxLines, bool glyphObjects) => Headless.Run(() =>
        {
            int runs = 0;
            foreach (double width in new[] { 60.0, 97, 131, 170, 233, 300, 417, 640 })
            {
                var field = Fixtures.Field(Document(align, label, maxLines), width, f => { f.ObjectAsGlyph = glyphObjects; f.ObjectImage = Fixtures.Picture; });
                for (int i = 0; i < field.Editor.Document.Paragraphs.Count; i++)
                {
                    var pl = field.ParagraphLayoutFor(i);
                    foreach (var li in pl.Lines.Where(l => !l.IsEmpty))
                    {
                        var slots = ParagraphLayout.RunSlots(li.Line);
                        Assert.True(slots.Count == li.Line.TextRuns.OfType<DrawableTextRun>().Count(), "paragraph " + i + " at " + width + ": a run of the line is not a slice of the paragraph text");
                        foreach (var slot in slots)
                        {
                            runs++;
                            if (!(slot.Run is ObjectRun))
                                Assert.Equal(pl.EffectiveText.Substring(slot.Start, slot.Length), slot.Run.Text.ToString());
                        }
                    }
                }
            }
            Assert.True(runs > 200, "runs looked at: " + runs);
        });

        [Fact]
        public void The_run_of_a_line_that_a_composition_splits_is_a_slice_too() => Headless.Run(() =>
        {
            var field = Fixtures.Field(Doc(Styled("en", "Some words ", "in bold ", "and more words after them ")), 150);
            field.Editor.SetCaret(new TextPosition(0, 14));
            field.SetPreedit("あいう", null);
            Fixtures.Layout(field, 150);
            var pl = field.ParagraphLayoutFor(0);
            foreach (var l in pl.Lines) Assert.Equal(l.Line.TextRuns.OfType<DrawableTextRun>().Count(), ParagraphLayout.RunSlots(l.Line).Count);
        });
    }
}
