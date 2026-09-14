using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Parser;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RealParserAffixMorphologyTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AuthoredCircumfixProjectsBothSourceFormsInSurfaceOrder()
    {
        using var cache = pristine.NewScratch();
        (AuthoredMorph Root, AuthoredMorph Prefix, AuthoredMorph Suffix) authored = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => authored = AuthorCircumfix(cache));
        const string word = "dkat";
        Approve(cache, word, authored.Prefix, authored.Root, authored.Suffix);
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b", "d", "k");

        var row = await RunCorrectness(cache, word);

        Assert.Equal("covered", row.Correctness!.Status);
        Assert.Equal(1, row.Correctness.Expected);
        Assert.Equal(1, row.Correctness.Matched);
        Assert.False(row.Morphology!.InvalidShape);
        Assert.False(row.Morphology.Capped);
        Assert.False(row.Morphology.TimedOut);
        Assert.Empty(row.Morphology.Unavailable);
        var morphs = Assert.Single(row.Morphology.Analyses).Morphs;
        Assert.Equal(3, morphs.Count);
        AssertMorph(morphs[0], authored.Prefix.Form, authored.Prefix.Msa);
        AssertMorph(morphs[1], authored.Root.Form, authored.Root.Msa);
        AssertMorph(morphs[2], authored.Suffix.Form, authored.Suffix.Msa);
    }

    [RealParserFact]
    public async Task AuthoredInfixProjectsBeforeTheRootInInsertBeforeLastOrder()
    {
        using var cache = pristine.NewScratch();
        (AuthoredMorph Root, AuthoredMorph Infix) authored = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => authored = AuthorInfix(cache));
        const string word = "kia";
        Approve(cache, word, authored.Infix, authored.Root);
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b", "k");

        var row = await RunCorrectness(cache, word);

        Assert.Equal("covered", row.Correctness!.Status);
        Assert.Equal(1, row.Correctness.Expected);
        Assert.Equal(1, row.Correctness.Matched);
        Assert.False(row.Morphology!.InvalidShape);
        Assert.False(row.Morphology.Capped);
        Assert.False(row.Morphology.TimedOut);
        Assert.Empty(row.Morphology.Unavailable);
        var morphs = Assert.Single(row.Morphology.Analyses).Morphs;
        Assert.Equal(2, morphs.Count);
        AssertMorph(morphs[0], authored.Infix.Form, authored.Infix.Msa);
        AssertMorph(morphs[1], authored.Root.Form, authored.Root.Msa);
    }

    private static (AuthoredMorph Root, AuthoredMorph Prefix, AuthoredMorph Suffix) AuthorCircumfix(
        LcmCache cache)
    {
        var root = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "ka", "root");
        var circumfix = MakeEntry(cache, MoMorphTypeTags.kguidMorphCircumfix, "d- -t", "circumfix");
        var forms = circumfix.Entry.AlternateFormsOS.OfType<IMoAffixAllomorph>().ToArray();
        Assert.Equal(2, forms.Length);
        return (
            root,
            new AuthoredMorph(forms[0], circumfix.Msa, circumfix.Entry),
            new AuthoredMorph(forms[1], circumfix.Msa, circumfix.Entry));
    }

    private static (AuthoredMorph Root, AuthoredMorph Infix) AuthorInfix(LcmCache cache)
    {
        var root = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "ka", "root");
        var infix = MakeEntry(cache, MoMorphTypeTags.kguidMorphInfix, "i", "infix");
        var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
        cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
        environment.StringRepresentation = TsStringUtils.MakeString("/ k _ a", cache.DefaultVernWs);
        var allomorph = (IMoAffixAllomorph)infix.Entry.LexemeFormOA;
        allomorph.PositionRS.Clear();
        allomorph.PositionRS.Add(environment);
        return (root, infix);
    }

    private static AuthoredMorph MakeEntry(LcmCache cache, Guid morphType, string form, string gloss)
    {
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryFactory>().Create(
            services.GetInstance<IMoMorphTypeRepository>().GetObject(morphType),
            TsStringUtils.MakeString(form, cache.DefaultVernWs),
            gloss,
            new SandboxGenericMSA
            {
                MsaType = morphType == MoMorphTypeTags.kguidMorphStem ? MsaType.kStem : MsaType.kUnclassified,
            });
        var msa = entry.MorphoSyntaxAnalysesOC.Single();
        return new AuthoredMorph(entry.LexemeFormOA, msa, entry);
    }

    private static void Approve(LcmCache cache, string word, params AuthoredMorph[] morphs)
    {
        var services = cache.ServiceLocator;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var wordform = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString(word, cache.DefaultVernWs));
            var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            foreach (var morph in morphs)
            {
                var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = morph.Form;
                bundle.MsaRA = morph.Msa;
            }
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
        });
    }

    private static async Task<WordAnalysis> RunCorrectness(LcmCache cache, string word)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-affix-morphology-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(root));
            using var invoker = new PanGlossInvoker();
            var assessor = new PanGlossAssessor(paths, invoker);
            var produced = await assessor.ProduceAsync(
                new([word], [AssessmentKind.Correctness], TimeSpan.FromSeconds(5)),
                Path.GetDirectoryName(cache.ProjectId.Path)!, CancellationToken.None);
            var batch = Assert.IsType<AssessmentRaw.Batch>(produced.Single().Raw).Analysis;
            return Assert.Single(batch.Words);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void AssertMorph(ParseMorph actual, IMoForm form, IMoMorphSynAnalysis msa)
    {
        Assert.Equal(form.Guid.ToString("D"), actual.Form);
        Assert.Equal(msa.Guid.ToString("D"), actual.Msa);
        Assert.Null(actual.InflType);
        Assert.Null(actual.GuessedString);
    }

    private sealed record AuthoredMorph(IMoForm Form, IMoMorphSynAnalysis Msa, ILexEntry Entry);
}
