using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PrefixRootRewriteTraceTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public void SegmentContextRuleAppliesFromPrefixIntoRootAcrossWordBoundary()
    {
        using var cache = pristine.NewScratch();
        var candidate = BuildPrefixRootCandidate(cache, pristine.Seed);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        Apply(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(candidate.Entry.Guid), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix), ws, "ar")));
        var operations = AuthorPhonologicalRuleComposer.Build(cache, new(
            "r becomes t between a segments",
            PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: candidate.R)],
            [new(Phoneme: candidate.T)],
            [new(Phoneme: candidate.A)],
            [new(NaturalClass: candidate.Vowels)]));
        Apply(cache, operations);
        new FwDataProjectLoader().Save(cache);

        var executable = PanGlossExecutable.TryLocate()!;
        var retainedDirectory = Path.Combine(Path.GetTempPath(), "allomorph-rule-prefix-root-context-project-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(Path.GetDirectoryName(cache.ProjectId.Path)!, retainedDirectory);
        var retainedProject = Path.Combine(retainedDirectory, Path.GetFileName(cache.ProjectId.Path));
        var tracePath = Path.Combine(retainedDirectory, "ata.trace.json");
        var parse = Run(executable, "parse", retainedProject, "ata", "--trace=" + tracePath,
            "--trace-format", "json", "--trace-details");
        var trace = File.Exists(tracePath) ? File.ReadAllText(tracePath) : "trace file missing";
        var diagnostic = $"saved project={retainedProject}\nparse ata exit={parse.ExitCode}\n" +
            $"{parse.Output}\n{parse.Error}\n{trace}";

        Assert.Equal(0, parse.ExitCode);
        using var document = JsonDocument.Parse(trace);
        var readings = document.RootElement.GetProperty("result").GetProperty("analyses")
            .EnumerateArray().SelectMany(analysis => analysis.GetProperty("morphs").EnumerateArray()).ToArray();
        Assert.Contains(readings, morph => morph.GetProperty("identity").GetProperty("formId").GetString() ==
            candidate.Entry.LexemeFormOA!.Guid.ToString("D"));
        Assert.Contains(readings, morph => morph.GetProperty("identity").GetProperty("entryId").GetString() ==
            pristine.Seed.FirstEntryId.ToString("D"));
        Assert.Contains("\"outputShape\":\"ata\"", trace, StringComparison.Ordinal);
        output.WriteLine($"Scratch={retainedProject}");
        output.WriteLine($"Parser={executable}; version=0.8.2");
        output.WriteLine("Segment context a_r_Vowel applies across prefix+root as ara→ata.");
        output.WriteLine(diagnostic);
    }

    [RealParserFact]
    public void SegmentContextRuleAppliesAcrossRootAndSuffixBoundary()
    {
        using var cache = pristine.NewScratch();
        var candidate = BuildSuffixContextCandidate(cache, pristine.Seed);
        var operations = AuthorPhonologicalRuleComposer.Build(cache, new(
            "r becomes t between a segments",
            PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: candidate.R)],
            [new(Phoneme: candidate.T)],
            [new(Phoneme: candidate.A)],
            [new(NaturalClass: candidate.Vowels)]));
        Apply(cache, operations);
        new FwDataProjectLoader().Save(cache);

        var executable = PanGlossExecutable.TryLocate()!;
        var retainedDirectory = Path.Combine(Path.GetTempPath(), "allomorph-rule-suffix-root-project-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(Path.GetDirectoryName(cache.ProjectId.Path)!, retainedDirectory);
        var retainedProject = Path.Combine(retainedDirectory, Path.GetFileName(cache.ProjectId.Path));
        var tracePath = Path.Combine(retainedDirectory, "ata.trace.json");
        var parse = Run(executable, "parse", retainedProject, "ata", "--trace=" + tracePath,
            "--trace-format", "json", "--trace-details");
        var trace = File.Exists(tracePath) ? File.ReadAllText(tracePath) : "trace file missing";
        var diagnostic = $"saved project={retainedProject}\nparse ata exit={parse.ExitCode}\n" +
            $"{parse.Output}\n{parse.Error}\n{trace}";

        Assert.Equal(0, parse.ExitCode);
        using var document = JsonDocument.Parse(trace);
        var analyses = document.RootElement.GetProperty("result").GetProperty("analyses");
        Assert.NotEmpty(analyses.EnumerateArray());
        Assert.Contains("\"outputShape\":\"ata\"", trace, StringComparison.Ordinal);
        output.WriteLine($"Scratch={retainedProject}");
        output.WriteLine($"Parser={executable}; version=0.8.2");
        output.WriteLine("Segment context a_r_Vowel applies across root+suffix as ara→ata.");
        output.WriteLine(diagnostic);
    }

    [RealParserFact]
    public void WordInitialBoundaryRuleSynthesizesThroughPrefixRootInPinnedParser()
    {
        using var cache = pristine.NewScratch();
        var prefix = BuildPrefixRootCandidate(cache, pristine.Seed);
        var ruleOperations = AuthorPhonologicalRuleComposer.Build(cache, new(
            "r becomes t word-initially before a vowel",
            PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: prefix.R)],
            [new(Phoneme: prefix.T)],
            [new(Boundary: "word")],
            [new(NaturalClass: prefix.Vowels)]));
        var ruleId = ruleOperations.Single(operation =>
            operation.Kind == PhPhonDataPhonRulesOperationKinds.Create).EntityId!.Value;
        Apply(cache, ruleOperations);

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => prefix.Alternate.Delete());
        new FwDataProjectLoader().Save(cache);
        var executable = PanGlossExecutable.TryLocate()!;
        var retainedDirectory = Path.Combine(Path.GetTempPath(), "allomorph-rule-prefix-root-project-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(Path.GetDirectoryName(cache.ProjectId.Path)!, retainedDirectory);
        var retainedProject = Path.Combine(retainedDirectory, Path.GetFileName(cache.ProjectId.Path));
        var importPath = Path.Combine(retainedDirectory, "import.json");
        var import = Run(executable, "import", retainedProject, importPath);
        var snapshot = JsonNode.Parse(File.ReadAllText(importPath))!;
        var phonology = snapshot["phonology"]!;
        var ruleJson = phonology["rules"]!.AsArray().Single(item =>
            item!["guid"]!.GetValue<string>() == ruleId.ToGuid().ToString("D"))!;
        var rightHandSideJson = ruleJson["rightHandSides"]![0]!;
        var wordContext = rightHandSideJson["leftContext"]!.AsObject();
        Assert.Equal("wordBoundary", wordContext["kind"]!.GetValue<string>());
        Assert.False(wordContext.ContainsKey("marker"));
        Assert.DoesNotContain(phonology["boundaryMarkers"]!.AsArray(), item =>
            item!["guid"]!.GetValue<string>() == LangProjectTags.kguidPhRuleWordBdry.ToString("D"));
        Assert.DoesNotContain("does not resolve", import.Output + import.Error, StringComparison.Ordinal);
        Assert.Equal("naturalClass", rightHandSideJson["rightContext"]!["kind"]!.GetValue<string>());
        var parserSnapshotPath = importPath;
        var tracePath = Path.Combine(retainedDirectory, "ta.trace.json");
        var parse = Run(executable, "parse", parserSnapshotPath, "ta", "--trace=" + tracePath,
            "--trace-format", "json", "--trace-details");
        var trace = File.Exists(tracePath) ? File.ReadAllText(tracePath) : "trace file missing";
        Assert.Equal(prefix.R.ToGuid().ToString("D"),
            ruleJson["structuralDescription"]![0]!["phoneme"]!.GetValue<string>());
        Assert.Equal(prefix.T.ToGuid().ToString("D"),
            rightHandSideJson["structuralChange"]![0]!["phoneme"]!.GetValue<string>());
        Assert.Equal(prefix.Vowels.ToGuid().ToString("D"),
            rightHandSideJson["rightContext"]!["natural_class"]!.GetValue<string>());
        var naturalClassJson = phonology["naturalClasses"]!.AsArray().Single(item =>
            item!["guid"]!.GetValue<string>() == prefix.Vowels.ToGuid().ToString("D"))!;
        Assert.Equal("segments", naturalClassJson["kind"]!.GetValue<string>());
        Assert.Equal(prefix.A.ToGuid().ToString("D"),
            Assert.Single(naturalClassJson["phonemes"]!.AsArray())!.GetValue<string>());
        foreach (var phonemeId in new[] { prefix.R, prefix.T, prefix.A })
        {
            var phonemeJson = phonology["phonemes"]!.AsArray().Single(item =>
                item!["guid"]!.GetValue<string>() == phonemeId.ToGuid().ToString("D"))!;
            Assert.Equal(2, phonemeJson["features"]!["values"]!.AsArray().Count);
        }
        var controlSnapshot = Path.Combine(retainedDirectory, "word-initial-rule-removed.json");
        var control = JsonNode.Parse(File.ReadAllText(parserSnapshotPath))!;
        var controlRules = control["phonology"]!["rules"]!.AsArray();
        controlRules.RemoveAt(controlRules.IndexOf(controlRules.Single(item =>
            item!["guid"]!.GetValue<string>() == ruleId.ToGuid().ToString("D"))));
        File.WriteAllText(controlSnapshot, control.ToJsonString());
        var controlTracePath = Path.Combine(retainedDirectory, "ta-without-left-boundary.trace.json");
        var controlParse = Run(executable, "parse", controlSnapshot, "ta", "--trace=" + controlTracePath,
            "--trace-format", "json", "--trace-details");
        var controlTrace = File.ReadAllText(controlTracePath);
        var diagnostic = $"saved project={retainedProject}\nimport exit={import.ExitCode}\n{import.Output}\n{import.Error}\n" +
            $"parse ta exit={parse.ExitCode}\n{parse.Output}\n{parse.Error}\n{trace}\n" +
            $"rule-removed control exit={controlParse.ExitCode}\n{controlParse.Output}\n{controlParse.Error}\n{controlTrace}";

        Assert.True(import.ExitCode == 0, diagnostic);
        Assert.Contains("sourceKind\":\"snapshot\"", trace, StringComparison.Ordinal);
        Assert.True(parse.ExitCode == 0, diagnostic);
        using var document = JsonDocument.Parse(trace);
        var readings = document.RootElement.GetProperty("result").GetProperty("analyses")
            .EnumerateArray().SelectMany(analysis => analysis.GetProperty("morphs").EnumerateArray()).ToArray();
        Assert.Contains(readings, morph => morph.GetProperty("identity").GetProperty("entryId").GetString() ==
            prefix.Entry.Guid.ToString("D"));
        Assert.Contains(readings, morph => morph.GetProperty("identity").GetProperty("formId").GetString() ==
            prefix.Entry.LexemeFormOA!.Guid.ToString("D"));
        Assert.Contains("\"type\":\"PhonologicalRuleSynthesis\"", trace, StringComparison.Ordinal);
        Assert.Contains("\"outputShape\":\"ta\"", trace, StringComparison.Ordinal);
        Assert.Contains(ruleId.ToGuid().ToString("D"), trace, StringComparison.OrdinalIgnoreCase);
        Assert.True(controlParse.ExitCode == 0, diagnostic);
        using var controlDocument = JsonDocument.Parse(controlTrace);
        Assert.Empty(controlDocument.RootElement.GetProperty("result").GetProperty("analyses").EnumerateArray());
        output.WriteLine($"Scratch={retainedProject}");
        output.WriteLine($"Parser={executable}; version=0.8.2");
        output.WriteLine($"Imported rule={ruleId.Value}; FWData context uses word-boundary GUID {prefix.WordBoundary.Value}.");
        output.WriteLine("Imported snapshot context is wordBoundary; PhonologicalRuleSynthesis produces ta through prefix r + root a.");
        output.WriteLine("Control: removing the rule leaves ta without an analysis.");
        Assert.False(Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator.ObjectRepository
            .GetObject(ruleId.ToGuid())).Disabled);
        Assert.Contains(cache.LangProject.PhonologicalDataOA.PhonRulesOS,
            rule => rule.Guid == ruleId.ToGuid());
        Assert.Equal(0, cache.LangProject.PhonologicalDataOA.PhonRulesOS.Select((rule, index) => (rule, index))
            .Single(item => item.rule.Guid == ruleId.ToGuid()).index);
        var rule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator.ObjectRepository.GetObject(ruleId.ToGuid()));
        Assert.Equal(prefix.R.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(Assert.Single(rule.StrucDescOS))
            .FeatureStructureRA!.Guid);
        var rhs = Assert.Single(rule.RightHandSidesOS);
        Assert.Equal(prefix.T.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(Assert.Single(rhs.StrucChangeOS))
            .FeatureStructureRA!.Guid);
        Assert.Equal(LangProjectTags.kguidPhRuleWordBdry, Assert.IsAssignableFrom<IPhSimpleContextBdry>(rhs.LeftContextOA)
            .FeatureStructureRA!.Guid);
        Assert.Equal(prefix.Vowels.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextNC>(rhs.RightContextOA)
            .FeatureStructureRA!.Guid);
    }

    [RealParserFact]
    public void LiteralHashMarkerDoesNotActAsAWordEdgeInPinnedParser()
    {
        using var cache = pristine.NewScratch();
        var prefix = BuildPrefixRootCandidate(cache, pristine.Seed);
        var literalHash = AddBoundaryMarker(cache, Guid.NewGuid(), "#");
        var operations = AuthorPhonologicalRuleComposer.Build(cache, new(
            "r becomes t next to a literal hash marker",
            PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: prefix.R)],
            [new(Phoneme: prefix.T)],
            [new(BoundaryMarker: literalHash)],
            [new(NaturalClass: prefix.Vowels)]));
        var ruleId = operations.Single(operation =>
            operation.Kind == PhPhonDataPhonRulesOperationKinds.Create).EntityId!.Value;
        Apply(cache, operations);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => prefix.Alternate.Delete());
        new FwDataProjectLoader().Save(cache);

        var executable = PanGlossExecutable.TryLocate()!;
        var retainedDirectory = Path.Combine(Path.GetTempPath(), "jm-literal-hash-prefix-project-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(Path.GetDirectoryName(cache.ProjectId.Path)!, retainedDirectory);
        var retainedProject = Path.Combine(retainedDirectory, Path.GetFileName(cache.ProjectId.Path));
        var importPath = Path.Combine(retainedDirectory, "import.json");
        var import = Run(executable, "import", retainedProject, importPath);
        using var imported = JsonDocument.Parse(File.ReadAllText(importPath));
        var rule = imported.RootElement.GetProperty("phonology").GetProperty("rules").EnumerateArray()
            .Single(item => item.GetProperty("guid").GetString() == ruleId.ToGuid().ToString("D"));
        var boundary = rule.GetProperty("rightHandSides")[0].GetProperty("leftContext");
        Assert.Equal("boundary", boundary.GetProperty("kind").GetString());
        Assert.Equal(literalHash.ToGuid().ToString("D"), boundary.GetProperty("marker").GetString());
        var boundaryMarkers = imported.RootElement.GetProperty("phonology").GetProperty("boundaryMarkers")
            .EnumerateArray().Select(item => item.GetProperty("guid").GetString()).ToArray();
        Assert.Contains(literalHash.ToGuid().ToString("D"), boundaryMarkers);
        Assert.DoesNotContain(LangProjectTags.kguidPhRuleWordBdry.ToString("D"), boundaryMarkers);

        var parserSnapshotPath = importPath;
        var tracePath = Path.Combine(retainedDirectory, "ta.trace.json");
        var parse = Run(executable, "parse", parserSnapshotPath, "ta", "--trace=" + tracePath,
            "--trace-format", "json", "--trace-details");
        var trace = File.Exists(tracePath) ? File.ReadAllText(tracePath) : "trace file missing";
        var diagnostic = $"import exit={import.ExitCode}\n{import.Output}\n{import.Error}\n" +
            $"parse ta exit={parse.ExitCode}\n{parse.Output}\n{parse.Error}\n{trace}";

        Assert.True(import.ExitCode == 0, diagnostic);
        Assert.True(parse.ExitCode == 0, diagnostic);
        Assert.NotEqual(prefix.WordBoundary.ToGuid().ToString("D"), boundary.GetProperty("marker").GetString());
        using var result = JsonDocument.Parse(trace);
        Assert.Empty(result.RootElement.GetProperty("result").GetProperty("analyses").EnumerateArray());
        output.WriteLine($"Scratch={retainedProject}");
        output.WriteLine($"Parser={executable}; version=0.8.2");
        output.WriteLine($"Literal marker={literalHash.Value}; word boundary={prefix.WordBoundary.Value}.");
        output.WriteLine("The imported snapshot keeps literal # as a marker apart from the word edge; it does not license ta.");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static PrefixRootParts BuildPrefixRootCandidate(LcmCache cache, SeededProject seed)
    {
        var features = AddFeatures(cache);
        var r = AddPhoneme(cache, "r", [features.FirstPositive, features.SecondPositive]);
        var t = AddPhoneme(cache, "t", [features.FirstNegative, features.SecondPositive]);
        var vowel = AddPhoneme(cache, "a", [features.FirstPositive, features.SecondNegative]);
        _ = AddPhoneme(cache, "k", [features.FirstNegative, features.SecondNegative]);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>");
        var classOperations = AuthorNaturalClassComposer.Build(cache, new("vowels", "V", [vowel]));
        var classId = classOperations.Single(operation =>
            operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;
        Apply(cache, classOperations);
        var wordBoundary = CanonicalId.FromGuid(LangProjectTags.kguidPhRuleWordBdry);

        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var stemType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem);
        foreach (var (entry, form) in new[]
                 {
                     (seed.FirstEntryId, "a"),
                     (seed.SecondEntryId, "k")
                 })
            Apply(cache, AuthorLexemeFormComposer.Build(cache,
                new(CanonicalId.FromGuid(entry), stemType, ws, form)));

        ILexEntry prefix = null!;
        IMoAffixAllomorph alternate = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var prefixType = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphPrefix);
            prefix = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(prefixType,
                TsStringUtils.MakeString("r", cache.DefaultVernWs), "r prefix",
                new SandboxGenericMSA { MsaType = MsaType.kDeriv });
            var derivational = Assert.IsAssignableFrom<IMoDerivAffMsa>(
                Assert.Single(prefix.MorphoSyntaxAnalysesOC));
            derivational.FromPartOfSpeechRA = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>()
                .GetObject(seed.PartOfSpeechId);
            derivational.ToPartOfSpeechRA = derivational.FromPartOfSpeechRA;
            alternate = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            prefix.AlternateFormsOS.Add(alternate);
            alternate.MorphTypeRA = prefixType;
            alternate.Form.set_String(cache.DefaultVernWs, "t");
        });
        return new(prefix, alternate, r, t, vowel, classId, wordBoundary);
    }

    private static SuffixRootParts BuildSuffixContextCandidate(LcmCache cache, SeededProject seed)
    {
        var features = AddFeatures(cache);
        var r = AddPhoneme(cache, "r", [features.FirstPositive, features.SecondPositive]);
        var t = AddPhoneme(cache, "t", [features.FirstNegative, features.SecondPositive]);
        var a = AddPhoneme(cache, "a", [features.FirstPositive, features.SecondNegative]);
        _ = AddPhoneme(cache, "k", [features.FirstNegative, features.SecondNegative]);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>");
        var classOperations = AuthorNaturalClassComposer.Build(cache, new("vowels", "V", [a]));
        var classId = classOperations.Single(operation =>
            operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;
        Apply(cache, classOperations);

        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var stemType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem);
        Apply(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(seed.FirstEntryId), stemType, ws, "a")));
        Apply(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(seed.SecondEntryId), stemType, ws, "k")));

        ILexEntry suffix = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var suffixType = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphSuffix);
            suffix = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(suffixType,
                TsStringUtils.MakeString("ra", cache.DefaultVernWs), "ra suffix",
                new SandboxGenericMSA { MsaType = MsaType.kDeriv });
            var derivational = Assert.IsAssignableFrom<IMoDerivAffMsa>(
                Assert.Single(suffix.MorphoSyntaxAnalysesOC));
            derivational.FromPartOfSpeechRA = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>()
                .GetObject(seed.PartOfSpeechId);
            derivational.ToPartOfSpeechRA = derivational.FromPartOfSpeechRA;
        });
        return new(suffix, r, t, a, classId);
    }

    private static CanonicalId AddPhoneme(LcmCache cache, string representation,
        IReadOnlyList<PhonologicalFeatureValue> features)
    {
        var operations = AuthorPhonemeComposer.Build(cache, new(representation, [representation], features));
        var id = operations.Single(operation =>
            operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value;
        Apply(cache, operations);
        return id;
    }

    private static FeaturePairs AddFeatures(LcmCache cache)
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

    private static CanonicalId AddBoundaryMarker(LcmCache cache, Guid guid, string codeText)
    {
        IPhBdryMarker boundary = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single();
            boundary = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(guid);
            set.BoundaryMarkersOC.Add(boundary);
            boundary.Name.set_String(cache.DefaultVernWs, "literal hash marker");
            var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            boundary.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, codeText);
        });
        return CanonicalId.FromGuid(boundary.Guid);
    }

    private static void Apply(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = new Proposal(new Dictionary<string, string> { ["grammar"] = "1.0" },
            CanonicalId.Mint(), null, operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "prefix-root-rewrite-trace-tests");
    }

    private static ToolProcessResult Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return ToolProcess.Run(start);
    }

    private sealed record PrefixRootParts(ILexEntry Entry, IMoAffixAllomorph Alternate,
        CanonicalId R, CanonicalId T, CanonicalId A, CanonicalId Vowels, CanonicalId WordBoundary);

    private sealed record SuffixRootParts(ILexEntry Entry, CanonicalId R, CanonicalId T,
        CanonicalId A, CanonicalId Vowels);

    private sealed record FeaturePairs(PhonologicalFeatureValue FirstPositive,
        PhonologicalFeatureValue FirstNegative, PhonologicalFeatureValue SecondPositive,
        PhonologicalFeatureValue SecondNegative);
}
