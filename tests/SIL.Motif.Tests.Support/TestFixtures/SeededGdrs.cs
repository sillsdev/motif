using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.Gdr;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Writes two Grammar Design Records into a run-time project: GDR-1 with a right-to-left paragraph and a character style, and GDR-2 with no Discussion.</summary>
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
            AddRecord(cache, list, "GDR-1", "Word boundaries", "Where one word ends.", stText =>
            {
                var contents = TsStringUtils.MakeStrBldr();
                contents.Replace(0, 0, "Words end at ", TsStringUtils.MakeProps("", analysis));
                var character = TsStringUtils.MakePropsBldr();
                character.SetIntPropValues((int)FwTextPropType.ktptWs, 0, analysis);
                character.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, "Emphasis");
                contents.Replace(contents.Length, contents.Length, "spaces", character.GetTextProps());
                AddParagraph(cache, stText, contents.GetString(), "Normal", rightToLeft: false);
                AddParagraph(cache, stText, TsStringUtils.MakeString("ar-text", arabic), "Normal", rightToLeft: true);
            });
            AddRecord(cache, list, "GDR-2", "Spelling variants", "Which spelling wins.", discussion: null);
        });
    }

    private static void AddRecord(LcmCache cache, ICmPossibilityList list, string number, string title, string abstractText,
        Action<IStText>? discussion)
    {
        var services = cache.ServiceLocator;
        var record = services.GetInstance<ICmPossibilityFactory>().Create();
        list.PossibilitiesOS.Add(record);
        record.Name.set_String(cache.DefaultAnalWs, title);
        record.Abbreviation.set_String(cache.DefaultAnalWs, number);
        record.Description.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString(abstractText, cache.DefaultAnalWs));
        if (discussion is null) return;
        record.DiscussionOA = services.GetInstance<IStTextFactory>().Create();
        discussion(record.DiscussionOA);
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
