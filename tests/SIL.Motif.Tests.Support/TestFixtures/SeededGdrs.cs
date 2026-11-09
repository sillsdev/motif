using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.Gdr;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Writes Grammar Design Records into a run-time project: GDR-1 with a right-to-left paragraph, a character style, a
/// hyperlink and an object run, and a sub-item GDR-1a under it; GDR-2 with no Discussion; and a record with neither an
/// abbreviation nor an abstract.
/// </summary>
public static class SeededGdrs
{
    public static void Seed(LcmCache cache)
    {
        var services = cache.ServiceLocator;
        var analysis = cache.DefaultAnalWs;
        var arabic = services.WritingSystemManager.Get(SeededProject.RightToLeftTag).Handle;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var emphasis = services.GetInstance<IStStyleFactory>().Create();
            cache.LangProject.StylesOC.Add(emphasis);
            emphasis.Name = "Emphasis";
            emphasis.Type = StyleType.kstCharacter;
            var italic = TsStringUtils.MakePropsBldr();
            italic.SetIntPropValues((int)FwTextPropType.ktptItalic, (int)FwTextPropVar.ktpvEnum, 1);
            emphasis.Rules = italic.GetTextProps();

            var list = services.GetInstance<ICmPossibilityListFactory>().Create();
            cache.LangProject.CheckListsOC.Add(list);
            list.Name.set_String(analysis, GdrReader.ListName);
            var first = AddRecord(cache, list.PossibilitiesOS, "GDR-1", "title-1", "abstract-1", stText =>
            {
                var contents = TsStringUtils.MakeStrBldr();
                contents.Replace(0, 0, "x", TsStringUtils.MakeProps("", analysis));
                var character = TsStringUtils.MakePropsBldr();
                character.SetIntPropValues((int)FwTextPropType.ktptWs, 0, analysis);
                character.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, "Emphasis");
                contents.Replace(contents.Length, contents.Length, "y", character.GetTextProps());
                var link = TsStringUtils.MakePropsBldr();
                link.SetIntPropValues((int)FwTextPropType.ktptWs, 0, analysis);
                link.SetStrPropValue((int)FwTextPropType.ktptObjData, HyperlinkData);
                contents.Replace(contents.Length, contents.Length, "link", link.GetTextProps());
                var embedded = TsStringUtils.MakePropsBldr();
                embedded.SetIntPropValues((int)FwTextPropType.ktptWs, 0, analysis);
                embedded.SetStrPropValue((int)FwTextPropType.ktptObjData, ObjectData);
                contents.Replace(contents.Length, contents.Length, "￼", embedded.GetTextProps());
                AddParagraph(cache, stText, contents.GetString(), "Normal", rightToLeft: false);
                AddParagraph(cache, stText, TsStringUtils.MakeString("ar-text", arabic), "Normal", rightToLeft: true);
            });
            AddRecord(cache, first.SubPossibilitiesOS, "GDR-1a", "title-1a", "abstract-1a",
                stText => AddParagraph(cache, stText, TsStringUtils.MakeString("z", analysis), "Normal", rightToLeft: false));
            AddRecord(cache, list.PossibilitiesOS, "GDR-2", "title-2", "abstract-2", discussion: null);
            AddRecord(cache, list.PossibilitiesOS, "", "title-3", "", discussion: null);
        });
    }

    /// <summary>The link target FieldWorks stores for an external hyperlink: a type character, then the address.</summary>
    public static readonly string HyperlinkData = (char)FwObjDataTypes.kodtExternalPathName + "https://example.invalid/x";

    /// <summary>The data of an object run: a type character, then eight characters of GUID.</summary>
    public static readonly string ObjectData = (char)FwObjDataTypes.kodtOwnNameGuidHot + "01234567";

    /// <summary>Adds a second record that repeats GDR-2's abbreviation.</summary>
    public static void SeedDuplicateAbbreviation(LcmCache cache) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        AddRecord(cache, Records(cache).PossibilitiesOS, "gdr-2", "title-4", "abstract-4", discussion: null));

    /// <summary>Adds a second list with the records' list name.</summary>
    public static void SeedSecondList(LcmCache cache) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
    {
        var list = cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
        cache.LangProject.CheckListsOC.Add(list);
        list.Name.set_String(cache.DefaultAnalWs, GdrReader.ListName);
    });

    /// <summary>Makes two styles each based on the other.</summary>
    public static void SeedStyleCycle(LcmCache cache) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
    {
        var styles = new[] { "Loop A", "Loop B" }.Select(name =>
        {
            var style = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
            cache.LangProject.StylesOC.Add(style);
            style.Name = name;
            style.Type = StyleType.kstCharacter;
            return style;
        }).ToArray();
        styles[0].BasedOnRA = styles[1];
        styles[1].BasedOnRA = styles[0];
    });

    private static ICmPossibilityList Records(LcmCache cache) => cache.LangProject.CheckListsOC
        .Single(list => list.Name.get_String(cache.DefaultAnalWs).Text == GdrReader.ListName);

    private static ICmPossibility AddRecord(LcmCache cache, ILcmOwningSequence<ICmPossibility> owner, string number, string title,
        string abstractText, Action<IStText>? discussion)
    {
        var services = cache.ServiceLocator;
        var record = services.GetInstance<ICmPossibilityFactory>().Create();
        owner.Add(record);
        record.Name.set_String(cache.DefaultAnalWs, title);
        if (number.Length > 0) record.Abbreviation.set_String(cache.DefaultAnalWs, number);
        if (abstractText.Length > 0)
            record.Description.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString(abstractText, cache.DefaultAnalWs));
        if (discussion is null) return record;
        record.DiscussionOA = services.GetInstance<IStTextFactory>().Create();
        discussion(record.DiscussionOA);
        return record;
    }

    private static void AddParagraph(LcmCache cache, IStText stText, ITsString contents, string style, bool rightToLeft)
    {
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
        stText.ParagraphsOS.Add(paragraph);
        paragraph.Contents = contents;
        var rules = TsStringUtils.MakePropsBldr();
        rules.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, style);
        if (rightToLeft) rules.SetIntPropValues((int)FwTextPropType.ktptRightToLeft, (int)FwTextPropVar.ktpvEnum, 1);
        paragraph.StyleRules = rules.GetTextProps();
    }
}
