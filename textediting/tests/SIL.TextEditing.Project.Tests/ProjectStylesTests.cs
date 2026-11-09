using System;
using System.Collections.Generic;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.TextEditing.Markdown;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class ProjectStylesTests
    {
        [Fact]
        public void Paragraph_fonts_sizes_and_character_inheritance_resolve_through_the_host_interfaces()
        {
            using (var project = new ProjectFixture())
            {
                var paragraph = project.Style("Body", StyleType.kstParagraph, null, b =>
                {
                    b.SetStrPropValue(1, "Charis SIL");
                    b.SetIntPropValues(6, 1, 17250);
                    b.SetIntPropValues(21, 1, 3000);
                });
                var emphasis = project.Style("Emphasis", StyleType.kstCharacter, null, b => b.SetIntPropValues(2, 3, 1));
                project.Style("Strong emphasis", StyleType.kstCharacter, emphasis, b => b.SetIntPropValues(3, 3, 1));
                var styles = new ProjectStyles(project.Cache);
                IStyleCatalog catalog = styles;
                IKnownStyles names = styles;
                IStyleResolver resolver = styles;
                Assert.True(catalog.IsParagraphStyle(paragraph.Name));
                Assert.False(catalog.IsCharacterStyle(paragraph.Name));
                Assert.True(catalog.IsCharacterStyle("Strong emphasis"));
                Assert.False(catalog.IsParagraphStyle("Strong emphasis"));
                Assert.Contains("Body", names.ParagraphStyleNames);
                Assert.Contains("Strong emphasis", names.CharacterStyleNames);
                var bag = resolver.Resolve("Body", "en")!;
                Assert.Equal("Charis SIL", bag.StrProps[1]);
                Assert.Equal(new IntPropValue(1, 17250), bag.IntProps[6]);
                Assert.Equal(new IntPropValue(1, 3000), bag.IntProps[21]);
                var run = new Run { Text = "styled", Ws = "en", Props = new PropertyBag().SetStr(133, "Strong emphasis") };
                var format = FormatResolver.ResolveChar(new Paragraph { Style = "Body" }, run, styles, 10);
                Assert.Equal("Charis SIL", format.FontFamily);
                Assert.Equal(17.25, format.SizePt);
                Assert.True(format.Italic && format.Bold);
                Assert.Null(resolver.Resolve("Absent", "en"));
                Assert.False(catalog.IsParagraphStyle("Absent"));
                Assert.False(catalog.IsCharacterStyle("Absent"));
            }
        }

        [Fact]
        public void Inherited_writing_system_overrides_keep_other_properties_and_allow_child_overrides()
        {
            using (var project = new ProjectFixture())
            {
                int ar = project.Cache.DefaultVernWs;
                var parent = project.Style("Body", StyleType.kstParagraph, null, b =>
                {
                    b.SetStrPropValue(1, "Common font");
                    b.SetStrPropValue(130, "cv01=2");
                    b.SetIntPropValues(6, 1, 17250);
                    var font = new FontInfo();
                    font.m_fontName.ExplicitValue = "Arabic font";
                    font.m_fontSize.ExplicitValue = 29750;
                    font.m_features.ExplicitValue = "smcp=0,1051=1";
                    BaseStyleInfo.SaveFontOverridesToBuilder(new Dictionary<int, FontInfo> { [ar] = font }, b);
                });
                var child = project.Style("Child", StyleType.kstParagraph, parent, b =>
                {
                    b.SetIntPropValues(3, 3, 1);
                    var font = new FontInfo();
                    font.m_fontSize.ExplicitValue = 24000;
                    BaseStyleInfo.SaveFontOverridesToBuilder(new Dictionary<int, FontInfo> { [ar] = font }, b);
                });
                var styles = new ProjectStyles(project.Cache);
                var arabic = styles.Resolve(child.Name, "ar")!;
                Assert.Equal("Arabic font", arabic.StrProps[1]);
                Assert.Equal("smcp=0,1051=1", arabic.StrProps[130]);
                Assert.Equal(new IntPropValue(1, 24000), arabic.IntProps[6]);
                Assert.Equal(1, arabic.IntProps[3].Value);
                var english = styles.Resolve(child.Name, "en")!;
                Assert.Equal("Common font", english.StrProps[1]);
                Assert.Equal("cv01=2", english.StrProps[130]);
                Assert.Equal(17250, english.IntProps[6].Value);
                Assert.True(english.ContentEquals(styles.Resolve(child.Name, "qaa-x-unknown")!));
            }
        }

        [Fact]
        public void Child_common_properties_override_inherited_writing_system_properties_only_where_explicit()
        {
            using (var project = new ProjectFixture())
            {
                var parent = project.Style("Parent", StyleType.kstParagraph, null, b =>
                {
                    var font = new FontInfo();
                    font.m_fontName.ExplicitValue = "Arabic font";
                    font.m_fontSize.ExplicitValue = 24000;
                    BaseStyleInfo.SaveFontOverridesToBuilder(new Dictionary<int, FontInfo> { [project.Cache.DefaultVernWs] = font }, b);
                });
                project.Style("Child", StyleType.kstParagraph, parent, b => b.SetIntPropValues(6, 1, 18000));
                var bag = new ProjectStyles(project.Cache).Resolve("Child", "ar")!;
                Assert.Equal("Arabic font", bag.StrProps[1]);
                Assert.Equal(18000, bag.IntProps[6].Value);
            }
        }

        [Fact]
        public void Font_overrides_include_toggles_decoration_offset_and_BGR_colours()
        {
            using (var project = new ProjectFixture())
            {
                project.Style("Decorated", StyleType.kstCharacter, null, b =>
                {
                    var font = new FontInfo();
                    font.m_bold.ExplicitValue = true;
                    font.m_italic.ExplicitValue = false;
                    font.m_superSub.ExplicitValue = FwSuperscriptVal.kssvSuper;
                    font.m_underline.ExplicitValue = FwUnderlineType.kuntDouble;
                    font.m_offset.ExplicitValue = 2500;
                    font.m_fontColor.ExplicitValue = System.Drawing.Color.FromArgb(12, 34, 56);
                    font.m_backColor.ExplicitValue = System.Drawing.Color.Transparent;
                    font.m_underlineColor.ExplicitValue = System.Drawing.Color.Red;
                    BaseStyleInfo.SaveFontOverridesToBuilder(new Dictionary<int, FontInfo> { [project.Cache.DefaultVernWs] = font }, b);
                });
                var bag = new ProjectStyles(project.Cache).Resolve("Decorated", "ar")!;
                Assert.Equal(new IntPropValue(3, 1), bag.IntProps[3]);
                Assert.Equal(new IntPropValue(3, 0), bag.IntProps[2]);
                Assert.Equal(1, bag.IntProps[4].Value);
                Assert.Equal(4, bag.IntProps[5].Value);
                Assert.Equal(new IntPropValue(1, 2500), bag.IntProps[7]);
                Assert.Equal(12 | 34 << 8 | 56 << 16, bag.IntProps[8].Value);
                Assert.Equal(unchecked((int)0xC0000000), bag.IntProps[9].Value);
                Assert.Equal(255, bag.IntProps[10].Value);
            }
        }

        [Fact]
        public void A_styles_default_font_uses_the_requested_writing_system()
        {
            using (var project = new ProjectFixture())
            {
                project.Change(() => project.Cache.ServiceLocator.WritingSystemManager.Get("ar").DefaultFont =
                    new SIL.WritingSystems.FontDefinition("Scheherazade New"));
                project.Style("Default font", StyleType.kstParagraph, null, b => b.SetStrPropValue(1, StyleServices.DefaultFont));
                var bag = new ProjectStyles(project.Cache).Resolve("Default font", "ar")!;
                Assert.Equal("Scheherazade New", bag.StrProps[1]);
            }
        }

        [Fact]
        public void Style_edits_are_seen_on_the_next_read_and_results_do_not_modify_the_project()
        {
            using (var project = new ProjectFixture())
            {
                var style = project.Style("Before", StyleType.kstParagraph, null, b => b.SetIntPropValues(6, 1, 12000));
                var styles = new ProjectStyles(project.Cache);
                var names = styles.ParagraphStyleNames;
                var bag = styles.Resolve("Before", "en")!;
                bag.SetInt(6, 99000, 1);
                Assert.Equal(12000, styles.Resolve("Before", "en")!.IntProps[6].Value);
                project.Change(() =>
                {
                    style.Name = "After";
                    var builder = style.Rules.GetBldr();
                    builder.SetIntPropValues(6, 1, 18000);
                    style.Rules = builder.GetTextProps();
                });
                Assert.Contains("Before", names);
                Assert.DoesNotContain("After", names);
                Assert.Contains("After", styles.ParagraphStyleNames);
                Assert.Null(styles.Resolve("Before", "en"));
                Assert.Equal(18000, styles.Resolve("After", "en")!.IntProps[6].Value);
                Assert.Equal(18000, style.Rules.GetIntPropValues(6, out _));
            }
        }

        [Fact]
        public void Common_properties_keep_their_variations_and_carried_values()
        {
            using (var project = new ProjectFixture())
            {
                project.Style("Relative", StyleType.kstCharacter, null, b =>
                {
                    b.SetIntPropValues(6, 2, 15000);
                    b.SetIntPropValues(3, 3, 2);
                    b.SetIntPropValues(999, 7, 42);
                    b.SetStrPropValue(998, "carried");
                    var font = new FontInfo();
                    font.m_fontName.ExplicitValue = "Arabic font";
                    BaseStyleInfo.SaveFontOverridesToBuilder(new Dictionary<int, FontInfo> { [project.Cache.DefaultVernWs] = font }, b);
                });
                var bag = new ProjectStyles(project.Cache).Resolve("Relative", "ar")!;
                Assert.Equal(new IntPropValue(2, 15000), bag.IntProps[6]);
                Assert.Equal(new IntPropValue(3, 2), bag.IntProps[3]);
                Assert.Equal(new IntPropValue(7, 42), bag.IntProps[999]);
                Assert.Equal("carried", bag.StrProps[998]);
                Assert.Equal("Arabic font", bag.StrProps[1]);
            }
        }

        [Fact]
        public void Cyclic_style_inheritance_fails_instead_of_recursing_forever()
        {
            using (var project = new ProjectFixture())
            {
                var first = project.Style("First", StyleType.kstParagraph, null, b => { });
                var second = project.Style("Second", StyleType.kstParagraph, first, b => { });
                project.Change(() => first.BasedOnRA = second);
                var error = Assert.Throws<InvalidOperationException>(() => new ProjectStyles(project.Cache).Resolve("First", "en"));
                Assert.Contains("cycle", error.Message);
            }
        }

        [Fact]
        public void A_null_cache_is_rejected() => Assert.Throws<ArgumentNullException>(() => new ProjectStyles(null!));
    }
}
