using Avalonia.Controls.Primitives;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    public class StyleToggleTests
    {
        [Theory]
        [InlineData(TextPropertyNumbers.Bold, "Strong", PlaygroundView.Names.Bold)]
        [InlineData(TextPropertyNumbers.Italic, "Emphasis", PlaygroundView.Names.Italic)]
        [InlineData(TextPropertyNumbers.Underline, "Misspelled", PlaygroundView.Names.Underline)]
        public void Style_appearance_toggles_off_explicitly_and_back_without_direct_formatting(int property, string style, string button) => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                s.Type("word");
                s.Select(0, 0, 4);
                s.View.ApplyCharacterStyle(style);
                Assert.True(s.Find<ToggleButton>(button).IsChecked);
                s.Click(button);
                Assert.False(s.Find<ToggleButton>(button).IsChecked);
                var run = Assert.Single(s.Editor.Document.Paragraphs[0].Runs);
                Assert.Equal(new IntPropValue(TextPropertyNumbers.VariationEnum, TextPropertyNumbers.ToggleOff), run.Props.IntProps[property]);
                Assert.Equal(style, run.Props.StrProps[TextPropertyNumbers.NamedStyle]);
                s.Click(button);
                run = Assert.Single(s.Editor.Document.Paragraphs[0].Runs);
                Assert.False(run.Props.IntProps.ContainsKey(property));
                Assert.Equal(style, run.Props.StrProps[TextPropertyNumbers.NamedStyle]);
                Assert.True(s.Find<ToggleButton>(button).IsChecked);
            }
        });

        [Theory]
        [InlineData(TextPropertyNumbers.Bold, PlaygroundView.Names.Bold, TextPropertyNumbers.ToggleOn)]
        [InlineData(TextPropertyNumbers.Italic, PlaygroundView.Names.Italic, TextPropertyNumbers.ToggleOn)]
        [InlineData(TextPropertyNumbers.Underline, PlaygroundView.Names.Underline, TextPropertyNumbers.UnderlineSingle)]
        public void Direct_formatting_still_toggles_on_and_back_to_no_property(int property, string button, int on) => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                s.Type("word");
                s.Select(0, 0, 4);
                s.Click(button);
                Assert.Equal(new IntPropValue(TextPropertyNumbers.VariationEnum, on), Assert.Single(s.Editor.Document.Paragraphs[0].Runs).Props.IntProps[property]);
                s.Click(button);
                Assert.False(Assert.Single(s.Editor.Document.Paragraphs[0].Runs).Props.IntProps.ContainsKey(property));
            }
        });
        [Theory]
        [InlineData(TextPropertyNumbers.Bold, "Strong", PlaygroundView.Names.BarBold)]
        [InlineData(TextPropertyNumbers.Italic, "Emphasis", PlaygroundView.Names.BarItalic)]
        [InlineData(TextPropertyNumbers.Underline, "Misspelled", PlaygroundView.Names.BarUnderline)]
        public void Mixed_selection_turns_on_then_off_using_each_runs_inheritance(int property, string style, string button) => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                s.Type("word plain");
                s.Select(0, 0, 4);
                s.View.ApplyCharacterStyle(style);
                s.Select(0, 0, 10);
                Assert.False(s.FindLogical<ToggleButton>(button).IsChecked);
                s.ClickLogical(button);
                var runs = s.Editor.Document.Paragraphs[0].Runs;
                Assert.Equal(2, runs.Count);
                Assert.False(runs[0].Props.IntProps.ContainsKey(property));
                Assert.True(runs[1].Props.IntProps.ContainsKey(property));
                Assert.True(s.FindLogical<ToggleButton>(button).IsChecked);
                s.ClickLogical(button);
                runs = s.Editor.Document.Paragraphs[0].Runs;
                Assert.Equal(TextPropertyNumbers.ToggleOff, runs[0].Props.IntProps[property].Value);
                Assert.False(runs[1].Props.IntProps.ContainsKey(property));
                Assert.False(s.FindLogical<ToggleButton>(button).IsChecked);
                s.Click(PlaygroundView.Names.Undo);
                s.Select(0, 0, 10);
                Assert.True(s.FindLogical<ToggleButton>(button).IsChecked);
                s.Click(PlaygroundView.Names.Redo);
                Assert.False(s.FindLogical<ToggleButton>(button).IsChecked);
            }
        });

        [Theory]
        [InlineData(TextPropertyNumbers.Bold, "Heading 1", PlaygroundView.Names.Bold)]
        [InlineData(TextPropertyNumbers.Italic, "Block Quote", PlaygroundView.Names.Italic)]
        public void Paragraph_style_also_supplies_the_resolved_appearance(int property, string style, string button) => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                s.Type("word");
                s.Select(0, 0, 4);
                s.View.ApplyParagraphStyle(style);
                Assert.True(s.Find<ToggleButton>(button).IsChecked);
                s.Click(button);
                Assert.Equal(TextPropertyNumbers.ToggleOff, Assert.Single(s.Editor.Document.Paragraphs[0].Runs).Props.IntProps[property].Value);
                s.Click(button);
                Assert.False(Assert.Single(s.Editor.Document.Paragraphs[0].Runs).Props.IntProps.ContainsKey(property));
                Assert.True(s.Find<ToggleButton>(button).IsChecked);
            }
        });

    }
}
