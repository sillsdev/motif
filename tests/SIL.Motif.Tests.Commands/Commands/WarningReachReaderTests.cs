using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Projection.Grammar;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Pins reverse references in a SYNTHETIC EXAMPLE noun grammar, without parser inference.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class WarningReachReaderTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AllomorphAndEntryKeepBaselineSpellingsForRefusedTypedWords()
    {
        using var project = new NoTextsWarningProject(pristine);
        using var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath);
        var formId = project.Allomorphs.Single(item => item.Form == "fenêtre").AllomorphId;
        var form = cache.ServiceLocator.ObjectRepository.GetObject(formId);
        var entry = form.Owner;
        foreach (var subject in new[] { form, entry })
        {
            var reach = WarningReachReader.Reach(Subject(subject), () => cache)!;
            Assert.Contains("fenêtre".Normalize(System.Text.NormalizationForm.FormD), reach.Spellings);
        }
        Assert.Empty(cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances());
    }

    [Theory]
    [InlineData("template", "membership", "msa")]
    [InlineData("slot", "membership", "msa")]
    [InlineData("feature", "through_feature_owners", "msa")]
    [InlineData("value", "through_feature_owners", "msa")]
    [InlineData("specification", "through_feature_owners", "msa")]
    [InlineData("stem-name", "through_allomorphs", "form")]
    [InlineData("inflection-class", "through_grammatical_info", "stem-msa")]
    [InlineData("entry-type", "membership", "form")]
    [InlineData("allomorph-prohibition", "membership", "form")]
    [InlineData("morpheme-prohibition", "membership", "msa")]
    [InlineData("boundary", "through_environments_and_rules", "rule")]
    [InlineData("phoneme-set", "membership", "rule")]
    [InlineData("feature-system", "membership", "msa")]
    public void ReverseRouteReachesOnlyItsReferencedOwner(string name, string path, string target)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var objects = Author(cache);
        var item = objects[name];
        var reach = WarningReachReader.Reach(Subject(item), () => cache)!;

        Assert.Equal(path, WirePath(reach));
        var expected = objects[target].Guid.ToString("D");
        if (target is "msa" or "stem-msa") Assert.Contains(expected, reach.GrammaticalInfoIds);
        if (target == "form") Assert.Contains(expected, reach.AllomorphIds);
        if (target == "rule") Assert.Contains(new TraceTimingKey("phon_rule", expected), reach.TimingKeys);
        Assert.DoesNotContain(pristine.Seed.SecondLexemeFormId.ToString("D"), reach.AllomorphIds);

        var morph = new ParserReadingMorph("synthetic", "plural", "noun", null, false, null)
        {
            AllomorphId = objects["form"].Guid.ToString("D"),
            GrammaticalInfoId = objects[target is "msa" or "stem-msa" ? target : "msa"].Guid.ToString("D"),
        };
        var word = new AssessmentWordResult("synthetic", "no-analysis", false, "Search completed", 1, null)
        {
            StoredAnalyses = [new ParserReading([morph]) { StoredAnalysisOpinion = ReadingGrade.Approved }],
        };
        var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, name,
            [Subject(item) with { Reach = reach }], [], name);
        var timings = target == "rule"
            ? new[] { new AssessmentObjectTiming("phon_rule", expected, "authored", "analysis", "Boundary", "synthetic", 1, null, 10) }
            : Array.Empty<AssessmentObjectTiming>();
        var matched = WarningWordsQuery.YourWordsOf(finding, [word], timings)!;
        Assert.Equal(path == "membership" ? WarningAttributionState.MembershipCandidates : WarningAttributionState.ExactUses,
            matched.State);
        Assert.Equal("synthetic", Assert.Single(matched.Words).Row.Word);
        Assert.Equal(WarningAttributionState.NoneInSelection, WarningWordsQuery.YourWordsOf(finding, [], [])!.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NaturalClassNotationDoesNotEstablishExactIdentity(bool fallbackName)
    {
        var grammar = WarningGrammar.Author(pristine);
        using var cache = new FwDataProjectLoader().LoadScratchCache(grammar.FwDataPath);
        var first = cache.ServiceLocator.GetInstance<IPhNaturalClassRepository>().GetObject(grammar.Vowels);
        IPhNaturalClass second = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            second = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(second);
            second.Abbreviation.set_String(cache.DefaultAnalWs, fallbackName ? "" : "V");
            second.Name.set_String(cache.DefaultAnalWs, "V");
            if (fallbackName)
            {
                first.Abbreviation.set_String(cache.DefaultAnalWs, "");
                first.Name.set_String(cache.DefaultAnalWs, "V");
            }
        });
        foreach (var naturalClass in new[] { first, second })
        {
            var reach = WarningReachReader.Reach(Subject(naturalClass) with { FieldWorksKind = "PhNaturalClass" }, () => cache)!;
            Assert.Empty(reach.AllomorphIds);
            Assert.NotEmpty(reach.AttributionLimits);
            var word = new AssessmentWordResult("synthetic", "no-analysis", false, "Search completed", 1, null)
            {
                StoredAnalyses = [new ParserReading([new ParserReadingMorph("synthetic", "root", "noun", null, false, null)
                    { AllomorphId = pristine.Seed.SecondLexemeFormId.ToString("D") }])
                    { StoredAnalysisOpinion = ReadingGrade.Approved }],
            };
            var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "class", [Subject(naturalClass) with { Reach = reach }], [], "class");
            var matches = WarningWordsQuery.YourWordsOf(finding, [word], [])!;
            Assert.Empty(matches.Words);
            Assert.Equal(0, WarningWordsQuery.Touched([finding with { YourWords = matches }])!.Words);
        }
        var direct = WarningReachReader.Reach(Subject(first) with { FieldWorksKind = "PhNaturalClass" }, () => cache)!;
        Assert.Contains(new TraceTimingKey("phon_rule", grammar.Harmony.ToString("D")), direct.TimingKeys);
        Assert.Empty(WarningReachReader.Reach(Subject(second) with { FieldWorksKind = "PhNaturalClass" }, () => cache)!.TimingKeys);
    }

    [Fact]
    public void MatchingNormalizedKindCannotOverrideAConflictingSourceClass()
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var msa = Author(cache)["msa"];
        var reach = WarningReachReader.Reach(Subject(msa) with { SourceClass = "LexSense" }, () => cache)!;
        Assert.Equal(WarningAttributionReason.WrongClass, reach.Reason);
        Assert.Empty(reach.GrammaticalInfoIds);
    }

    [Fact]
    public void GenericGrammaticalInfoKindUsesItsExactGuid()
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var msa = Author(cache)["msa"];
        var reach = WarningReachReader.Reach(Subject(msa) with
            { FieldWorksKind = "MoMorphSynAnalysis", SourceClass = msa.ClassName }, () => cache)!;
        Assert.Equal(WarningWordsPath.Uses, reach.Path);
        Assert.Equal([msa.Guid.ToString("D")], reach.GrammaticalInfoIds);
    }

    [Fact]
    public void EmptyProjectResourcesHaveNoWordAttribution()
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        IPhPhonemeSet set = null!;
        IFsFeatureSystem system = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            set = cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
            OwnedBoundaryMarkerFixture.EnsureReservedMarkers(cache, set);
            system = cache.ServiceLocator.GetInstance<IFsFeatureSystemFactory>().Create();
            cache.LangProject.PhFeatureSystemOA = system;
        });
        Assert.Equal("project_wide", WirePath(WarningReachReader.Reach(Subject(set), () => cache)!));
        Assert.Equal("project_wide", WirePath(WarningReachReader.Reach(Subject(system), () => cache)!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedComplexAndSharedSpecificationsReachTheirLexicalOwners(bool shareContainer)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var objects = Author(cache);
        IFsComplexFeature complex = null!;
        IMoStemMsa other = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            complex = cache.ServiceLocator.GetInstance<IFsComplexFeatureFactory>().Create();
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(complex);
            var outer = ((IMoInflAffMsa)objects["msa"]).InflFeatsOA;
            var nested = cache.ServiceLocator.GetInstance<IFsComplexValueFactory>().Create();
            outer.FeatureSpecsOC.Add(nested);
            nested.FeatureRA = complex;
            nested.ValueOA = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            var nestedFs = (IFsFeatStruc)nested.ValueOA;
            var closed = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            nestedFs.FeatureSpecsOC.Add(closed);
            closed.FeatureRA = (IFsClosedFeature)objects["feature"];
            closed.ValueRA = (IFsSymFeatVal)objects["value"];
            other = (IMoStemMsa)cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                .GetObject(pristine.Seed.SecondEntryId).MorphoSyntaxAnalysesOC.First();
            other.MsFeaturesOA = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            var shared = cache.ServiceLocator.GetInstance<IFsSharedValueFactory>().Create();
            other.MsFeaturesOA.FeatureSpecsOC.Add(shared);
            shared.ValueRA = shareContainer ? nested : closed;
        });
        var complexReach = WarningReachReader.Reach(Subject(complex), () => cache)!;
        Assert.Contains(objects["msa"].Guid.ToString("D"), complexReach.GrammaticalInfoIds);
        var valueReach = WarningReachReader.Reach(Subject(objects["value"]), () => cache)!;
        Assert.Contains(other.Guid.ToString("D"), valueReach.GrammaticalInfoIds);
        var featureReach = WarningReachReader.Reach(Subject(objects["feature"]), () => cache)!;
        Assert.Contains(other.Guid.ToString("D"), featureReach.GrammaticalInfoIds);
        var word = new AssessmentWordResult("synthetic", "no-analysis", false, "Search completed", 1, null)
        {
            StoredAnalyses = [new ParserReading([new ParserReadingMorph("synthetic", "feature", "noun", null, false, null)
                { GrammaticalInfoId = other.Guid.ToString("D") }]) { StoredAnalysisOpinion = ReadingGrade.Approved }],
        };
        foreach (var (subject, reach) in new[] { (objects["feature"], featureReach), (objects["value"], valueReach) })
        {
            var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "feature",
                [Subject(subject) with { Reach = reach }], [], "feature");
            finding = finding with { YourWords = WarningWordsQuery.YourWordsOf(finding, [word], []) };
            Assert.Equal(WarningAttributionState.ExactUses, finding.AttributionState);
            Assert.Equal("synthetic", Assert.Single(finding.YourWords!.Words).Row.Word);
            Assert.Equal(1, WarningWordsQuery.Touched([finding])!.Words);
        }
    }

    [Fact]
    public void BoundaryReverseReferencesKeepWordEdgeSeparateFromLiteralHash()
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var objects = Author(cache);
        var set = Assert.IsAssignableFrom<IPhPhonemeSet>(objects["phoneme-set"]);
        var literal = Assert.IsAssignableFrom<IPhBdryMarker>(objects["boundary"]);
        IPhBdryMarker word = null!;
        IPhRegularRule wordRule = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var literalCode = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            literal.CodesOS.Add(literalCode);
            literalCode.Representation.set_String(cache.DefaultVernWs, "#");

            word = set.BoundaryMarkersOC.Single(marker => marker.Guid == LangProjectTags.kguidPhRuleWordBdry);

            wordRule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(wordRule);
            var context = cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create();
            wordRule.StrucDescOS.Add(context);
            context.FeatureStructureRA = word;
        });

        var wordReach = WarningReachReader.Reach(Subject(word), () => cache)!;
        var literalReach = WarningReachReader.Reach(Subject(literal), () => cache)!;
        var wordTiming = new TraceTimingKey("phon_rule", wordRule.Guid.ToString("D"));
        var literalTiming = new TraceTimingKey("phon_rule", ((IPhRegularRule)objects["rule"]).Guid.ToString("D"));

        Assert.Contains(wordTiming, wordReach.TimingKeys);
        Assert.DoesNotContain(literalTiming, wordReach.TimingKeys);
        Assert.Contains(literalTiming, literalReach.TimingKeys);
        Assert.DoesNotContain(wordTiming, literalReach.TimingKeys);
    }

    [Fact]
    public void SharedSpecificationCyclesReachEachOwnerOnce()
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var objects = Author(cache);
        var firstOwner = (IMoInflAffMsa)objects["msa"];
        var secondOwner = (IMoStemMsa)cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.SecondEntryId).MorphoSyntaxAnalysesOC.First();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            secondOwner.MsFeaturesOA = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            var first = cache.ServiceLocator.GetInstance<IFsSharedValueFactory>().Create();
            firstOwner.InflFeatsOA.FeatureSpecsOC.Add(first);
            first.FeatureRA = (IFsClosedFeature)objects["feature"];
            var second = cache.ServiceLocator.GetInstance<IFsSharedValueFactory>().Create();
            secondOwner.MsFeaturesOA.FeatureSpecsOC.Add(second);
            first.ValueRA = second;
            second.ValueRA = first;
        });

        var reach = WarningReachReader.Reach(Subject(objects["feature"]), () => cache)!;
        Assert.Equal(new[] { firstOwner.Guid.ToString("D"), secondOwner.Guid.ToString("D") }.Order(),
            reach.GrammaticalInfoIds.Order());
    }

    [Theory]
    [InlineData("MoForm")]
    [InlineData("MoStemMsa")]
    [InlineData("PhRegularRule")]
    [InlineData("PhPhoneme")]
    [InlineData("MoInflAffixTemplate")]
    public void MissingAndWrongClassGuidsAreDistinctEvenOnDirectRoutes(string kind)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var missing = new GrammarWarningPart("missing", GrammarWarningPartRole.Object, "id", kind)
            { SubjectGuid = Guid.NewGuid().ToString("D"), Title = "u" };
        var wrong = missing with { SubjectGuid = pristine.Seed.FirstSenseId.ToString("D") };
        Assert.Equal("stale_guid", WireReason(WarningReachReader.Reach(missing, () => cache)!));
        Assert.Equal("wrong_class", WireReason(WarningReachReader.Reach(wrong, () => cache)!));
    }

    [Fact]
    public void NamedWithoutProjectGuidDoesNotBecomeNoSubject()
    {
        var part = new GrammarWarningPart("Plural", GrammarWarningPartRole.Text, FieldWorksKind: "MoInflAffixSlot");
        var reach = WarningReachReader.Reach(part, () => throw new InvalidOperationException())!;
        Assert.NotNull(reach);
        Assert.Equal("named_without_project_guid", WireReason(reach));
    }

    private static GrammarWarningPart Subject(ICmObject item) =>
        new(item.ClassName, GrammarWarningPartRole.Object, item.Guid.ToString("D"), item.ClassName)
            { SubjectGuid = item.Guid.ToString("D") };

    private static string? WirePath(WarningReach reach) =>
        JsonSerializer.SerializeToElement(reach, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .GetProperty("path").GetString();

    private static string? WireReason(WarningReach reach) =>
        JsonSerializer.SerializeToElement(reach, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .GetProperty("reason").GetString();

    private Dictionary<string, ICmObject> Author(LcmCache cache)
    {
        var s = cache.ServiceLocator;
        var entry = s.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var form = (IMoStemAllomorph)entry.LexemeFormOA;
        var msa = (IMoStemMsa)entry.MorphoSyntaxAnalysesOC.First();
        var objects = new Dictionary<string, ICmObject> { ["form"] = form, ["msa"] = msa, ["stem-msa"] = msa, ["none"] = entry };
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var pos = s.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            msa.PartOfSpeechRA = pos;
            var slot = s.GetInstance<IMoInflAffixSlotFactory>().Create();
            pos.AffixSlotsOC.Add(slot);
            var template = s.GetInstance<IMoInflAffixTemplateFactory>().Create();
            pos.AffixTemplatesOS.Add(template);
            template.SuffixSlotsRS.Add(slot);
            var infl = s.GetInstance<IMoInflAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(infl);
            infl.PartOfSpeechRA = pos;
            infl.SlotsRC.Add(slot);
            objects["template"] = template;
            objects["slot"] = slot;
            objects["msa"] = infl;

            var feature = s.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            var value = s.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
            var fs = s.GetInstance<IFsFeatStrucFactory>().Create();
            infl.InflFeatsOA = fs;
            var spec = s.GetInstance<IFsClosedValueFactory>().Create();
            fs.FeatureSpecsOC.Add(spec);
            spec.FeatureRA = feature;
            spec.ValueRA = value;
            objects["feature"] = feature;
            objects["value"] = value;
            objects["specification"] = spec;
            objects["feature-system"] = cache.LangProject.MsFeatureSystemOA;

            var stemName = s.GetInstance<IMoStemNameFactory>().Create();
            pos.StemNamesOC.Add(stemName);
            stemName.RegionsOC.Add(s.GetInstance<IFsFeatStrucFactory>().Create());
            form.StemNameRA = stemName;
            objects["stem-name"] = stemName;
            var inflClass = s.GetInstance<IMoInflClassFactory>().Create();
            pos.InflectionClassesOC.Add(inflClass);
            msa.InflectionClassRA = inflClass;
            objects["inflection-class"] = inflClass;

            var type = s.GetInstance<ILexEntryInflTypeFactory>().Create();
            cache.LangProject.LexDbOA.VariantEntryTypesOA.PossibilitiesOS.Add(type);
            var reference = s.GetInstance<ILexEntryRefFactory>().Create();
            entry.EntryRefsOS.Add(reference);
            reference.VariantEntryTypesRS.Add(type);
            reference.ComponentLexemesRS.Add(s.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.SecondEntryId));
            objects["entry-type"] = type;
            var allos = s.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(allos);
            allos.FirstAllomorphRA = form;
            objects["allomorph-prohibition"] = allos;
            var morphs = s.GetInstance<IMoMorphAdhocProhibFactory>().Create();
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(morphs);
            morphs.FirstMorphemeRA = infl;
            objects["morpheme-prohibition"] = morphs;

            var set = s.GetInstance<IPhPhonemeSetFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
            OwnedBoundaryMarkerFixture.EnsureReservedMarkers(cache, set);
            var boundary = s.GetInstance<IPhBdryMarkerFactory>().Create();
            set.BoundaryMarkersOC.Add(boundary);
            var rule = s.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            var context = s.GetInstance<IPhSimpleContextBdryFactory>().Create();
            rule.StrucDescOS.Add(context);
            context.FeatureStructureRA = boundary;
            objects["boundary"] = boundary;
            objects["rule"] = rule;
            objects["phoneme-set"] = set;
        });
        return objects;
    }
}
