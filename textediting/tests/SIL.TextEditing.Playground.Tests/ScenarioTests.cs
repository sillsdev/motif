using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    public class ScenarioTests
    {
        public static IEnumerable<object[]> ScenarioNames() => Scenarios.All.Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(ScenarioNames))]
        public void Every_scenario_loads_through_the_picker_and_the_inspector_shows_its_model(string name) => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                s.Pick(name);
                var scenario = Scenarios.Find(name);
                Assert.Equal(name, s.View.CurrentScenario.Name);
                Assert.Equal(scenario.Description, s.Find<TextBlock>(PlaygroundView.Names.ScenarioDescription).Text);
                Assert.Contains("scenario: " + name, s.Log);

                int fieldCount = scenario.Many ? s.View.FieldCount : 1;
                Assert.Equal(fieldCount, s.View.Fields.Count);
                foreach (var field in s.View.Fields.Take(3))
                    Assert.True(field.DesiredSize.Height > 10, "field was laid out: " + field.DesiredSize);

                int paragraphs = s.Editor.Document.Paragraphs.Count;
                Assert.Contains("paragraphs " + paragraphs, s.Inspector);
                Assert.Contains("[0] key=" + s.ParagraphKey(0), s.Inspector);
                Assert.StartsWith("ws: ", s.WsLabel);
            }
        });

        [Fact]
        public void Mixed_scenario_has_an_rtl_and_an_ltr_paragraph_and_both_writing_systems() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                Assert.Contains(s.Editor.Document.Paragraphs, p => p.Rtl);
                Assert.Contains(s.Editor.Document.Paragraphs, p => !p.Rtl);
                Assert.Contains("rtl=yes", s.Inspector);
                Assert.Contains("rtl=no", s.Inspector);
                Assert.Contains("ws=ar", s.Inspector);
                Assert.Contains("ws=en", s.Inspector);
            }
        });

        [Fact]
        public void Indic_and_thai_scenario_holds_hi_ta_and_th_runs() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.IndicThai))
            {
                foreach (var ws in new[] { "hi", "ta", "th" }) Assert.Contains("ws=" + ws, s.Inspector);
                Assert.True(s.Field.ParagraphLayoutFor(0).Height > 10);
            }
        });

        [Fact]
        public void Unicode_scenario_shows_combining_marks_surrogates_and_private_use_as_escapes() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Unicode))
            {
                Assert.Contains("cafe\\u0301", s.Inspector);
                Assert.Contains("café", s.Inspector);
                Assert.Contains("\\U0001F600", s.Inspector);
                Assert.Contains("\\U00020000", s.Inspector);
                Assert.Contains("\\uE000", s.Inspector);
                Assert.Contains("ws=x-pua", s.Inspector);
                Assert.Contains("\\u200D", s.Inspector);
            }
        });

        [Fact]
        public void Picture_scenario_has_object_runs_including_inside_rtl_text() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Picture))
            {
                var rtl = s.Editor.Document.Paragraphs.First(p => p.Rtl);
                Assert.Contains(rtl.Runs, r => r.IsObject);
                Assert.Contains("obj=", s.Inspector);
                Assert.Contains("\\uFFFC", s.Inspector);
            }
        });

        [Fact]
        public void Opaque_scenario_shows_unknown_properties_and_the_paragraph_style() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Opaque))
            {
                Assert.Contains("9001=42@1", s.Inspector);
                Assert.Contains("9002=\"a;b=c<&>é\"", s.Inspector);
                Assert.Contains("style=\"Block Quote\"", s.Inspector);
            }
        });

        [Fact]
        public void Long_scenario_has_200_paragraphs_and_the_inspector_shows_a_window_around_the_caret() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long))
            {
                Assert.Equal(200, s.Editor.Document.Paragraphs.Count);
                Assert.Contains("paragraphs 200", s.Inspector);
                Assert.Contains("showing paragraphs 0 to 4", s.Inspector);
                s.Press(Key.End, KeyModifiers.Control);
                Assert.Equal(199, s.Editor.Caret.Paragraph);
                Assert.Contains("showing paragraphs 195 to 199", s.Inspector);
                Assert.DoesNotContain("[0] id=", s.Inspector);
            }
        });

        [Fact]
        public void Many_fields_scenario_follows_the_focused_field_and_keeps_undo_per_field() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                s.Pick(Scenarios.ManyFields);
                var fields = s.View.Fields;
                Assert.Equal(50, fields.Count);

                fields[1].Focus();
                Headless.Pump();
                Assert.Same(fields[1], s.Field);
                Assert.Contains("focus: field 2", s.Log);
                Assert.Contains("Champ 2", s.Inspector);

                s.Caret(0, 0);
                s.Type("X");
                Assert.Equal("XChamp 2", fields[1].Editor.Document.Paragraphs[0].Text);
                Assert.Equal("Field 1", fields[0].Editor.Document.Paragraphs[0].Text);

                fields[0].Focus();
                Headless.Pump();
                s.Click(PlaygroundView.Names.Undo);
                Assert.Contains("nothing to undo", s.Log);
                Assert.Equal("XChamp 2", fields[1].Editor.Document.Paragraphs[0].Text);

                fields[1].Focus();
                Headless.Pump();
                s.Click(PlaygroundView.Names.Undo);
                Assert.Equal("Champ 2", fields[1].Editor.Document.Paragraphs[0].Text);
            }
        });
    }
}
