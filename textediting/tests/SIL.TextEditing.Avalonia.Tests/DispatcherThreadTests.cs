using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The field's thread check and its pause-timer fallback use the field's own dispatcher (issue #68), and the check
    /// is the control's CheckAccess (issue #67).</summary>
    public class DispatcherThreadTests
    {
        static readonly (string Name, Func<RichTextField, Action> Edit)[] Edits =
        {
            ("TypeText", f => () => f.Editor.TypeText("x")),
            ("PasteText", f => () => f.Editor.PasteText("x")),
            ("Backspace", f => () => f.Editor.Backspace()),
            ("Enter", f => () => f.Editor.Enter()),
            ("InsertLineBreak", f => () => f.Editor.InsertLineBreak()),
        };

        [Fact]
        public void A_detached_field_takes_every_edit_on_the_UI_thread_and_refuses_each_from_another_thread() => Headless.Run(async () =>
        {
            foreach (var (name, edit) in Edits)
            {
                var field = Fixtures.Field(OneParagraph("abcd"));
                field.Editor.SetCaret(new TextPosition(0, 2));
                var ex = await Task.Run(() => Record.Exception(edit(field)));
                Assert.True(ex is InvalidOperationException, name + ": " + (ex?.ToString() ?? "no exception"));
                Assert.Equal("abcd", field.Editor.Document.Paragraphs[0].Text);   // refused before it touched the document
                Assert.Single(field.Editor.Document.Paragraphs);

                edit(field)();                                                      // the UI thread's own edit goes through
                Assert.True(field.Editor.Document.Paragraphs[0].Text != "abcd" || field.Editor.Document.Paragraphs.Count != 1, name + ": the UI-thread edit changed nothing");
            }
        });
    }
}
