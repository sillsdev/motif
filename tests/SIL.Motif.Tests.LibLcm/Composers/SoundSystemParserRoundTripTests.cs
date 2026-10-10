using System.Diagnostics;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class SoundSystemParserRoundTripTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task RemovingLeakedClassMemberStopsWrongFormAndKeepsHeldOutMemberParsing()
    {
        using var cache = pristine.NewScratch();
        var operations = new List<OperationEnvelope>();
        var phonemes = new Dictionary<string, CanonicalId>();
        var values = AddParserFeatures(cache);
        foreach (var (letter, featureValues) in new[]
                 {
                     ("t", new[] { values.FirstPositive, values.SecondPositive }),
                     ("b", new[] { values.FirstNegative, values.SecondPositive }),
                     ("n", new[] { values.FirstPositive, values.SecondNegative }),
                     ("m", new[] { values.FirstNegative, values.SecondNegative }),
                     ("a", new[] { values.FirstPositive, values.SecondPositive }),
                 })
            phonemes.Add(letter, SoundSystemComposerTests.Compose(cache, operations,
                () => AuthorPhonemeComposer.Build(cache, new(letter, [letter], featureValues))));
        var contexts = SoundSystemComposerTests.Compose(cache, operations,
            () => AuthorNaturalClassComposer.Build(cache, new("productive contexts", "CTX",
                [phonemes["t"], phonemes["b"]])));
        PrepareParser(cache);
        var rule = AuthorPhonologicalRuleComposer.Build(cache,
            new AuthorPhonologicalRuleIntent("n becomes m after a productive context",
                PhonologicalRuleDirection.Simultaneous, [new(Phoneme: phonemes["n"])],
                [new(Phoneme: phonemes["m"])], [new(NaturalClass: contexts)],
                [new(Phoneme: phonemes["a"])]));
        SoundSystemComposerTests.Execute(cache, rule);
        operations.AddRange(rule);

        var leakedWord = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.FirstEntryId);
        var heldOutWord = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.SecondEntryId);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var stemType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem);
        SoundSystemComposerTests.Execute(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(leakedWord.Guid), stemType, ws, "tna")));
        SoundSystemComposerTests.Execute(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(heldOutWord.Guid), stemType, ws, "bna")));

        var loader = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader();
        loader.Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var wrongBefore = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
            cache.ProjectId.Path, "tma", CancellationToken.None, TimeSpan.FromSeconds(30)));
        var heldOutBefore = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
            cache.ProjectId.Path, "bma", CancellationToken.None, TimeSpan.FromSeconds(30)));
        Assert.True(wrongBefore.Document!.Analyses.SelectMany(analysis => analysis.Morphs)
            .Any(morph => morph.FormId == leakedWord.LexemeFormOA!.Guid.ToString("D")),
            wrongBefore.Document.RawJson);
        Assert.Contains(heldOutBefore.Document!.Analyses.SelectMany(analysis => analysis.Morphs),
            morph => morph.FormId == heldOutWord.LexemeFormOA!.Guid.ToString("D"));

        var edit = EditNaturalClassComposer.Build(cache,
            new EditNaturalClassIntent(contexts, [phonemes["t"], phonemes["b"]], [phonemes["b"]]));
        Assert.Contains(edit, operation => operation.Kind == PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments);
        SoundSystemComposerTests.Execute(cache, edit);
        loader.Save(cache);

        var wrongAfter = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
            cache.ProjectId.Path, "tma", CancellationToken.None, TimeSpan.FromSeconds(30)));
        var heldOutAfter = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
            cache.ProjectId.Path, "bma", CancellationToken.None, TimeSpan.FromSeconds(30)));
        Assert.DoesNotContain(wrongAfter.Document!.Analyses.SelectMany(analysis => analysis.Morphs),
            morph => morph.FormId == leakedWord.LexemeFormOA!.Guid.ToString("D"));
        Assert.Contains(heldOutAfter.Document!.Analyses.SelectMany(analysis => analysis.Morphs),
            morph => morph.FormId == heldOutWord.LexemeFormOA!.Guid.ToString("D"));
    }

    private static ParserFeatureValues AddParserFeatures(LcmCache cache)
    {
        IFsClosedFeature first = null!;
        IFsClosedFeature second = null!;
        IFsSymFeatVal firstPositive = null!;
        IFsSymFeatVal firstNegative = null!;
        IFsSymFeatVal secondPositive = null!;
        IFsSymFeatVal secondNegative = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            first = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            second = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(first);
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(second);
            first.Name.set_String(cache.DefaultAnalWs, "First segment feature");
            second.Name.set_String(cache.DefaultAnalWs, "Second segment feature");
            firstPositive = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            firstNegative = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            secondPositive = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            secondNegative = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            first.ValuesOC.Add(firstPositive);
            first.ValuesOC.Add(firstNegative);
            second.ValuesOC.Add(secondPositive);
            second.ValuesOC.Add(secondNegative);
            firstPositive.Name.set_String(cache.DefaultAnalWs, "positive");
            firstNegative.Name.set_String(cache.DefaultAnalWs, "negative");
            secondPositive.Name.set_String(cache.DefaultAnalWs, "positive");
            secondNegative.Name.set_String(cache.DefaultAnalWs, "negative");
        });
        return new(
            new(CanonicalId.FromGuid(first.Guid), CanonicalId.FromGuid(firstPositive.Guid)),
            new(CanonicalId.FromGuid(first.Guid), CanonicalId.FromGuid(firstNegative.Guid)),
            new(CanonicalId.FromGuid(second.Guid), CanonicalId.FromGuid(secondPositive.Guid)),
            new(CanonicalId.FromGuid(second.Guid), CanonicalId.FromGuid(secondNegative.Guid)));
    }

    private static void PrepareParser(LcmCache cache)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>";
            var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single();
            var boundary = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create();
            set.BoundaryMarkersOC.Add(boundary);
            boundary.Name.set_String(cache.DefaultVernWs, "+");
            var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            boundary.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, "+");
        });
    }

    private sealed record ParserFeatureValues(PhonologicalFeatureValue FirstPositive,
        PhonologicalFeatureValue FirstNegative, PhonologicalFeatureValue SecondPositive,
        PhonologicalFeatureValue SecondNegative);

    [RealParserFact]
    public async Task SyntheticRewriteShowsItsSurfaceInParseAndGenerate()
    {
        await AssertRewriteWitnessAsync([("n", "m", null, null)], [0], "n", "m");
    }

    [RealParserFact]
    public async Task RuleOrderFeedsAndBleedsAcrossParseAndGenerate()
    {
        var feeding = new[] { ("n", "m", (string?)null, (string?)null), ("m", "p", null, null) };
        await AssertRewriteWitnessAsync(feeding, [0, 1], "n", "p");
        await AssertRewriteWitnessAsync(feeding, [1, 0], "n", "m");

        var bleeding = new[] { ("n", "m", (string?)null, (string?)null), ("n", "p", null, null) };
        await AssertRewriteWitnessAsync(bleeding, [0, 1], "n", "m");
        await AssertRewriteWitnessAsync(bleeding, [1, 0], "n", "p");
    }

    [RealParserFact]
    public async Task ComposerAuthoredSoundSystemConditionsASuffixOnAVowelAndRefusesAConsonant()
    {
        using var cache = pristine.NewScratch();
        var all = new List<OperationEnvelope>();
        var phonemes = new Dictionary<string, CanonicalId>();
        foreach (var letter in new[] { "k", "a", "s" })
            phonemes.Add(letter, SoundSystemComposerTests.Compose(cache, all,
                () => AuthorPhonemeComposer.Build(cache, new(letter, [letter]))));
        var vowels = SoundSystemComposerTests.Compose(cache, all,
            () => AuthorNaturalClassComposer.Build(cache, new("vowels", "V", [phonemes["a"]])));
        var environment = SoundSystemComposerTests.Compose(cache, all,
            () => AuthorEnvironmentComposer.Build(cache, new("after a vowel at word end",
                [new(NaturalClass: vowels)], [new(Boundary: "word")])));
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var stemType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem);
        foreach (var (entry, text) in new[] { (pristine.Seed.FirstEntryId, "ka"), (pristine.Seed.SecondEntryId, "ks") })
            SoundSystemComposerTests.Execute(cache, AuthorLexemeFormComposer.Build(cache,
                new(CanonicalId.FromGuid(entry), stemType, ws, text)));
        ILexEntry suffix = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>";
            suffix = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(
                cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphSuffix),
                TsStringUtils.MakeString("s", cache.DefaultVernWs), "plural",
                new SandboxGenericMSA { MsaType = MsaType.kUnclassified });
        });
        SoundSystemComposerTests.Execute(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(suffix.Guid), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphSuffix), ws, "s",
                Environments: [environment])));
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(cache.ProjectId.Path,
            ["ka", "ks", "kas", "kss"], TimeSpan.FromSeconds(5)), "sound-system round trip", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var rows = BatchTsvParser.Parse(completed.Output);
        Assert.DoesNotContain("invalid environment", completed.StandardError.ToLowerInvariant());
        Assert.DoesNotContain("failed", completed.StandardError.ToLowerInvariant());
        Assert.Equal(WordOutcome.Analysed, rows.Single(w => w.Word == "ka").Outcome);
        Assert.Equal(WordOutcome.Analysed, rows.Single(w => w.Word == "ks").Outcome);
        Assert.Equal(WordOutcome.Analysed, rows.Single(w => w.Word == "kas").Outcome);
        Assert.Equal(WordOutcome.NoAnalysis, rows.Single(w => w.Word == "kss").Outcome);
    }

    [RealParserFact]
    public async Task EditingAStemConditionChangesWhichAllomorphParsesAWord()
    {
        using var cache = pristine.NewScratch();
        var operations = new List<OperationEnvelope>();
        var vowel = SoundSystemComposerTests.Compose(cache, operations,
            () => AuthorPhonemeComposer.Build(cache, new("a", ["a"])));
        var rejectedContext = SoundSystemComposerTests.Compose(cache, operations,
            () => AuthorEnvironmentComposer.Build(cache,
                new("after a", [new(Phoneme: vowel)], [])));
        var wordEdgeContext = SoundSystemComposerTests.Compose(cache, operations,
            () => AuthorEnvironmentComposer.Build(cache,
                new("word initial", [new(Boundary: "word")], [])));
        ILexEntry root = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            root = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(
                cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("ka", cache.DefaultVernWs), "condition witness",
                new SandboxGenericMSA { MsaType = MsaType.kStem }));
        var stem = Assert.IsAssignableFrom<IMoStemAllomorph>(root.LexemeFormOA);
        var rejected = EditAllomorphConditionComposer.Build(cache,
            new EditAllomorphConditionIntent(CanonicalId.FromGuid(stem.Guid), AllomorphConditionField.PhoneEnv,
                [], [rejectedContext]));
        SoundSystemComposerTests.Execute(cache, rejected);
        RealParserProject.PrepareForParsing(cache, "k");
        var formId = stem.Guid.ToString("D");

        var loader = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader();
        loader.Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var beforeOutcome = await tracer.TraceAsync(cache.ProjectId.Path, "ka", CancellationToken.None,
            TimeSpan.FromSeconds(30));
        var before = Assert.IsType<PanGlossTraceOutcome.Completed>(beforeOutcome);
        Assert.DoesNotContain(before.Document!.Analyses.SelectMany(analysis => analysis.Morphs),
            morph => morph.FormId == formId);

        var edit = EditAllomorphConditionComposer.Build(cache,
            new EditAllomorphConditionIntent(CanonicalId.FromGuid(stem.Guid), AllomorphConditionField.PhoneEnv,
                [rejectedContext], [wordEdgeContext]));
        Assert.Contains(edit, operation => operation.Kind == MoStemAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv);
        Assert.Contains(edit, operation => operation.Kind == MoStemAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv);
        SoundSystemComposerTests.Execute(cache, edit);
        loader.Save(cache);

        var afterOutcome = await tracer.TraceAsync(cache.ProjectId.Path, "ka", CancellationToken.None,
            TimeSpan.FromSeconds(30));
        var after = Assert.IsType<PanGlossTraceOutcome.Completed>(afterOutcome);
        var morph = Assert.Single(after.Document!.Analyses.SelectMany(analysis => analysis.Morphs));
        Assert.Equal(formId, morph.FormId);
    }

    [RealParserFact]
    public async Task FinalUnrestrictedAllomorphFallsBackAfterAConditionedAlternate()
    {
        using var cache = pristine.NewScratch();
        var operations = new List<OperationEnvelope>();
        var wordInitial = SoundSystemComposerTests.Compose(cache, operations,
            () => AuthorEnvironmentComposer.Build(cache,
                new("word initial", [new(Boundary: "word")], [])));
        ILexEntry root = null!;
        ILexEntry prefix = null!;
        IMoStemAllomorph conditioned = null!;
        IMoStemAllomorph fallback = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var morphTypes = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>();
            var entries = cache.ServiceLocator.GetInstance<ILexEntryFactory>();
            root = entries.Create(morphTypes.GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("ka", cache.DefaultVernWs), "root",
                new SandboxGenericMSA { MsaType = MsaType.kStem });
            conditioned = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            fallback = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            root.AlternateFormsOS.Add(conditioned);
            root.AlternateFormsOS.Add(fallback);
            foreach (var form in new[] { conditioned, fallback })
            {
                form.MorphTypeRA = root.LexemeFormOA!.MorphTypeRA;
                form.Form.set_String(cache.DefaultVernWs, "ka");
            }
            prefix = entries.Create(morphTypes.GetObject(MoMorphTypeTags.kguidMorphPrefix),
                TsStringUtils.MakeString("a", cache.DefaultVernWs), "prefix",
                new SandboxGenericMSA { MsaType = MsaType.kUnclassified });
        });
        var condition = EditAllomorphConditionComposer.Build(cache,
            new EditAllomorphConditionIntent(CanonicalId.FromGuid(conditioned.Guid),
                AllomorphConditionField.PhoneEnv, [], [wordInitial]));
        SoundSystemComposerTests.Execute(cache, condition);
        RealParserProject.PrepareForParsing(cache, "a", "k");
        var loader = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader();
        loader.Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        var conditionedWord = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
            cache.ProjectId.Path, "ka", CancellationToken.None, TimeSpan.FromSeconds(30)));
        Assert.Contains(conditionedWord.Document!.Analyses.SelectMany(analysis => analysis.Morphs),
            morph => morph.FormId == conditioned.Guid.ToString("D"));
        var fallbackWord = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
            cache.ProjectId.Path, "aka", CancellationToken.None, TimeSpan.FromSeconds(30)));
        Assert.Contains(fallbackWord.Document!.Analyses.SelectMany(analysis => analysis.Morphs),
            morph => morph.FormId == prefix.LexemeFormOA!.Guid.ToString("D"));
        Assert.Contains(fallbackWord.Document.Analyses.SelectMany(analysis => analysis.Morphs),
            morph => morph.FormId == fallback.Guid.ToString("D"));

        var order = OrderAllomorphsComposer.Build(cache,
            new OrderAllomorphsIntent(CanonicalId.FromGuid(root.Guid),
                [CanonicalId.FromGuid(conditioned.Guid), CanonicalId.FromGuid(fallback.Guid)],
                [CanonicalId.FromGuid(fallback.Guid), CanonicalId.FromGuid(conditioned.Guid)]));
        SoundSystemComposerTests.Execute(cache, order);
        loader.Save(cache);
        var earlierFallback = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
            cache.ProjectId.Path, "ka", CancellationToken.None, TimeSpan.FromSeconds(30)));
        Assert.Contains(earlierFallback.Document!.Analyses.SelectMany(analysis => analysis.Morphs),
            morph => morph.FormId == fallback.Guid.ToString("D"));
    }

    private static async Task AssertRewriteWitnessAsync(
        (string Input, string Output, string? Left, string? Right)[] rewrites, int[] order,
        string root, string expected)
    {
        var grammarPath = Path.Combine(Path.GetTempPath(), "motif-rule-order-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(grammarPath, SyntheticRewriteGrammar(rewrites, order, root));
        try
        {
            var generated = Generate(grammarPath, "ROOT");
            Assert.True(generated.ExitCode == 0, generated.Error + Environment.NewLine + generated.Output);
            Assert.Contains(expected, generated.Output, StringComparison.Ordinal);

            using var invoker = new PanGlossInvoker();
            var batch = await invoker.RunAsync(new PanGlossRequest.Batch(grammarPath, [expected],
                TimeSpan.FromSeconds(5)), "synthetic rewrite parse", CancellationToken.None);
            var completed = Assert.IsType<PanGlossOutcome.Completed>(batch);
            Assert.Equal(WordOutcome.Analysed, Assert.Single(BatchTsvParser.Parse(completed.Output)).Outcome);
        }
        finally
        {
            if (File.Exists(grammarPath)) File.Delete(grammarPath);
        }
    }

    private static ToolProcessResult Generate(string grammarPath, string rootMorphemeId)
    {
        var executable = PanGlossExecutable.TryLocate()!;
        var generate = new ProcessStartInfo(executable);
        generate.ArgumentList.Add("generate");
        generate.ArgumentList.Add(grammarPath);
        generate.ArgumentList.Add(rootMorphemeId);
        return ToolProcess.Run(generate);
    }

    private static string SyntheticRewriteGrammar(
        (string Input, string Output, string? Left, string? Right)[] rewrites, int[] order, string root)
    {
        var rules = string.Concat(order.Select(index => (rewrite: rewrites[index], index)).Select(item =>
        {
            var left = item.rewrite.Left is { } leftContext
                ? $"<LeftEnvironment><PhoneticTemplate><PhoneticSequence><SimpleContext naturalClass=\"nc{leftContext}\" /></PhoneticSequence></PhoneticTemplate></LeftEnvironment>"
                : string.Empty;
            var right = item.rewrite.Right is { } rightContext
                ? $"<RightEnvironment><PhoneticTemplate><PhoneticSequence><SimpleContext naturalClass=\"nc{rightContext}\" /></PhoneticSequence></PhoneticTemplate></RightEnvironment>"
                : string.Empty;
            var environment = left.Length + right.Length == 0 ? string.Empty : $"<Environment>{left}{right}</Environment>";
            return $$"""
                    <PhonologicalRule id="pr{{item.index + 1}}">
                      <Name>{{item.rewrite.Input}} becomes {{item.rewrite.Output}}</Name>
                      <PhoneticInput><PhoneticSequence><SimpleContext naturalClass="nc{{item.rewrite.Input}}" /></PhoneticSequence></PhoneticInput>
                      <PhonologicalSubrules><PhonologicalSubrule>
                        <PhoneticOutput><PhoneticSequence><SimpleContext naturalClass="nc{{item.rewrite.Output}}" /></PhoneticSequence></PhoneticOutput>
                        {{environment}}
                      </PhonologicalSubrule></PhonologicalSubrules>
                    </PhonologicalRule>
            """;
        }));
        var ruleOrder = string.Join(' ', order.Select(index => $"pr{index + 1}"));
        return $$"""
            <HermitCrabInput><Language><Name>RuleOrderWitness</Name>
              <PartsOfSpeech><PartOfSpeech id="posV"><Name>Verb</Name></PartOfSpeech></PartsOfSpeech>
              <PhonologicalFeatureSystem><SymbolicFeature id="featF"><Name>f</Name>
                <Symbols><Symbol id="symP">plus</Symbol><Symbol id="symM">minus</Symbol></Symbols>
              </SymbolicFeature><SymbolicFeature id="featG"><Name>g</Name>
                <Symbols><Symbol id="symG1">plus</Symbol><Symbol id="symG0">minus</Symbol></Symbols>
              </SymbolicFeature></PhonologicalFeatureSystem>
              <CharacterDefinitionTable id="t1"><Name>Main</Name><SegmentDefinitions>
                <SegmentDefinition id="cn"><Representations><Representation>n</Representation></Representations><FeatureValue feature="featF" symbolValues="symP" /><FeatureValue feature="featG" symbolValues="symG0" /></SegmentDefinition>
                <SegmentDefinition id="cm"><Representations><Representation>m</Representation></Representations><FeatureValue feature="featF" symbolValues="symM" /><FeatureValue feature="featG" symbolValues="symG0" /></SegmentDefinition>
                <SegmentDefinition id="cp"><Representations><Representation>p</Representation></Representations><FeatureValue feature="featF" symbolValues="symP" /><FeatureValue feature="featG" symbolValues="symG1" /></SegmentDefinition>
            </SegmentDefinitions><BoundaryDefinitions>
              <BoundaryDefinition id="cHash"><Representations><Representation>#</Representation></Representations></BoundaryDefinition>
              </BoundaryDefinitions></CharacterDefinitionTable>
              <NaturalClasses>
                <SegmentNaturalClass id="ncn"><Name>N</Name><Segment segment="cn" /></SegmentNaturalClass>
                <SegmentNaturalClass id="ncm"><Name>M</Name><Segment segment="cm" /></SegmentNaturalClass>
                <SegmentNaturalClass id="ncp"><Name>P</Name><Segment segment="cp" /></SegmentNaturalClass>
              </NaturalClasses>
              <PhonologicalRuleDefinitions>{{rules}}</PhonologicalRuleDefinitions>
              <Strata><Stratum characterDefinitionTable="t1" morphologicalRuleOrder="unordered" phonologicalRules="{{ruleOrder}}">
                <Name>Main</Name><LexicalEntries><LexicalEntry id="entryN" partOfSpeech="posV">
                  <Allomorphs><Allomorph id="alloN"><PhoneticShape>{{root}}</PhoneticShape></Allomorph></Allomorphs>
                  <MorphemeId>ROOT</MorphemeId><Gloss>root</Gloss>
                </LexicalEntry></LexicalEntries>
              </Stratum></Strata>
            </Language></HermitCrabInput>
            """;
    }
}
