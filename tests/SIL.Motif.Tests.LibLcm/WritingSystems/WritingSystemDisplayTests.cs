using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.WritingSystems;
using SIL.Motif.Projection;
using SIL.Motif.Tests.TestFixtures;
using SIL.WritingSystems;
using Xunit;

namespace SIL.Motif.Tests.WritingSystems;

[Collection(LcmCacheTestCollection.Name)]
public sealed class WritingSystemDisplayTests(PristineProjectFixture pristine)
{
    [Fact]
    public void CapturesLdmlAndEffectiveStyleSizesWithoutResolvingInstalledFonts()
    {
        using var cache = pristine.NewScratch();
        var systems = WritingSystemDisplayReader.Read(cache);
        var rtl = Assert.Single(systems, ws => ws.Id == SeededProject.RightToLeftTag);
        Assert.True(rtl.RightToLeft);
        Assert.Equal(SeededProject.MissingFont, rtl.FontFamily);
        Assert.Equal(SeededProject.FontFeatures, rtl.FontFeatures);
        Assert.Equal("Ar", rtl.Abbreviation);
        Assert.Equal(2, rtl.Position);
        Assert.False(rtl.IsDefault);
        Assert.Equal(SeededProject.RightToLeftSizePoints, rtl.StyleSizes["Normal"]);
        Assert.All(rtl.StyleSizes.Values, size => Assert.Equal(SeededProject.RightToLeftSizePoints, size));
        Assert.Equal(10, systems[0].StyleSizes["Normal"]);
        foreach (var kind in new[] { WritingSystemKind.Vernacular, WritingSystemKind.Analysis })
        {
            var list = systems.Where(ws => ws.Kind == kind).ToArray();
            Assert.Equal(Enumerable.Range(0, list.Length), list.Select(ws => ws.Position));
            Assert.True(list[0].IsDefault);
            Assert.Single(list, ws => ws.IsDefault);
        }
    }

    [Fact]
    public void InheritedStyleSizesAndSharedListMembershipRemainDistinct()
    {
        using var cache = pristine.NewScratch();
        cache.ServiceLocator.WritingSystemManager.GetOrSet("ar-x-display", out var rtl);
        rtl.DefaultFont = new FontDefinition(SeededProject.MissingFont)
            { Engines = FontEngines.Graphite, Features = "1051=1" };
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(rtl);
            cache.ServiceLocator.WritingSystems.CurrentVernacularWritingSystems.Add(rtl);
            cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Add(rtl);
            cache.ServiceLocator.WritingSystems.CurrentAnalysisWritingSystems.Insert(0, rtl);
            var normal = cache.LangProject.StylesOC.Single(style => style.Name == "Normal");
            var rules = normal.Rules.GetBldr();
            var font = new FontInfo();
            font.m_fontSize.ExplicitValue = 18000;
            BaseStyleInfo.SaveFontOverridesToBuilder(new Dictionary<int, FontInfo> { [rtl.Handle] = font }, rules);
            normal.Rules = rules.GetTextProps();
            var paragraph = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
            cache.LangProject.StylesOC.Add(paragraph);
            paragraph.Name = "Paragraph";
            paragraph.BasedOnRA = normal;
            paragraph.Rules = TsStringUtils.MakePropsBldr().GetTextProps();
        });
        var memberships = WritingSystemDisplayReader.Read(cache).Where(ws => ws.Id == rtl.Id).ToArray();
        Assert.Equal(2, memberships.Length);
        Assert.True(memberships[1].IsDefault);
        Assert.Equal(WritingSystemKind.Analysis, memberships[1].Kind);
        Assert.All(memberships, ws =>
        {
            Assert.Equal("1051=1", ws.FontFeatures);
            Assert.Equal(18, ws.StyleSizes["Paragraph"]);
        });
    }

    [Fact]
    public void ProjectStyleOverridesSupplyExactFontFeaturesAndUnscaledPoints()
    {
        using var cache = pristine.NewScratch();
        var rtl = cache.ServiceLocator.WritingSystemManager.Get(SeededProject.RightToLeftTag);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var normal = cache.LangProject.StylesOC.Single(style => style.Name == "Normal");
            var paragraph = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
            cache.LangProject.StylesOC.Add(paragraph);
            paragraph.Name = "Authored paragraph";
            paragraph.BasedOnRA = normal;
            var rules = TsStringUtils.MakePropsBldr();
            rules.SetIntPropValues((int)FwTextPropType.ktptFontSize, (int)FwTextPropVar.ktpvMilliPoint, 17250);
            rules.SetStrPropValue((int)FwTextPropType.ktptFontFamily, "Common style font");
            rules.SetStrPropValue((int)FwTextPropType.ktptFontVariations, "cv01=3");
            var font = new FontInfo();
            font.m_fontSize.ExplicitValue = 29750;
            font.m_fontName.ExplicitValue = "Writing-system style font";
            font.m_features.ExplicitValue = "smcp=0,1051=1";
            BaseStyleInfo.SaveFontOverridesToBuilder(new Dictionary<int, FontInfo> { [rtl.Handle] = font }, rules);
            paragraph.Rules = rules.GetTextProps();
        });
        var displays = WritingSystemDisplayReader.Read(cache);
        var arabic = Assert.Single(displays, display => display.Id == rtl.Id);
        Assert.Equal(29.75, arabic.StyleSizes["Authored paragraph"]);
        Assert.Equal(new WritingSystemStyleFont("Writing-system style font", "smcp=0,1051=1"),
            arabic.StyleFonts["Authored paragraph"]);
        var other = displays[0];
        Assert.Equal(17.25, other.StyleSizes["Authored paragraph"]);
        Assert.Equal(new WritingSystemStyleFont("Common style font", "cv01=3"),
            other.StyleFonts["Authored paragraph"]);
    }

    [Fact]
    public void ExplicitStyleFontDoesNotAcquireWritingSystemDefaultFeatures()
    {
        using var cache = pristine.NewScratch();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var normal = cache.LangProject.StylesOC.Single(style => style.Name == "Normal");
            var rules = normal.Rules.GetBldr();
            rules.SetStrPropValue((int)FwTextPropType.ktptFontFamily, "Explicit style font");
            normal.Rules = rules.GetTextProps();
        });
        var arabic = Assert.Single(WritingSystemDisplayReader.Read(cache),
            display => display.Id == SeededProject.RightToLeftTag);
        Assert.Equal(SeededProject.FontFeatures, arabic.FontFeatures);
        Assert.Equal(new WritingSystemStyleFont("Explicit style font", ""), arabic.StyleFonts["Normal"]);
        Assert.Equal(arabic.StyleFonts["Normal"], arabic.StyleFonts["Dictionary-Headword"]);
    }

    [Fact]
    public void MixedRunsHaveNoSingleWritingSystemAndBestAlternativeKeepsItsActualTag()
    {
        using var cache = pristine.NewScratch();
        var builder = TsStringUtils.MakeString("abc", cache.DefaultVernWs).GetBldr();
        builder.ReplaceTsString(3, 3, TsStringUtils.MakeString("xyz", cache.DefaultAnalWs));
        Assert.Null(WritingSystemTextReader.SingleId(cache, builder.GetString()));
        var sense = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(pristine.Seed.FirstSenseId);
        var gloss = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss);
        Assert.Equal(SeededProject.FirstGloss, gloss.Text);
        Assert.Equal(NewLangProjFixture.AnalysisTag, gloss.WritingSystem);
    }
}
