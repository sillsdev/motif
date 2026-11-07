using System.Collections.Generic;
using Xunit;

namespace SIL.TextEditing.Tests
{
    public class FormatResolverTests
    {
        sealed class Styles : IStyleResolver
        {
            public Dictionary<string, PropertyBag> ByName { get; } = new Dictionary<string, PropertyBag>();
            public List<string> Asked { get; } = new List<string>();
            public PropertyBag? Resolve(string styleName, string ws)
            {
                Asked.Add(styleName + "/" + ws);
                return ByName.TryGetValue(styleName, out var b) ? b : null;
            }
        }

        static Paragraph Para(string style = "", Run? run = null)
        {
            var p = new Paragraph { Style = style };
            p.Runs.Add(run ?? new Run { Text = "x" });
            return p;
        }

        [Fact]
        public void Without_a_resolver_the_run_bag_alone_formats_the_run_on_the_writing_system_size()
        {
            var run = new Run { Text = "x" };
            run.Props.SetInt(TextPropertyNumbers.Bold, 1).SetInt(TextPropertyNumbers.ForeColor, 0x0000FF);
            var f = FormatResolver.ResolveChar(Para("Heading", run), run, null, 12);
            Assert.Equal(700, f.Weight);
            Assert.Equal(12, f.SizePt);
            Assert.Equal(0x0000FF, f.ForeColor);
        }

        [Fact]
        public void Layers_apply_paragraph_style_then_character_style_then_run_and_the_resolver_is_asked_per_writing_system()
        {
            var styles = new Styles();
            styles.ByName["Para"] = new PropertyBag().SetInt(TextPropertyNumbers.FontSize, 14000, 1).SetInt(TextPropertyNumbers.Italic, 1);
            styles.ByName["Emph"] = new PropertyBag().SetInt(TextPropertyNumbers.Bold, 1).SetInt(TextPropertyNumbers.Italic, 0);
            var run = new Run { Ws = "fr", Text = "x" };
            run.Props.SetStr(TextPropertyNumbers.NamedStyle, "Emph").SetInt(TextPropertyNumbers.FontSize, 18000, 1);
            var f = FormatResolver.ResolveChar(Para("Para", run), run, styles, 10);
            Assert.Equal(18, f.SizePt);
            Assert.True(f.Bold);
            Assert.False(f.Italic);
            Assert.Equal(new[] { "Para/fr", "Emph/fr" }, styles.Asked);
        }

        [Fact]
        public void Invert_toggles_against_the_style_and_relative_and_named_sizes_work_against_the_current_size()
        {
            var styles = new Styles();
            styles.ByName["Bolded"] = new PropertyBag().SetInt(TextPropertyNumbers.Bold, 1);
            var run = new Run { Text = "x" };
            run.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleInvert).SetInt(TextPropertyNumbers.FontSize, 5000, TextPropertyNumbers.VariationRelative);
            var f = FormatResolver.ResolveChar(Para("Bolded", run), run, styles, 12);
            Assert.False(f.Bold);
            Assert.Equal(6, f.SizePt);

            var larger = new Run { Text = "x" };
            larger.Props.SetInt(TextPropertyNumbers.FontSize, 8, TextPropertyNumbers.VariationEnum);
            Assert.Equal(14, FormatResolver.ResolveChar(Para(), larger, null, 12).SizePt);
            var xx = new Run { Text = "x" };
            xx.Props.SetInt(TextPropertyNumbers.FontSize, 0, TextPropertyNumbers.VariationEnum);
            Assert.Equal(5, FormatResolver.ResolveChar(Para(), xx, null, 12).SizePt);
        }

        [Fact]
        public void Numeric_bold_weights_are_clamped_and_below_550_is_not_bold()
        {
            var run = new Run { Text = "x" };
            run.Props.SetInt(TextPropertyNumbers.Bold, 1200);
            Assert.Equal(900, FormatResolver.ResolveChar(Para(), run, null, 12).Weight);
            run.Props.SetInt(TextPropertyNumbers.Bold, 500);
            Assert.False(FormatResolver.ResolveChar(Para(), run, null, 12).Bold);
        }

        [Fact]
        public void Special_colours_and_default_font_markup_resolve_as_FieldWorks_does()
        {
            var run = new Run { Text = "x" };
            run.Props.SetInt(TextPropertyNumbers.ForeColor, unchecked((int)0xC0000000)).SetStr(TextPropertyNumbers.FontFamily, "<default serif>");
            var f = FormatResolver.ResolveChar(Para(), run, null, 12);
            Assert.Null(f.ForeColor);
            Assert.Equal("Times New Roman", f.FontFamily);
            Assert.True(CharFormat.TryColor(0x00FF8040, out var r, out var g, out var b));
            Assert.Equal((0x40, 0x80, 0xFF), (r, g, b));
        }

        [Fact]
        public void Paragraph_properties_convert_millipoints_and_line_height_kinds()
        {
            var styles = new Styles();
            styles.ByName["P"] = new PropertyBag().SetInt(TextPropertyNumbers.LeadingIndent, 36000, 1).SetInt(TextPropertyNumbers.FirstIndent, -18000, 1)
                .SetInt(TextPropertyNumbers.LineHeight, 15000, 1).SetInt(TextPropertyNumbers.Align, 5);
            var p = Para("P");
            p.Props.SetInt(TextPropertyNumbers.LineHeight, -20000, 1);
            var f = FormatResolver.ResolveParagraph(p, styles, 10);
            Assert.Equal(36, f.LeadingIndent);
            Assert.Equal(-18, f.FirstIndent);
            Assert.Equal(5, f.Align);
            Assert.Equal(20, f.LineHeight);
            Assert.True(f.ExactLineHeight);

            var rel = Para();
            rel.Props.SetInt(TextPropertyNumbers.LineHeight, 15000, TextPropertyNumbers.VariationRelative);
            var fr = FormatResolver.ResolveParagraph(rel, null, 10);
            Assert.Equal(21, fr.LineHeight, 6);
            Assert.False(fr.ExactLineHeight);
        }
    }
}
