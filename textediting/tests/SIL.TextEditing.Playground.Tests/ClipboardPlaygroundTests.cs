using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0029 in the playground: a Copy as submenu, and a notice when pasted text names a writing system the project lacks.</summary>
    public class ClipboardPlaygroundTests
    {
        [Fact]
        public void The_context_menu_has_a_Copy_as_submenu_with_plain_text_and_the_registered_forms() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Select(2, 0, 5);
                s.View.CloseContextMenu();
                s.RightClick(s.PointAt(2, 2));
                Assert.Contains(PlaygroundView.CopyAsCaption, s.View.LastContextMenuItems);
                Assert.Equal("Plain text", s.View.LastCopyAsItems[0]);
                Assert.Contains("Runs (debug dump)", s.View.LastCopyAsItems);
                Assert.Contains(s.View.LastCopyAsItems, c => c.StartsWith("FieldWorks Markdown"));   // listed, disabled until a converter is registered
            }
        });

        [Fact]
        public void Copy_as_plain_text_puts_only_the_text_on_the_clipboard() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Select(2, 0, 5);
                await s.View.CopyAsAsync(ClipboardForms.PlainTextId);
                Assert.Contains("clipboard: copied as " + ClipboardForms.PlainTextId, s.Log);
                var top = TopLevel.GetTopLevel(s.Field);
                Assert.Equal(s.ParagraphText(2).Substring(0, 5), await global::SIL.TextEditing.Avalonia.ClipboardCompat.GetTextAsync(top));
                Assert.Null(await global::SIL.TextEditing.Avalonia.ClipboardCompat.GetStringAsync(top, Interchange.ClipboardFormat));
            }
        });

        [Fact]
        public void Pasting_text_in_a_writing_system_the_project_lacks_logs_the_notice() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                var p = new Paragraph { DefaultWs = "qqq" };
                p.Runs.Add(new Run { Ws = "qqq", Text = "stranger" });
                var top = TopLevel.GetTopLevel(s.Field);
                await global::SIL.TextEditing.Avalonia.ClipboardCompat.SetAsync(top, "stranger",
                    new[] { new System.Collections.Generic.KeyValuePair<string, object>(Interchange.ClipboardFormat, Interchange.ToXml(new[] { p })) });
                s.Caret(0, 0);
                s.Click(PlaygroundView.Names.Paste);
                await s.WaitClipboard();
                Assert.Contains("clipboard: notice, writing system(s) qqq", s.Log);
                Assert.All(s.Editor.Document.Paragraphs[0].Runs.Take(1), r => Assert.NotEqual("qqq", r.Ws));
            }
        });
    }
}
