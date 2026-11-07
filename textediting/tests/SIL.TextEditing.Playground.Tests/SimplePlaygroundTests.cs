using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>The playground as a person writing sees it: the field, the selection bar over the selection and the Markdown. Developer mode
    /// adds the switches, the model and the event log.</summary>
    public class SimplePlaygroundTests
    {
        [Fact]
        public void Without_developer_mode_only_the_field_the_scenario_picker_and_the_markdown_show() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, developerMode: false))
            {
                Assert.False(s.View.DeveloperMode);
                Assert.False(s.FindLogical<StackPanel>(PlaygroundView.Names.DeveloperPanel).IsVisible);
                Assert.False(s.FindLogical<TabItem>(PlaygroundView.Names.ModelTab).IsVisible);
                Assert.False(s.FindLogical<TextBox>(PlaygroundView.Names.EventLog).IsEffectivelyVisible);
                Assert.True(s.View.ShowsMarkdown);
                Assert.True(s.Find<ComboBox>(PlaygroundView.Names.ScenarioPicker).IsEffectivelyVisible);
                Assert.StartsWith("The word [كتاب]{ws=ar}", s.View.MarkdownText);
            }
        });

        [Fact]
        public void The_developer_mode_switch_shows_the_switches_the_model_and_the_log_and_hides_them_again() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, developerMode: false))
            {
                var check = s.Find<CheckBox>(PlaygroundView.Names.DeveloperMode);
                check.IsChecked = true;
                Headless.Pump();
                Assert.True(s.View.DeveloperMode);
                Assert.True(s.FindLogical<StackPanel>(PlaygroundView.Names.DeveloperPanel).IsVisible);
                Assert.True(s.FindLogical<TabItem>(PlaygroundView.Names.ModelTab).IsVisible);

                s.View.ShowsMarkdown = false;
                check.IsChecked = false;
                Headless.Pump();
                Assert.False(s.FindLogical<StackPanel>(PlaygroundView.Names.DeveloperPanel).IsVisible);
                Assert.True(s.View.ShowsMarkdown);
            }
        });

        [Fact]
        public void A_selection_opens_the_bar_and_its_bold_button_makes_the_selection_bold() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, developerMode: false))
            {
                s.Caret(0, 2);
                Headless.Pump();
                Assert.False(s.View.SelectionBarOpen);

                s.Select(0, 0, 8);
                Headless.Pump();
                Assert.True(s.View.SelectionBarOpen);
                Assert.True(s.FindLogical<Popup>(PlaygroundView.Names.SelectionBar).IsOpen);

                s.ClickLogical(PlaygroundView.Names.BarBold);
                var run = s.Editor.Document.Paragraphs[0].Runs[0];
                Assert.Equal("The word", run.Text);
                Assert.Equal(TextPropertyNumbers.ToggleOn, run.Props.IntProps[TextPropertyNumbers.Bold].Value);
                Assert.Contains("**The word**", s.View.MarkdownText);

                s.Caret(0, 2);
                Headless.Pump();
                Assert.False(s.View.SelectionBarOpen);
            }
        });

        [Fact]
        public void The_bar_shows_and_sets_the_writing_system_of_the_selection() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, developerMode: false))
            {
                s.Select(0, 0, 3);
                Headless.Pump();
                var box = s.FindLogical<ComboBox>(PlaygroundView.Names.BarWritingSystem);
                Assert.StartsWith("en ", (string)box.SelectedItem!);

                box.SelectedIndex = SampleWritingSystems.All.ToList().FindIndex(w => w.Tag == "fr");
                Headless.Pump();
                Assert.Equal("fr", s.Editor.Document.Paragraphs[0].Runs[0].Ws);
                Assert.StartsWith("[The]{ws=fr} word", s.View.MarkdownText);
            }
        });

        [Fact]
        public void More_opens_the_second_row_with_the_paragraph_style_and_picture() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, developerMode: false))
            {
                s.Select(0, 0, 3);
                Headless.Pump();
                Assert.False(s.View.SelectionBarExpanded);
                s.FindLogical<ToggleButton>(PlaygroundView.Names.BarMore).IsChecked = true;
                Assert.True(s.View.SelectionBarExpanded);
                Assert.True(s.FindLogical<ComboBox>(PlaygroundView.Names.BarParaStyle).IsVisible);

                s.ClickLogical(PlaygroundView.Names.BarPicture);
                Assert.Equal(Scenarios.RedPicture, s.Editor.Document.Paragraphs[0].Runs[0].ObjData);
            }
        });

        [Fact]
        public void The_bar_goes_when_the_field_turns_read_only_or_focus_moves_to_the_markdown() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Select(0, 0, 8);
                Headless.Pump();
                Assert.True(s.View.SelectionBarOpen);
                s.Find<CheckBox>(PlaygroundView.Names.ReadOnly).IsChecked = true;
                Headless.Pump();
                Assert.False(s.View.SelectionBarOpen);
                s.Find<CheckBox>(PlaygroundView.Names.ReadOnly).IsChecked = false;
                Headless.Pump();
                Assert.True(s.View.SelectionBarOpen);

                s.Find<TextBox>(PlaygroundView.Names.MarkdownSource).Focus();
                Headless.Pump();
                Assert.False(s.View.SelectionBarOpen);
            }
        });

        [Fact]
        public void Closing_a_box_of_the_bar_gives_focus_back_to_the_field() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, developerMode: false))
            {
                s.Select(0, 0, 8);
                Headless.Pump();
                var box = s.FindLogical<ComboBox>(PlaygroundView.Names.BarWritingSystem);
                box.Focus();
                box.IsDropDownOpen = true;
                Headless.Pump();
                box.IsDropDownOpen = false;
                Headless.Pump();
                Assert.True(s.Field.IsFocused);
                Assert.True(s.View.SelectionBarOpen);
            }
        });

        [Fact]
        public void Pictures_are_written_as_images_with_their_path_and_guid() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Picture, developerMode: false))
            {
                var red = "![Red square](/files/pictures/red-square.png){guid=" + SamplePictures.Red.Guid.ToString("D") + "}";
                Assert.Contains(red, s.View.MarkdownText);
                Assert.Contains("![Blue square](/files/pictures/blue-square.png)", s.View.MarkdownText);
                Assert.DoesNotContain("s6x", s.View.MarkdownText);
            }
        });

        [Fact]
        public void A_picture_the_project_does_not_hold_is_not_applied() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Picture, developerMode: false))
            {
                s.View.MarkdownText = s.View.MarkdownText.Replace("/files/pictures/red-square.png){guid=" + SamplePictures.Red.Guid.ToString("D") + "}", "/files/pictures/green.png)");
                Assert.False(s.View.ApplyMarkdown());
                Assert.Contains("fwmd.picture.unknown", s.View.MarkdownStatusText);
            }
        });
    }
}
