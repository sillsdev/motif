using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProhibitionParserProject
{
    internal static (ILexEntry Prefix, ILexEntry Stem) Seed(LcmCache cache)
    {
        RealParserProject.PrepareForParsing(cache, "p", "a", "b", "g", "o");
        ILexEntry prefix = null!, stem = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var noun = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(noun);
            noun.Name.set_String(cache.DefaultAnalWs, "Noun");
            var slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
            noun.AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, "S1");
            slot.Optional = true;
            var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
            noun.AffixTemplatesOS.Add(template);
            template.Name.set_String(cache.DefaultAnalWs, "T1");
            template.Final = true;
            template.PrefixSlotsRS.Add(slot);
            var types = services.GetInstance<IMoMorphTypeRepository>();
            prefix = services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphPrefix),
                TsStringUtils.MakeString("pa", cache.DefaultVernWs), "prefix", SandboxGenericMSA.Create(MsaType.kInfl, noun));
            ((IMoInflAffMsa)prefix.MorphoSyntaxAnalysesOC.Single()).SlotsRC.Add(slot);
            stem = services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("bago", cache.DefaultVernWs), "stem", SandboxGenericMSA.Create(MsaType.kStem, noun));
        });
        new FwDataProjectLoader().Save(cache);
        return (prefix, stem);
    }

}
