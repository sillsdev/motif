using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.Assess;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RealParserBatchTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task SharedMorphologyRetainsSameOwnerAllomorphsAndMatchesAllApprovedReadings()
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        IMoStemAllomorph alternate = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            alternate = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(alternate);
            alternate.MorphTypeRA = entry.LexemeFormOA.MorphTypeRA;
            alternate.Form.set_String(cache.DefaultVernWs, SeededProject.FirstForm);
            var word = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString(SeededProject.FirstForm, cache.DefaultVernWs));
            foreach (var form in new IMoForm[] { entry.LexemeFormOA, alternate })
            {
                var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
                word.AnalysesOC.Add(analysis);
                var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = form;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
                bundle.SenseRA = entry.SensesOS[0];
                cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            }
        });
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
        var root = Path.Combine(Path.GetTempPath(), "motif-morphology-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(root));
            using var invoker = new PanGlossInvoker();
            var assessor = new PanGlossAssessor(paths, invoker);
            var produced = await assessor.ProduceAsync(new([SeededProject.FirstForm, SeededProject.FirstForm, "mofita"],
                [AssessmentKind.ParseTime, AssessmentKind.Correctness], TimeSpan.FromSeconds(5)),
                Path.GetDirectoryName(cache.ProjectId.Path)!, CancellationToken.None);
            var batch = Assert.IsType<AssessmentRaw.Batch>(produced.Single(item => item.Kind == AssessmentKind.Correctness).Raw).Analysis;
            var first = batch.Words[0];
            Assert.Equal("covered", first.Correctness!.Status);
            Assert.Equal(2, first.Correctness.Expected);
            Assert.Equal(2, first.Correctness.Matched);
            Assert.Empty(first.Morphology!.Unavailable);
            var forms = first.Morphology.Analyses.SelectMany(analysis => analysis.Morphs).Select(morph => morph.Form).ToHashSet();
            Assert.Contains(entry.LexemeFormOA.Guid.ToString("D"), forms);
            Assert.Contains(alternate.Guid.ToString("D"), forms);
            Assert.All(first.Morphology.Analyses.SelectMany(analysis => analysis.Morphs), morph =>
                Assert.Equal(entry.MorphoSyntaxAnalysesOC.First().Guid.ToString("D"), morph.Msa));
            Assert.Equal(1, batch.Words[1].Morphology!.Index);
            Assert.Equal("covered", batch.Words[1].Correctness!.Status);
            Assert.Equal(WordOutcome.NoAnalysis, batch.Words[2].Outcome);
            Assert.Equal("no-expectations", batch.Words[2].Correctness!.Status);
            Assert.Equal(produced[0].Invocation, produced[1].Invocation);
            Assert.Equal(BatchInvocationEvidence.DigestFile(produced[0].Invocation!.AnalysesPath!),
                produced[0].Invocation!.AnalysesSha256);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [RealParserFact]
    public async Task InflectionalVariantRetainsVariantFormBaseMsaAndInflTypeGuids()
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        ILexEntry variant = null!;
        ILexEntryInflType inflType = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var lexDb = cache.LangProject.LexDbOA;
            if (lexDb.VariantEntryTypesOA is null)
                lexDb.VariantEntryTypesOA = services.GetInstance<ICmPossibilityListFactory>().Create();
            inflType = services.GetInstance<ILexEntryInflTypeFactory>().Create();
            lexDb.VariantEntryTypesOA.PossibilitiesOS.Add(inflType);
            inflType.Name.set_String(cache.DefaultAnalWs, "Inflectional variant");
            inflType.Abbreviation.set_String(cache.DefaultAnalWs, "infl.var.");
            var variantRef = entry.CreateVariantEntryAndBackRef(
                inflType, TsStringUtils.MakeString("motifv", cache.DefaultVernWs));
            variant = variantRef.OwnerOfClass<ILexEntry>();

            var word = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("motifv", cache.DefaultVernWs));
            var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            word.AnalysesOC.Add(analysis);
            var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = variant.LexemeFormOA;
            bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
            bundle.InflTypeRA = inflType;
            bundle.SenseRA = entry.SensesOS[0];
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
        });
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b", "v");
        var root = Path.Combine(Path.GetTempPath(), "motif-variant-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(root));
            using var invoker = new PanGlossInvoker();
            var assessor = new PanGlossAssessor(paths, invoker);
            var produced = await assessor.ProduceAsync(
                new(["motifv"], [AssessmentKind.Correctness], TimeSpan.FromSeconds(5)),
                Path.GetDirectoryName(cache.ProjectId.Path)!, CancellationToken.None);
            var batch = Assert.IsType<AssessmentRaw.Batch>(produced.Single().Raw).Analysis;
            var word = Assert.Single(batch.Words);
            Assert.Equal("covered", word.Correctness!.Status);
            Assert.Equal(1, word.Correctness.Expected);
            Assert.Equal(1, word.Correctness.Matched);
            Assert.Empty(word.Morphology!.Unavailable);
            var morph = Assert.Single(Assert.Single(word.Morphology.Analyses).Morphs);
            Assert.Equal(variant.LexemeFormOA.Guid.ToString("D"), morph.Form);
            Assert.Equal(entry.MorphoSyntaxAnalysesOC.First().Guid.ToString("D"), morph.Msa);
            Assert.Equal(inflType.Guid.ToString("D"), morph.InflType);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [RealParserFact]
    public async Task ExhaustedStepBudgetReturnsPerWordCapsAndContinuesTheBatch()
    {
        const string grammar = """
            <HermitCrabInput><Language><Name>BoundedSuffix</Name>
            <PartsOfSpeech><PartOfSpeech id="pos"><Name>Noun</Name></PartOfSpeech></PartsOfSpeech>
            <CharacterDefinitionTable id="table"><Name>Main</Name><SegmentDefinitions>
            <SegmentDefinition id="k"><Representations><Representation>k</Representation></Representations></SegmentDefinition>
            <SegmentDefinition id="a"><Representations><Representation>a</Representation></Representations></SegmentDefinition>
            <SegmentDefinition id="d"><Representations><Representation>d</Representation></Representations></SegmentDefinition>
            </SegmentDefinitions></CharacterDefinitionTable>
            <NaturalClasses><FeatureNaturalClass id="any"><Name>Any</Name></FeatureNaturalClass></NaturalClasses>
            <Strata><Stratum characterDefinitionTable="table" morphologicalRuleOrder="unordered" morphologicalRules="suffix">
            <Name>Main</Name><MorphologicalRuleDefinitions>
            <MorphologicalRule id="suffix" requiredPartsOfSpeech="pos"><Name>Suffix</Name>
            <MorphologicalSubrules><MorphologicalSubrule id="suffix-form"><MorphologicalInput>
            <PhoneticSequence id="stem"><OptionalSegmentSequence min="1" max="-1"><SimpleContext naturalClass="any" />
            </OptionalSegmentSequence></PhoneticSequence></MorphologicalInput><MorphologicalOutput>
            <CopyFromInput index="stem" /><InsertSegments><PhoneticShape>d</PhoneticShape></InsertSegments>
            </MorphologicalOutput></MorphologicalSubrule></MorphologicalSubrules></MorphologicalRule>
            </MorphologicalRuleDefinitions><LexicalEntries><LexicalEntry id="root" partOfSpeech="pos">
            <Allomorphs><Allomorph id="root-form"><PhoneticShape>ka</PhoneticShape></Allomorph></Allomorphs>
            </LexicalEntry></LexicalEntries></Stratum></Strata></Language></HermitCrabInput>
            """;
        var grammarPath = Path.Combine(Path.GetTempPath(), "motif-cap-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(grammarPath, grammar);
        string[] words = ["kadddd", "ak"];
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var outcome = await invoker.RunAsync(
                new PanGlossRequest.Batch(grammarPath, words, TimeSpan.FromSeconds(1), PerWordStepLimit: 1)
                    { CollectAnalyses = true },
                "test:per-word-cap", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));
            Assert.True(outcome is PanGlossOutcome.Completed, outcome.Message);
            var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
            var results = BatchTsvParser.Parse(completed.Output);
            Assert.Equal(words, results.Select(result => result.Word));
            Assert.Equal(WordOutcome.Capped, results[0].Outcome);
            Assert.Equal(WordOutcome.NoAnalysis, results[1].Outcome);
            var evidence = ParseMorphEvidence.Read(completed.MorphologyOutput!, words);
            Assert.True(evidence[0].Capped);
            Assert.False(evidence[1].Capped);
            Assert.Contains("CAP", completed.StandardError);
        }
        finally { File.Delete(grammarPath); }
    }

    [RealParserFact]
    public async Task RuntimeSeededStemsParseAndASegmentableUnknownWordHasNoAnalysis()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");

        string[] words = [SeededProject.FirstForm, SeededProject.SecondForm, "mofita"];
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(cache.ProjectId.Path, words, TimeSpan.FromSeconds(1)),
            "test:runtime-seeded-stems", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));

        Assert.True(outcome is PanGlossOutcome.Completed, $"Expected a completed batch, received {outcome}.");
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var results = BatchTsvParser.Parse(completed.Output);

        Assert.Equal(words, results.Select(result => result.Word));
        Assert.Collection(results,
            first => Assert.Equal(WordOutcome.Analysed, first.Outcome),
            second => Assert.Equal(WordOutcome.Analysed, second.Outcome),
            unknown => Assert.Equal(WordOutcome.NoAnalysis, unknown.Outcome));
    }
}
