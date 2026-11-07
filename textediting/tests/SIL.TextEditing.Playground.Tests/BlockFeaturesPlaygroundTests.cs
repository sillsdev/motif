using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0033: the three block switches of the playground set RichTextField.BlockFeatures; off hides the styles of that kind and flattens a paste.</summary>
    public class BlockFeaturesPlaygroundTests
    {
        static IEnumerable<string> Offered(Session s) => s.Find<ComboBox>(PlaygroundView.Names.ParaStyle).Items.Cast<string>();

        [Fact]
        public void Every_feature_is_on_in_a_new_field_and_the_style_box_offers_every_style() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                Assert.Equal(BlockFeatures.All, s.Field.BlockFeatures);
                Assert.True(s.Find<CheckBox>(PlaygroundView.Names.AllowTables).IsChecked);
                Assert.True(s.Find<CheckBox>(PlaygroundView.Names.AllowLists).IsChecked);
                Assert.True(s.Find<CheckBox>(PlaygroundView.Names.AllowHeadings).IsChecked);
                Assert.Contains("Table Row", Offered(s));
                Assert.Contains("List Bullet", Offered(s));
                Assert.Contains("Heading 1", Offered(s));
            }
        });

        [Fact]
        public void A_switch_sets_the_field_and_hides_and_refuses_the_styles_of_its_kind() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Find<CheckBox>(PlaygroundView.Names.AllowHeadings).IsChecked = false;
                Headless.Pump();
                Assert.Equal(BlockFeatures.Tables | BlockFeatures.Lists, s.Field.BlockFeatures);
                Assert.Contains("block features: Tables, Lists", s.Log);
                Assert.DoesNotContain("Heading 1", Offered(s));
                Assert.Contains("List Bullet", Offered(s));

                s.Caret(0, 1);
                Assert.False(s.View.ApplyParagraphStyle("Heading 1"));
                Assert.Contains("paragraph style Heading 1 refused", s.Log);
                Assert.NotEqual("Heading 1", s.Editor.Document.Paragraphs[0].Style);

                s.Find<CheckBox>(PlaygroundView.Names.AllowHeadings).IsChecked = true;
                Headless.Pump();
                Assert.Equal(BlockFeatures.All, s.Field.BlockFeatures);
                Assert.Contains("Heading 1", Offered(s));
                Assert.True(s.View.ApplyParagraphStyle("Heading 1"));
                Assert.Equal("Heading 1", s.Editor.Document.Paragraphs[0].Style);
            }
        });

        [Fact]
        public void Switching_tables_off_hides_the_table_styles_and_a_pasted_table_becomes_plain_rows() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Find<CheckBox>(PlaygroundView.Names.AllowTables).IsChecked = false;
                Headless.Pump();
                Assert.DoesNotContain("Table Row", Offered(s));
                Assert.DoesNotContain("Table Header", Offered(s));

                var lead = new Paragraph { DefaultWs = "en" };
                lead.Runs.Add(new Run { Ws = "en", Text = "lead" });
                var header = new Paragraph { Style = "Table Header", DefaultWs = "en" };
                header.Runs.Add(new Run { Ws = "en", Text = "Name\tGloss" });
                var row = new Paragraph { Style = "Table Row", DefaultWs = "en" };
                row.Runs.Add(new Run { Ws = "en", Text = "palit\tbuy" });
                var top = global::Avalonia.Controls.TopLevel.GetTopLevel(s.Field);
                await global::SIL.TextEditing.Avalonia.ClipboardCompat.SetAsync(top, "x",
                    new[] { new KeyValuePair<string, object>(Interchange.ClipboardFormat, Interchange.ToXml(new[] { lead, header, row })) });
                int before = s.Editor.Document.Paragraphs.Count;
                s.Caret(0, 0);
                s.Click(PlaygroundView.Names.Paste);
                await s.WaitClipboard();

                var paragraphs = s.Editor.Document.Paragraphs;
                Assert.Equal(before + 2, paragraphs.Count);
                Assert.Equal("Name | Gloss", paragraphs[1].Text);
                Assert.StartsWith("palit | buy", paragraphs[2].Text);   // the last pasted paragraph is followed by the rest of the paragraph the caret was in
                Assert.All(paragraphs.Skip(1).Take(2), p => Assert.Equal(string.Empty, p.Style));
                Assert.Contains("2 table rows", s.Log);
                Assert.Contains("now plain paragraphs", s.Log);
            }
        });

        [Fact]
        public void With_tables_on_the_same_paste_keeps_the_table_rows() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                var lead = new Paragraph { DefaultWs = "en" };
                lead.Runs.Add(new Run { Ws = "en", Text = "lead" });
                var row = new Paragraph { Style = "Table Row", DefaultWs = "en" };
                row.Runs.Add(new Run { Ws = "en", Text = "palit" });
                var top = global::Avalonia.Controls.TopLevel.GetTopLevel(s.Field);
                await global::SIL.TextEditing.Avalonia.ClipboardCompat.SetAsync(top, "x",
                    new[] { new KeyValuePair<string, object>(Interchange.ClipboardFormat, Interchange.ToXml(new[] { lead, row })) });
                s.Caret(0, 0);
                s.Click(PlaygroundView.Names.Paste);
                await s.WaitClipboard();
                Assert.Equal("Table Row", s.Editor.Document.Paragraphs[1].Style);
                Assert.DoesNotContain("now plain paragraphs", s.Log);
            }
        });

        [Fact]
        public void The_switches_are_per_field_and_a_new_scenario_has_everything_on_again() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Find<CheckBox>(PlaygroundView.Names.AllowLists).IsChecked = false;
                Headless.Pump();
                Assert.False(s.Find<CheckBox>(PlaygroundView.Names.AllowLists).IsChecked);
                s.Pick(Scenarios.ReadOnlyRuns);
                Assert.Equal(BlockFeatures.All, s.Field.BlockFeatures);
                Assert.True(s.Find<CheckBox>(PlaygroundView.Names.AllowLists).IsChecked);
                Assert.Contains("List Bullet", Offered(s));
            }
        });
    }
}
