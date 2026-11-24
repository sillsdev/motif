using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A phonological rule is a record file: its text and typed fields import, its formula fields are shown read-only, and its right-hand sides are not added or removed in the file.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class PhonologicalRuleRecordImportTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private static readonly Guid RuleId = Guid.Parse("00000000-0000-4000-8000-0000000000a1");
    private static readonly Guid SwapId = Guid.Parse("00000000-0000-4000-8000-0000000000a2");
    private const string Kind = "grammar/phonological-rule";

    private static void Build(LcmCache cache)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var data = cache.LangProject.PhonologicalDataOA ??= cache.ServiceLocator.GetInstance<IPhPhonDataFactory>().Create();
            var set = data.PhonemeSetsOS.FirstOrDefault() ?? cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
            if (!data.PhonemeSetsOS.Contains(set)) data.PhonemeSetsOS.Add(set);
            foreach (var old in set.PhonemesOC.ToList()) set.PhonemesOC.Remove(old);
            foreach (var old in set.BoundaryMarkersOC.ToList()) set.BoundaryMarkersOC.Remove(old);
            void Code(IPhTerminalUnit unit, string code)
            {
                unit.CodesOS.Clear();
                var phCode = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
                unit.CodesOS.Add(phCode);
                phCode.Representation.set_String(cache.DefaultVernWs, TsStringUtils.MakeString(code, cache.DefaultVernWs));
            }
            Code(cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(LangProjectTags.kguidPhRuleWordBdry, set), "#");
            Code(cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(LangProjectTags.kguidPhRuleMorphBdry, set), "+");
            var phonemes = new Dictionary<string, IPhPhoneme>();
            foreach (var code in "p t k b d g m n a i u".Split(' '))
            {
                var phoneme = cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
                set.PhonemesOC.Add(phoneme);
                Code(phoneme, code);
                phonemes[code] = phoneme;
            }
            var system = cache.LangProject.PhFeatureSystemOA ??= cache.ServiceLocator.GetInstance<IFsFeatureSystemFactory>().Create();
            var features = new Dictionary<string, IFsClosedFeature>();
            foreach (var abbreviation in new[] { "cons", "son", "voi" })
            {
                var feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
                system.FeaturesOC.Add(feature);
                feature.Abbreviation.set_String(cache.DefaultAnalWs, abbreviation);
                feature.Name.set_String(cache.DefaultAnalWs, abbreviation + " name");
                foreach (var (name, value) in new[] { ("plus", "+"), ("minus", "-") })
                {
                    var symbol = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                    feature.ValuesOC.Add(symbol);
                    symbol.Name.set_String(cache.DefaultAnalWs, name);
                    symbol.Abbreviation.set_String(cache.DefaultAnalWs, value);
                }
                features[abbreviation] = feature;
            }
            foreach (var (abbreviation, members) in new[] { ("C", "p t k b d g m n"), ("V", "a i u") })
            {
                var segments = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
                data.NaturalClassesOS.Add(segments);
                segments.Abbreviation.set_String(cache.DefaultAnalWs, abbreviation);
                segments.Name.set_String(cache.DefaultAnalWs, "class " + abbreviation);
                foreach (var code in members.Split(' ')) segments.SegmentsRC.Add(phonemes[code]);
            }
            var voiced = cache.ServiceLocator.GetInstance<IPhNCFeaturesFactory>().Create();
            data.NaturalClassesOS.Add(voiced);
            voiced.Abbreviation.set_String(cache.DefaultAnalWs, "Vd");
            voiced.Name.set_String(cache.DefaultAnalWs, "voiced");
            voiced.FeaturesOA = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            var spec = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            voiced.FeaturesOA.FeatureSpecsOC.Add(spec);
            spec.FeatureRA = features["voi"];
            spec.ValueRA = features["voi"].ValuesOC.Single(value => value.Abbreviation.get_String(cache.DefaultAnalWs).Text == "+");

            var rule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create(RuleId, data);
            rule.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("Voicing", cache.DefaultAnalWs));
            var rhs = cache.ServiceLocator.GetInstance<IPhSegRuleRHSFactory>().Create();
            rule.RightHandSidesOS.Add(rhs);
            var seg = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
            data.ContextsOS.Add(seg);
            seg.FeatureStructureRA = phonemes["p"];
            rule.StrucDescOS.Add(seg);
            var change = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
            data.ContextsOS.Add(change);
            change.FeatureStructureRA = phonemes["b"];
            rhs.StrucChangeOS.Add(change);
            var left = cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
            data.ContextsOS.Add(left);
            left.FeatureStructureRA = data.NaturalClassesOS.OfType<IPhNCSegments>().Single(c => c.Abbreviation.get_String(cache.DefaultAnalWs).Text == "V");
            rhs.LeftContextOA = left;
            var swap = cache.ServiceLocator.GetInstance<IPhMetathesisRuleFactory>().Create(SwapId, data);
            swap.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("Swap", cache.DefaultAnalWs));
            var category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(cache.DefaultAnalWs, "category");
            data.PhonRuleFeatsOA ??= cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            var feat = cache.ServiceLocator.GetInstance<IPhPhonRuleFeatFactory>().Create();
            data.PhonRuleFeatsOA.PossibilitiesOS.Add(feat);
            feat.Name.set_String(cache.DefaultAnalWs, "property");
        });
    }

    private static ProjectRecordSchemaResult SchemaOf(LcmCache cache) =>
        ProjectRecordSchema.Generate(cache, ProjectRecordSchema.PhonologicalRuleOverlay);

    private static RecordObject Rhs(RecordDocument document) =>
        ((RecordList)document.Data["right-hand-sides"]!).Items.OfType<RecordObject>().Single();

    private static ProjectRecordPlan PlanEdit(LcmCache cache, Guid rule, Action<RecordDocument> edit, out ProjectRecordSchemaResult schema)
    {
        schema = SchemaOf(cache);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(Kind, rule).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors, string.Join("\n", read.Diagnostics));
        edit(read.Document!);
        return new ProjectRecordImporter(exporter).Plan(RecordFileWriter.Write(read.Document!, schema.Schema), new CatalogWritableFields(schema));
    }

    private static RecordLowering Lower(LcmCache cache, Guid rule, Action<RecordDocument> edit)
    {
        var plan = PlanEdit(cache, rule, edit, out var schema);
        return new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
    }

    private static void Import(string path, string draft, Guid rule, Action<RecordDocument> edit)
    {
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var plan = PlanEdit(cache, rule, edit, out var schema);
            Assert.Empty(plan.ChangeSet.Refusals);
            Assert.True(ProposalCommands.New(new(path, Version, draft, "Record import")).Succeeded);
            var staged = ProposalCommands.StageRecordImport(path, draft, schema, plan);
            Assert.True(staged.Succeeded, staged.Refusal?.Message);
            Assert.Empty(staged.Value!.Lowering.Refusals);
        }
        DraftRationale.Author(path, draft, "Imported record", "The record file was edited and imported.");
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId, rebaseline: true);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(path, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
    }

    private string Start()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        return cache.ProjectId.Path;
    }

    [Fact]
    public void NameAndDescriptionImportAndReadBackOnARegularRuleAndAMetathesisRule()
    {
        var path = Start();
        Import(path, "names", RuleId, document =>
        {
            ((RecordAlternatives)document.Data["name"]!).Add("en", new RecordScalar("Intervocalic voicing"));
            document.Data.Set("description", new RecordAlternatives().Add("en", new RecordScalar("Stops voice between vowels.")));
        });
        Import(path, "swap", SwapId, document =>
            document.Data.Set("description", new RecordAlternatives().Add("en", new RecordScalar("Swaps two segments."))));
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var rule = (IPhRegularRule)cache.ServiceLocator.GetObject(RuleId);
        Assert.Equal("Intervocalic voicing", rule.Name.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal("Stops voice between vowels.", rule.Description.get_String(cache.DefaultAnalWs).Text);
        var swap = (IPhMetathesisRule)cache.ServiceLocator.GetObject(SwapId);
        Assert.Equal("Swaps two segments.", swap.Description.get_String(cache.DefaultAnalWs).Text);
        Assert.Empty(PlanEdit(cache, RuleId, _ => { }, out _).ChangeSet.Changes);
        Assert.Empty(PlanEdit(cache, SwapId, _ => { }, out _).ChangeSet.Changes);
    }

    [Fact]
    public void DirectionActiveCategoriesAndPropertiesImportThroughTypedOperations()
    {
        var path = Start();
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var lowering = Lower(cache, RuleId, document =>
            {
                document.Data.Set("direction", RecordScalar.Of(1));
                document.Data.Set("disabled", RecordScalar.Of(true));
            });
            Assert.Empty(lowering.Refusals);
            Assert.Equal(new[] { "grammar/phSegmentRule/setDirection", "grammar/phSegmentRule/setDisabled" },
                lowering.Operations.Select(operation => operation.Kind).Order());
        }
        Guid category, feat;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            category = cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Last().Guid;
            feat = cache.LangProject.PhonologicalDataOA.PhonRuleFeatsOA.PossibilitiesOS.Single().Guid;
        }
        static string Link(Guid target) => "[x](/objects/" + target.ToString("D") + "){.name guid=" + target.ToString("D") + "}";
        Import(path, "scalars", RuleId, document =>
        {
            document.Data.Set("direction", RecordScalar.Of(1));
            document.Data.Set("disabled", RecordScalar.Of(true));
            Rhs(document).Set("input-poses", new RecordList().Add(new RecordScalar(Link(category))));
            Rhs(document).Set("req-rule-feats", new RecordList().Add(new RecordScalar(Link(feat))));
        });
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var held = ((IPhRegularRule)cache.ServiceLocator.GetObject(RuleId)).RightHandSidesOS.Single();
            Assert.Equal(category, held.InputPOSesRC.Single().Guid);
            Assert.Equal(feat, held.ReqRuleFeatsRC.Single().Guid);
            var rule = (IPhRegularRule)cache.ServiceLocator.GetObject(RuleId);
            Assert.Equal(1, rule.Direction);
            Assert.True(rule.Disabled);
            Assert.Empty(PlanEdit(cache, RuleId, _ => { }, out _).ChangeSet.Changes);
        }
    }

    [Fact]
    public void TheFormulaFieldsAreShownAndRefusedAsReadOnlyOnImport()
    {
        var path = Start();
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var shown = SchemaOf(cache);
        var export = new ProjectRecordExporter(cache, shown).Export(Kind, RuleId);
        Assert.Equal("p", ((RecordScalar)export.Document.Data["struc-desc"]!).Text);
        Assert.Equal("b", ((RecordScalar)Rhs(export.Document)["struc-change"]!).Text);
        Assert.Equal("[V]", ((RecordScalar)Rhs(export.Document)["left-context"]!).Text);
        var lowering = Lower(cache, RuleId, document =>
        {
            document.Data.Set("struc-desc", new RecordScalar("t"));
            Rhs(document).Set("struc-change", new RecordScalar("d"));
            Rhs(document).Set("left-context", new RecordScalar("[C]"));
            Rhs(document).Set("right-context", new RecordScalar("[V]"));
        });
        Assert.Empty(lowering.Operations);
        Assert.Equal(4, lowering.Refusals.Count);
        Assert.All(lowering.Refusals, refusal => Assert.Equal(RefusalCodes.RuleNotationReadOnly, refusal.Code));
    }

    [Fact]
    public void AddingOrRemovingARightHandSideInTheFileIsRefused()
    {
        var path = Start();
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var added = PlanEdit(cache, RuleId, document =>
            ((RecordList)document.Data["right-hand-sides"]!).Add(new RecordObject { Guid = Guid.NewGuid() }), out _);
        Assert.Empty(added.ChangeSet.Changes);
        Assert.Contains(added.ChangeSet.Refusals, refusal => refusal.Kind == RecordRefusalKind.CreateNotWritable);
        var removed = PlanEdit(cache, RuleId, document => document.Data["right-hand-sides"] = null, out _);
        Assert.Empty(removed.ChangeSet.Changes);
        Assert.Contains(removed.ChangeSet.Refusals, refusal => refusal.Kind == RecordRefusalKind.DeleteNotWritable);
    }

    [Fact]
    public void TheCatalogOffersTheTypedFieldsAndNotTheRightHandSideMembership()
    {
        using var cache = pristine.NewScratch();
        var catalog = new CatalogWritableFields(SchemaOf(cache));
        Assert.True(catalog.CanWrite("PhSegmentRule", "Name"));
        Assert.True(catalog.CanWrite("PhSegmentRule", "Direction"));
        Assert.True(catalog.CanWrite("PhSegmentRule", "Disabled"));
        Assert.True(catalog.CanWrite("PhSegRuleRHS", "InputPOSes"));
        Assert.False(catalog.CanCreate("PhRegularRule", "RightHandSides", "PhSegRuleRHS"));
        Assert.False(catalog.CanDelete("PhRegularRule", "RightHandSides", "PhSegRuleRHS"));
    }
}
