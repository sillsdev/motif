using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>FieldWorks' double-click word stops where the writing system, the spell-check property or the character style changes (issue 116),
    /// but not at a change of bold, size, colour or any other property. A change of character style stops it only when the two resolved styles differ in
    /// writing system or spell-check property, so it needs the host's style resolver.</summary>
    public class DoubleClickRunBoundaryTests
    {
        sealed class Styles : IStyleResolver
        {
            public readonly System.Collections.Generic.Dictionary<string, PropertyBag> Bags = new System.Collections.Generic.Dictionary<string, PropertyBag>();
            public PropertyBag? Resolve(string styleName, string ws) => Bags.TryGetValue(styleName, out var b) ? b : null;
        }

        static (int Start, int End) Word(Paragraph p, int at, IStyleResolver? styles = null)
        {
            WordBreaks.WordAt(p, at, null, styles, out int s, out int e);
            return (s, e);
        }

        [Fact]
        public void A_word_in_two_writing_systems_is_two_double_click_words()
        {
            var p = Para("en", "en", "ab", "fr", "cd", "fr", " ef");
            Assert.Equal((0, 2), Word(p, 1));
            Assert.Equal((2, 4), Word(p, 2));
            Assert.Equal((2, 4), Word(p, 3));
            Assert.Equal((4, 5), Word(p, 4));
        }

        [Fact]
        public void A_change_of_the_spell_check_property_stops_the_word()
        {
            var p = Para("en", "en", "ab", "en", "cd", "en", "ef");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            Assert.Equal((0, 2), Word(p, 0));
            Assert.Equal((2, 4), Word(p, 3));
            Assert.Equal((4, 6), Word(p, 4));
        }

        static Paragraph Styled(Styles styles, string name, PropertyBag bag)
        {
            styles.Bags[name] = bag;
            var p = Para("en", "en", "un", "en", "believ", "en", "able");
            p.Runs[1].Props.SetStr(TextPropertyNumbers.NamedStyle, name);
            return p;
        }

        [Fact]
        public void A_style_that_only_changes_bold_keeps_the_word_whole()
        {
            var styles = new Styles();
            var p = Styled(styles, "Emphasis", new PropertyBag().SetInt(TextPropertyNumbers.Bold, 1));
            Assert.Equal((0, 12), Word(p, 3, styles));
            Assert.Equal((0, 12), Word(p, 0, styles));
        }

        [Fact]
        public void A_style_that_changes_the_spell_check_property_stops_the_word()
        {
            var styles = new Styles();
            var p = Styled(styles, "Code", new PropertyBag().SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot));
            Assert.Equal((0, 2), Word(p, 1, styles));
            Assert.Equal((2, 8), Word(p, 3, styles));
            Assert.Equal((8, 12), Word(p, 9, styles));
        }

        [Fact]
        public void A_style_that_changes_the_writing_system_stops_the_word()
        {
            var styles = new Styles();
            var p = Styled(styles, "Foreign", new PropertyBag().SetInt(TextPropertyNumbers.WritingSystem, 7));
            styles.Bags["Home"] = new PropertyBag().SetInt(TextPropertyNumbers.WritingSystem, 3);
            p.Runs[0].Props.SetStr(TextPropertyNumbers.NamedStyle, "Home");
            p.Runs[2].Props.SetStr(TextPropertyNumbers.NamedStyle, "Home");   // two styles that both have a writing system, and differ
            Assert.Equal((2, 8), Word(p, 3, styles));
        }

        [Fact]
        public void A_style_with_a_writing_system_beside_no_style_does_not_stop_the_word()
        {
            var styles = new Styles();
            var p = Styled(styles, "Foreign", new PropertyBag().SetInt(TextPropertyNumbers.WritingSystem, 7));
            p.Runs[2].Props.IntProps.Clear();
            p.Runs[0].Props.SetStr(TextPropertyNumbers.NamedStyle, "Foreign");    // styled, styled, plain: only the pair of styles is compared
            Assert.Equal((0, 12), Word(p, 0, styles));
        }

        [Fact]
        public void A_style_with_no_spell_check_equals_one_that_says_normal()
        {
            var styles = new Styles();
            var p = Styled(styles, "Plain", new PropertyBag().SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckNormal));
            Assert.Equal((0, 12), Word(p, 3, styles));        // the middle run's style says normal; its neighbours have no style, which is normal too
        }

        [Fact]
        public void With_no_style_resolver_a_change_of_style_name_does_not_stop_the_word()
        {
            var p = Para("en", "en", "ab", "en", "cd", "en", "ef");
            p.Runs[1].Props.SetStr(TextPropertyNumbers.NamedStyle, "Emphasis");
            Assert.Equal((0, 6), Word(p, 3));
        }

        [Fact]
        public void Runs_that_differ_only_in_other_properties_stay_one_word()
        {
            var p = Para("en", "en", "ab", "en", "cd");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1).SetInt(TextPropertyNumbers.ForeColor, 0x0000FF);
            Assert.Equal((0, 4), Word(p, 1));
            Assert.Equal((0, 4), Word(p, 3));
        }

        [Fact]
        public void A_white_space_run_is_cut_at_a_change_of_writing_system()
        {
            var p = Para("en", "en", "ab  ", "fr", "  cd");
            Assert.Equal((2, 4), Word(p, 2));    // the spaces before the change
            Assert.Equal((4, 6), Word(p, 4));    // the spaces after it
        }

        [Fact]
        public void The_other_word_rules_still_cross_the_boundary()
        {
            var p = Para("en", "en", "ab", "fr", "cd");
            Assert.True(WordBreaks.TryWordAt(p, 1, null, out int s, out int e));
            Assert.Equal((0, 4), (s, e));                                   // the word of a context request and of the spell checker
            Assert.Equal(4, WordBreaks.NextWordStart(p, 0, null));          // Ctrl+Right
            Assert.False(WordBreaks.Boundaries(p, null)[2]);                // whole-word find
        }
    }
}
