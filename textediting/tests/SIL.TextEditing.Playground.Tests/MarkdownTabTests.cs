using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>The playground's Markdown tab: the field as FieldWorks Markdown, which a person or an agent edits and applies as one undo step.</summary>
    public class MarkdownTabTests
    {
        const string BookLine = "The word [كتاب]{ws=ar} means book; [مرحبا بالعالم]{ws=ar} means hello world.";

        public static IEnumerable<object[]> ScenarioNames() => Scenarios.All.Where(s => !s.Many).Select(s => new object[] { s.Name });

        [Fact]
        public void The_markdown_tab_is_shown_first_and_holds_the_field_one_line_per_paragraph() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                Assert.True(s.View.ShowsMarkdown);
                var lines = s.View.MarkdownText.Split('\n').Where(l => l.Length > 0).ToList();
                Assert.Equal(BookLine, lines[0]);
                // The two right-to-left paragraphs share one div that says so; the others are plain lines.
                Assert.Equal(new[]
                {
                    BookLine,
                    "::: {rightToLeft=1}",
                    "[هذه جملة عربية فيها&#x20;]{ws=ar}English words[&#x20;و&#x20;]{ws=ar}1234[&#x20;أرقام.]{ws=ar}",
                    "[فقرة عربية قصيرة.]{ws=ar}",
                    ":::",
                    "Plain English to finish, with an [expression française]{ws=fr} in the middle.",
                }, lines);
                Assert.DoesNotContain("key=", s.View.MarkdownText);
            }
        });

        [Fact]
        public void Typing_in_the_field_updates_the_markdown() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Caret(0, 0);
                s.Type("Look: ");
                Headless.Pump();   // the Markdown is written again once the dispatcher is idle
                Assert.StartsWith("Look: The word [كتاب]{ws=ar}", s.View.MarkdownText);
            }
        });

        [Fact]
        public void An_edit_to_the_markdown_applies_as_one_undo_step_and_undo_restores_the_field() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var before = s.ParagraphText(0);
                int done = s.View.Undo.Done.Count;
                // What an agent does: change the gloss and mark a vernacular word, as text.
                s.View.MarkdownText = s.View.MarkdownText.Replace("means book;", "means [kitabu]{.v};");
                Assert.Contains("Edited", s.View.MarkdownStatusText);
                s.Click(PlaygroundView.Names.MarkdownApply);

                Assert.Contains("means kitabu;", s.ParagraphText(0));
                var run = s.Editor.Document.Paragraphs[0].Runs.Single(r => r.Text == "kitabu");
                Assert.Equal(SampleWritingSystems.DefaultVernacular, run.Ws);
                Assert.Equal(done + 1, s.View.Undo.Done.Count);
                Assert.Equal("edit as Markdown", s.View.Undo.Done[s.View.Undo.Done.Count - 1].Label);
                Assert.Contains("Applied as one undo step", s.View.MarkdownStatusText);
                Assert.Contains("[kitabu]{.v}", s.View.MarkdownText);

                Assert.True(s.View.UndoLast());
                Headless.Pump();
                Assert.Equal(before, s.ParagraphText(0));
                Assert.Equal(BookLine, s.View.MarkdownText.Split('\n')[0]);
            }
        });

        [Fact]
        public void Markdown_with_an_error_changes_nothing_and_keeps_what_was_typed() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var before = s.ParagraphText(0);
                int done = s.View.Undo.Done.Count;
                var broken = s.View.MarkdownText.Replace("{ws=ar} means book", "{} means book");
                s.View.MarkdownText = broken;
                s.Click(PlaygroundView.Names.MarkdownApply);

                Assert.Equal(before, s.ParagraphText(0));
                Assert.Equal(done, s.View.Undo.Done.Count);
                Assert.StartsWith("Not applied: fwmd.", s.View.MarkdownStatusText);
                Assert.Equal(broken, s.View.MarkdownText);

                s.Click(PlaygroundView.Names.MarkdownRevert);
                Assert.Equal(BookLine, s.View.MarkdownText.Split('\n')[0]);
            }
        });

        [Fact]
        public void A_heading_made_on_the_first_line_is_applied_and_undone_exactly() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var key = s.ParagraphKey(0);
                s.View.MarkdownText = "# " + s.View.MarkdownText;
                Assert.True(s.View.ApplyMarkdown(), s.View.MarkdownStatusText);
                Assert.Equal("Heading 1", s.Editor.Document.Paragraphs[0].Style);
                Assert.Equal(key, s.ParagraphKey(0));
                Assert.StartsWith("# The word", s.View.MarkdownText);

                Assert.True(s.View.UndoLast());
                Headless.Pump();
                Assert.Equal(string.Empty, s.Editor.Document.Paragraphs[0].Style);
                Assert.Equal(BookLine, s.View.MarkdownText.Split('\n')[0]);
            }
        });

        [Fact]
        public void A_heading_the_field_does_not_allow_is_made_plain() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Editor.BlockFeatures = BlockFeatures.All & ~BlockFeatures.Headings;
                s.View.MarkdownText = s.View.MarkdownText + "\n# A new heading\n";
                Assert.True(s.View.ApplyMarkdown(), s.View.MarkdownStatusText);
                var added = s.Editor.Document.Paragraphs[s.Editor.Document.Paragraphs.Count - 1];
                Assert.Equal("A new heading", added.Text);
                Assert.Equal(string.Empty, added.Style);
                Assert.Contains("now plain paragraphs", s.View.MarkdownStatusText);
            }
        });

        [Fact]
        public void An_edit_that_would_replace_read_only_text_is_refused() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.ReadOnlyRuns))
            {
                int done = s.View.Undo.Done.Count;
                var before = s.ParagraphText(0);
                var edited = s.View.MarkdownText + "\nOne more line.\n";
                s.View.MarkdownText = edited;
                Assert.False(s.View.ApplyMarkdown());
                Assert.Contains("read-only text", s.View.MarkdownStatusText);
                Assert.Equal(done, s.View.Undo.Done.Count);
                Assert.Equal(before, s.ParagraphText(0));
                Assert.Equal(edited, s.View.MarkdownText);
            }
        });

        [Fact]
        public void A_line_added_at_the_top_has_no_key_and_every_other_paragraph_keeps_its_own() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var keys = s.Editor.Document.Paragraphs.Select(p => p.Key).ToList();
                s.View.MarkdownText = "A new first line.\n\n" + s.View.MarkdownText;
                Assert.True(s.View.ApplyMarkdown(), s.View.MarkdownStatusText);
                Assert.Null(s.ParagraphKey(0));
                Assert.Equal(keys, s.Editor.Document.Paragraphs.Skip(1).Select(p => p.Key).ToList());
            }
        });

        [Fact]
        public void Applied_text_takes_the_field_s_normalisation_form_and_no_read_only_mark() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Editor.Document.Normalization = System.Text.NormalizationForm.FormC;
                // The reader gives NFD; a field stored NFC must get the composed letter. "editable=no" marks the run read-only.
                s.View.MarkdownText = s.View.MarkdownText + "\n[cafe\u0301]{editable=no}\n";
                Assert.True(s.View.ApplyMarkdown(), s.View.MarkdownStatusText);
                var added = s.Editor.Document.Paragraphs[s.Editor.Document.Paragraphs.Count - 1];
                Assert.Equal("caf\u00E9", added.Text);
                Assert.All(added.Runs, r => Assert.False(r.Props.IntProps.ContainsKey(TextPropertyNumbers.Editable)));
            }
        });

        [Theory]
        [MemberData(nameof(ScenarioNames))]
        public void Applying_the_markdown_unchanged_makes_no_undo_step(string name) => Headless.Run(() =>
        {
            using (var s = new Session(name))
            {
                int done = s.View.Undo.Done.Count;
                Assert.False(s.View.ApplyMarkdown(), s.View.MarkdownStatusText);
                Assert.Equal(done, s.View.Undo.Done.Count);
                Assert.Contains("No change", s.View.MarkdownStatusText);
            }
        });

        [Fact]
        public void The_model_tab_still_shows_the_runs() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.View.ShowsMarkdown = false;
                Assert.False(s.View.ShowsMarkdown);
                Assert.Contains("ws=ar", s.Inspector);
                Assert.NotNull(s.Find<TabItem>(PlaygroundView.Names.ModelTab));
            }
        });
    }
}
