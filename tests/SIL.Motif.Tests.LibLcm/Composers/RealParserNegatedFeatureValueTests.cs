using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Composers;

/// <summary>What PanGloss does with a stored negated feature value, which FieldWorks' own loader cannot read.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserNegatedFeatureValueTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task ANegatedValueOnAStemMakesPanGlossRefuseTheProjectAsHCLoaderDoes()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "k", "i", "t", "a", "b", "u");
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            cache.LangProject.MsFeatureSystemOA ??= services.GetInstance<IFsFeatureSystemFactory>().Create();
            var feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            feature.Name.set_String(cache.DefaultAnalWs, "gender");
            feature.Abbreviation.set_String(cache.DefaultAnalWs, "gender");
            var value = services.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
            value.Abbreviation.set_String(cache.DefaultAnalWs, "g1");
            value.Name.set_String(cache.DefaultAnalWs, "g1");
            var noun = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(noun);
            noun.Name.set_String(cache.DefaultAnalWs, "Noun");
            var closedStem = Stem(cache, "kitabu", noun);
            closedStem.MsFeaturesOA = services.GetInstance<IFsFeatStrucFactory>().Create();
            var closed = services.GetInstance<IFsClosedValueFactory>().Create();
            closedStem.MsFeaturesOA.FeatureSpecsOC.Add(closed);
            closed.FeatureRA = feature;
            closed.ValueRA = value;
            var negatedStem = Stem(cache, "kibu", noun);
            negatedStem.MsFeaturesOA = services.GetInstance<IFsFeatStrucFactory>().Create();
            var negated = services.GetInstance<IFsNegatedValueFactory>().Create();
            negatedStem.MsFeaturesOA.FeatureSpecsOC.Add(negated);
            negated.FeatureRA = feature;
            negated.ValueRA = value;
        });
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        // A negated value declines the whole project.
        foreach (var word in new[] { "kitabu", "kibu" })
        {
            var outcome = Assert.IsType<PanGlossTraceOutcome.Declined>(
                await tracer.TraceAsync(path, word, CancellationToken.None));
            output.WriteLine(word + ": " + outcome.Detail);
        }
    }

    private static IMoStemMsa Stem(LcmCache cache, string form, IPartOfSpeech category)
    {
        var type = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(type,
            TsStringUtils.MakeString(form, cache.DefaultVernWs), form,
            SandboxGenericMSA.Create(MsaType.kStem, category));
        return (IMoStemMsa)entry.MorphoSyntaxAnalysesOC.Single();
    }
}
