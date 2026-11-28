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

/// <summary>An affix process's Input and Output are one rule-formula key each; an import of the text writes the objects FieldWorks's affix editor makes.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RuleFormulaImportTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private static readonly Guid EntryId = Guid.Parse("00000000-0000-4000-8000-0000000000e2");

    private static void Build(LcmCache cache, bool ambiguous = false)
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
            foreach (var code in "p t k b d g m n a i u".Split(' ').Concat(ambiguous ? ["p2"] : []))
            {
                var phoneme = cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
                set.PhonemesOC.Add(phoneme);
                Code(phoneme, code == "p2" ? "p" : code);
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

            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(EntryId, cache.LangProject.LexDbOA);
            var prefix = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphPrefix);
            var lexeme = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.LexemeFormOA = lexeme;
            lexeme.MorphTypeRA = prefix;
            var category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(cache.DefaultAnalWs, "category");
            var msa = cache.ServiceLocator.GetInstance<IMoUnclassifiedAffixMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = category;
            var sense = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.MorphoSyntaxAnalysisRA = msa;
            var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.AlternateFormsOS.Add(process);
            process.MorphTypeRA = prefix;
            process.IsAbstract = false;
            process.InputOS.Clear();
            process.OutputOS.Clear();
            var gap = cache.ServiceLocator.GetInstance<IPhVariableFactory>().Create();
            process.InputOS.Add(gap);
            var copy = cache.ServiceLocator.GetInstance<IMoCopyFromInputFactory>().Create();
            process.OutputOS.Add(copy);
            copy.ContentRA = gap;
            var insert = cache.ServiceLocator.GetInstance<IMoInsertPhonesFactory>().Create();
            process.OutputOS.Add(insert);
            insert.ContentRS.Add(phonemes["m"]);
        });
    }

    private static RecordObject Process(RecordDocument document) =>
        ((RecordList)document.Data["allomorphs"]!).Items.OfType<RecordObject>().Single();

    private static string FormulaOf(LcmCache cache, string key)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exported = new ProjectRecordExporter(cache, schema).Export(EntryId);
        return ((RecordScalar)Process(exported.Document)[key]!).Text;
    }

    private static ProjectRecordPlan PlanEdit(LcmCache cache, Action<RecordObject> edit, out ProjectRecordSchemaResult schema)
    {
        schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(EntryId).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        edit(Process(read.Document!));
        return new ProjectRecordImporter(exporter).Plan(RecordFileWriter.Write(read.Document!, schema.Schema), new CatalogWritableFields(schema));
    }

    private static RecordLowering Lower(LcmCache cache, string? input, string? output)
    {
        var plan = PlanEdit(cache, process =>
        {
            if (input is not null) process.Set("input", new RecordScalar(input));
            if (output is not null) process.Set("output", new RecordScalar(output));
        }, out var schema);
        Assert.Empty(plan.ChangeSet.Refusals);
        return new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
    }

    private static void Refused(LcmCache cache, string? input, string? output, string code)
    {
        var lowering = Lower(cache, input, output);
        Assert.Empty(lowering.Operations);
        Assert.All(lowering.Refusals, refusal => Assert.Equal(code, refusal.Code));
        Assert.NotEmpty(lowering.Refusals);
    }

    /// <summary>Imports the texts through a real draft, finalize and apply.</summary>
    private static void Import(string path, string draft, string? input, string? output)
    {
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var plan = PlanEdit(cache, process =>
            {
                if (input is not null) process.Set("input", new RecordScalar(input));
                if (output is not null) process.Set("output", new RecordScalar(output));
            }, out var schema);
            Assert.Empty(plan.ChangeSet.Refusals);
            if (plan.ChangeSet.Changes.Count == 0)
                return;
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

    private (string Path, int Step) Start()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        return (cache.ProjectId.Path, 0);
    }

    /// <summary>Applies each pair in turn and requires the project to export exactly that text.</summary>
    private void Walk(params (string? Input, string? Output)[] steps)
    {
        var (path, _) = Start();
        for (var i = 0; i < steps.Length; i++)
        {
            Import(path, "step" + i, steps[i].Input, steps[i].Output);
            using var cache = new FwDataProjectLoader().LoadCache(path);
            if (steps[i].Input is { } input) Assert.Equal(input, FormulaOf(cache, "input"));
            if (steps[i].Output is { } output) Assert.Equal(output, FormulaOf(cache, "output"));
            Assert.Empty(PlanEdit(cache, _ => { }, out _).ChangeSet.Changes);
            Assert.Equal(cache.LangProject.PhonologicalDataOA.ContextsOS.Count,
                cache.LangProject.PhonologicalDataOA.ContextsOS.Count(context => context.ReferringObjects.Any()));
        }
    }

    [Fact]
    public void TheFormulaIsTheOneTextKeyAndThePartsKeyReachesTheMembers()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var process = Process(new ProjectRecordExporter(cache, schema).Export(EntryId).Document);
        Assert.Equal("*", ((RecordScalar)process["input"]!).Text);
        Assert.Equal("$1 m", ((RecordScalar)process["output"]!).Text);
        Assert.NotNull(process["input-parts"]);
        Assert.NotNull(process["output-parts"]);
    }

    // ---- worked examples 1-5 of the design, through real import and export

    [Fact] public void Example1PrefixInsertsTwoPhonemes() => Walk(("*", "m a $1"));

    [Fact] public void Example2InfixAfterTheFirstConsonant() => Walk(("[C] *", "$1 u m $2"));

    [Fact] public void Example3PartialReduplicationWithATwoContextColumn() => Walk(("<[C] [V]> *", "$1 $1 $2"));

    [Fact] public void Example4ModifyWithAnAnonymousClass() => Walk(("[C] *", "$1~[voi:+] $2"));

    [Fact] public void Example4bModifyWithANamedClass() => Walk(("[C] *", "$1~[Vd] $2"));

    [Fact] public void Example5InsertAMorphemeBoundaryAndAClass() => Walk(("* [C]", "a + $1 $2 [V]"));

    // ---- every edit the object-list keys accepted is a formula edit

    [Theory]
    [InlineData("p *", "$1 $2 a", "[C] *", "$1 $2 a")]               // PhSimpleContextSeg then PhSimpleContextNC; segment context feature-structure set
    [InlineData("[C] *", "$1 $2 a", "[V] *", "$1 $2 a")]             // PhSimpleContextNC feature-structure set
    [InlineData("[C] *", "$1 $2 a", "# *", "$1 $2 a")]               // PhSimpleContextBdry created in place of a class context
    [InlineData("# *", "$1 $2 a", "+ *", "$1 $2 a")]                 // PhSimpleContextBdry feature-structure set
    [InlineData("*", "$1 a", "* *", "$1 $2 a")]                      // input AddObject variable, with the output that copies it
    [InlineData("[C] * [V]", "$1 $2 $3 a", "[C] *", "$1 $2 a")]      // input RemoveObject, with the output that copied it
    [InlineData("[C] *", "$1 $2 a", "* [C]", "$2 $1 a")]             // input MoveObject
    [InlineData("<[C] [V]> *", "$1 $2 a", "<[C] [V] [C]> *", "$1 $2 a")]  // PhSequenceContext members add
    [InlineData("<[C] [V] [C]> *", "$1 $2 a", "<[C] [V]> *", "$1 $2 a")]  // members remove; the dropped member context goes with it
    [InlineData("<[C] [V]> *", "$1 $2 a", "<[V] [C]> *", "$1 $2 a")]      // members move
    [InlineData("<[C] [V]> *", "$1 $2 a", "<[C] a> *", "$1 $2 a")]        // member feature-structure set
    [InlineData("<[C] [V]> *", "$1 $2 a", "[C] *", "$1 $2 a")]            // a column becomes a bare context; its members go
    public void InputColumnsAreEditedByTheirText(string firstInput, string firstOutput, string secondInput, string secondOutput) =>
        Walk((firstInput, firstOutput), (secondInput, secondOutput));

    [Theory]
    [InlineData("$1 m", "$1 m a")]                   // output AddObject insert phones
    [InlineData("$1 m", "$1 m [V]")]                 // output AddObject insert class
    [InlineData("$1 m", "$1 $1 m")]                  // output AddObject copy
    [InlineData("$1 m", "$1~[voi:+] m")]             // output AddObject modify
    [InlineData("$1 [V]", "$1 [C]")]                 // insert class content set
    [InlineData("$1 [V] m", "$1 m")]                 // insert class content clear, as the step's removal
    [InlineData("$1 a", "$1 <a i>")]                 // insert phones content add
    [InlineData("$1 <a i>", "$1 a")]                 // insert phones content remove
    [InlineData("$1 <a i>", "$1 + <i a>")]           // multi-unit insert with a boundary marker
    [InlineData("$1~[voi:+]", "$1~[Vd]")]            // modification set
    [InlineData("$1~[Vd]", "$1~[cons:+]")]           // modification set to a new anonymous class
    [InlineData("$1~[Vd] a", "$1 a")]                // modify removed
    [InlineData("a $1 i", "i $1 a")]                 // output MoveObject
    public void OutputStepsAreEditedByTheirText(string first, string second) =>
        Walk(("[C] *", first + " $2"), ("[C] *", second + " $2"));

    [Fact]
    public void ACopyAndAModifyPointAtTheColumnTheTextNames() => Walk(("[C] *", "$1 $2 a"), ("[C] *", "$2 $1 a"), ("[C] [V] *", "$2~[voi:+] $1 $3"));

    [Fact]
    public void AnInputAndOutputEditedTogetherSetEveryContentAgainstTheNewInput() =>
        Walk(("[C] *", "$1 a $2"), ("[V] [C] *", "$2 a $3 $1"));

    [Fact]
    public void EachModifyGetsItsOwnAnonymousClassAsFieldWorksCreatesIt()
    {
        var (path, _) = Start();
        Import(path, "twice", "[C] [C] *", "$1~[voi:+] $2~[voi:+] $3");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var process = cache.LangProject.LexDbOA.Entries.Single(e => e.Guid == EntryId).AlternateFormsOS.OfType<IMoAffixProcess>().Single();
        var classes = process.OutputOS.OfType<IMoModifyFromInput>().Select(modify => modify.ModificationRA).ToList();
        Assert.Equal(2, classes.Distinct().Count());
        Assert.All(classes, created => Assert.StartsWith("Created automatically for rule", created.Name.BestAnalysisAlternative.Text));
        Assert.All(classes, created => Assert.True(string.IsNullOrEmpty(created.Abbreviation.BestAnalysisAlternative.Text) ||
            created.Abbreviation.BestAnalysisAlternative.Text.StartsWith("***", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnAnonymousClassAnotherRuleUsesIsNotSharedWithThisOne()
    {
        var (path, _) = Start();
        Import(path, "first", "[C] *", "$1~[voi:+] $2");
        Import(path, "clear", "[C] *", "$1 $2 a");
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var shared = cache.LangProject.PhonologicalDataOA.NaturalClassesOS.OfType<IPhNCFeatures>()
                    .Single(c => c.Name.BestAnalysisAlternative.Text.StartsWith("Created automatically for rule", StringComparison.Ordinal));
                var other = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
                cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(Guid.NewGuid(), cache.LangProject.LexDbOA).AlternateFormsOS.Add(other);
                var modify = cache.ServiceLocator.GetInstance<IMoModifyFromInputFactory>().Create();
                other.OutputOS.Add(modify);
                modify.ModificationRA = shared;
            });
            new FwDataProjectLoader().Save(cache);
        }
        // FieldWorks: AffixRuleFormulaControl.SetMappingFeatures makes a new class per modify.
        using var probe = new FwDataProjectLoader().LoadCache(path);
        var lowering = Lower(probe, null, "$1~[voi:+] $2");
        Assert.Empty(lowering.Refusals);
        Assert.Single(lowering.Operations, operation => operation.Kind == "grammar/phPhonData/createNaturalClasses");
    }

    [Theory]
    [InlineData("[C] *", "$1 $2~[voi:+]")]
    [InlineData("*", "$1~[voi:+]")]
    public void AModifyOfAStemVariableIsRefusedAsFieldWorksRefusesIt(string input, string output)
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        Refused(cache, input, output, RefusalCodes.RuleNotationContextNotAllowed);
    }

    [Fact]
    public void AnInputOnlyChangeThatRetargetsAModifyAtAStemVariableIsRefused()
    {
        var (path, _) = Start();
        Import(path, "modify", "[C] [V] *", "$1~[voi:+] $2 $3");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        Refused(cache, "* [V] *", null, RefusalCodes.RuleNotationContextNotAllowed);
    }

    [Fact]
    public void AMemberFieldTheFormulaExpressesIsNotWritableThroughThePartsKey()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        var plan = PlanEdit(cache, process =>
        {
            process.Set("output", new RecordScalar("m $1"));
            ((RecordObject)((RecordList)process["output-parts"]!).Items[0]).Fields.RemoveAll(field => field.Key == "content");
        }, out _);
        Assert.Contains(plan.ChangeSet.Refusals, refusal => refusal.Kind == RecordRefusalKind.FieldNotWritable);
    }

    // The context constraint rows are written by the phonological rule formulas, not the affix-process ones.
    private HashSet<string> PhonologicalRuleKinds()
    {
        using var scratch = pristine.NewScratch();
        PhonologicalRuleRecordImportTests.Build(scratch);
        new FwDataProjectLoader().Save(scratch);
        var ruleId = scratch.LangProject.PhonologicalDataOA.PhonRulesOS.OfType<IPhRegularRule>().Single().Guid;
        var path = scratch.ProjectId.Path;
        var kinds = new HashSet<string>();
        void Probe(string left)
        {
            using var cache = new FwDataProjectLoader().LoadCache(path);
            kinds.UnionWith(PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document =>
                PhonologicalRuleRecordImportTests.Rhs(document).Set("left-context", new RecordScalar(left))).Operations.Select(operation => operation.Kind));
        }
        void Seed(string draft, string left) => PhonologicalRuleRecordImportTests.Import(path, draft, ruleId, document =>
            PhonologicalRuleRecordImportTests.Rhs(document).Set("left-context", new RecordScalar(left)));
        Probe("[V voi:α]");
        Seed("agree", "[V voi:α]");
        Probe("[V voi:-α]");
        Probe("[V]");
        Seed("disagree", "[V voi:-α]");
        Probe("[V]");
        return kinds;
    }

    [Fact]
    public void EveryMemberFieldCoveredAsWrittenByAFormulaKeyIsWrittenByAFormula()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        var written = new[] { ("p + [C] <[C] [V]> *", "$1 $2 $3~[voi:+] $4 $5"), ("*", "m <a i> [V] $1") }
            .SelectMany(pair => Lower(cache, pair.Item1, pair.Item2).Operations).Select(operation => operation.Kind).ToHashSet();
        var (path, _) = Start();
        Import(path, "seed", "<[C] [V]> *", "$1 $2 <a i>");
        using (var seeded = new FwDataProjectLoader().LoadCache(path))
            written.UnionWith(Lower(seeded, "<[C] a p> *", "$1 $2 <a p>").Operations.Select(operation => operation.Kind));
        written.UnionWith(PhonologicalRuleKinds());
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var owned = new RecordWritableFields(schema).Entries.Where(entry => entry.Value is WritableFieldResolution.Covered { Owner: not null }).ToList();
        Assert.NotEmpty(owned);
        Assert.All(owned, entry => Assert.Contains(((WritableFieldResolution.Covered)entry.Value).Kind, written));
    }


    // ---- in-place lowering: a field that was only edited keeps its object

    private static (List<Guid> Inputs, List<Guid> Outputs) Members(string path)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var process = cache.LangProject.LexDbOA.Entries.Single(e => e.Guid == EntryId).AlternateFormsOS.OfType<IMoAffixProcess>().Single();
        return (process.InputOS.Select(item => item.Guid).ToList(), process.OutputOS.Select(item => item.Guid).ToList());
    }

    private static Guid? ContentOf(string path, Guid output)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var mapping = cache.ServiceLocator.GetObject(output);
        return mapping switch { IMoCopyFromInput copy => copy.ContentRA?.Guid, IMoModifyFromInput modify => modify.ContentRA?.Guid, _ => null };
    }

    [Fact]
    public void AFeatureChangeInOneColumnKeepsEveryGuid()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] [V] *", "$1 $2 $3 a");
        var before = Members(path);
        Import(path, "two", "[C] [Vd] *", "$1 $2 $3 a");
        var after = Members(path);
        Assert.Equal(before.Inputs, after.Inputs);
        Assert.Equal(before.Outputs, after.Outputs);
        Assert.Equal(after.Inputs[2], ContentOf(path, after.Outputs[2]));
    }

    [Fact]
    public void AReorderKeepsGuidsByPositionWithChangedContents()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] [V] *", "$1 $2 $3 a");
        var before = Members(path);
        Import(path, "two", "[V] [C] *", "$3 $1 $2 a");
        var after = Members(path);
        Assert.Equal(before.Inputs, after.Inputs);
        Assert.Equal(before.Outputs, after.Outputs);
        Assert.Equal(after.Inputs[2], ContentOf(path, after.Outputs[0]));
        Assert.Equal(after.Inputs[0], ContentOf(path, after.Outputs[1]));
        Assert.Equal(after.Inputs[1], ContentOf(path, after.Outputs[2]));
    }

    [Fact]
    public void AnInsertionAtTheEndKeepsTheEarlierGuids()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] *", "$1 $2 a");
        var before = Members(path);
        Import(path, "two", "[C] * [V]", "$1 $2 a $3");
        var after = Members(path);
        Assert.Equal(before.Inputs, after.Inputs.Take(2));
        Assert.Equal(before.Outputs, after.Outputs.Take(3));
        Assert.Equal(3, after.Inputs.Count);
        Assert.Equal(4, after.Outputs.Count);
    }

    [Fact]
    public void AKindChangeAtOnePositionRecreatesOnlyThatObject()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] [V] *", "$1 $2 $3 a");
        var before = Members(path);
        Import(path, "two", "[C] # *", null);
        var after = Members(path);
        Assert.Equal(before.Inputs[0], after.Inputs[0]);
        Assert.NotEqual(before.Inputs[1], after.Inputs[1]);
        Assert.Equal(before.Inputs[2], after.Inputs[2]);
        Assert.Equal(before.Outputs, after.Outputs);
        Assert.Equal(after.Inputs[1], ContentOf(path, after.Outputs[1]));
        Import(path, "three", null, "$1 $2 $3 <a i>");
        var last = Members(path);
        Assert.Equal(after.Outputs, last.Outputs);
    }

    [Fact]
    public void AnOutputKindChangeRecreatesOnlyThatMapping()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] *", "$1 a $2");
        var before = Members(path);
        Import(path, "two", "[C] *", "$1 [V] $2");
        var after = Members(path);
        Assert.Equal(before.Inputs, after.Inputs);
        Assert.Equal(before.Outputs[0], after.Outputs[0]);
        Assert.NotEqual(before.Outputs[1], after.Outputs[1]);
        Assert.Equal(before.Outputs[2], after.Outputs[2]);
    }

    [Fact]
    public void ACopyFollowsTheKeptContextWhenOnlyTheInputChanges()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] [V] *", "$1 $2 $3 a");
        var before = Members(path);
        Import(path, "two", "[C] + *", null);
        var after = Members(path);
        Assert.Equal(before.Outputs, after.Outputs);
        Assert.Equal(before.Inputs[0], after.Inputs[0]);
        for (var i = 0; i < 3; i++)
            Assert.Equal(after.Inputs[i], ContentOf(path, after.Outputs[i]));
    }

    [Fact]
    public void ASequenceKeepsItsGuidAndItsSameKindMembers()
    {
        var (path, _) = Start();
        Import(path, "one", "<[C] [V] [C]> *", "$1 $2 a");
        var before = Members(path);
        List<Guid> Parts()
        {
            using var cache = new FwDataProjectLoader().LoadCache(path);
            return ((IPhSequenceContext)cache.ServiceLocator.GetObject(before.Inputs[0])).MembersRS.Select(member => member.Guid).ToList();
        }
        var members = Parts();
        Import(path, "two", "<[V] a> *", null);
        var after = Members(path);
        var changed = Parts();
        Assert.Equal(before.Inputs, after.Inputs);
        Assert.Equal(2, changed.Count);
        Assert.Equal(members[0], changed[0]);
        Assert.NotEqual(members[1], changed[1]);
        using var cache = new FwDataProjectLoader().LoadCache(path);
        Assert.Equal(cache.LangProject.PhonologicalDataOA.ContextsOS.Count,
            cache.LangProject.PhonologicalDataOA.ContextsOS.Count(context => context.ReferringObjects.Any()));
        Assert.Equal(2, cache.LangProject.PhonologicalDataOA.ContextsOS.Count);
    }

    [Fact]
    public void AnInsertedGroupKeepsItsMappingAndItsLeadingUnits()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] *", "$1 <a i> $2");
        var before = Members(path);
        Import(path, "two", "[C] *", "$1 <a p u> $2");
        Assert.Equal(before.Outputs, Members(path).Outputs);
        Import(path, "three", "[C] *", "$1 a $2");
        Assert.Equal(before.Outputs, Members(path).Outputs);
    }

    [Fact]
    public void AnUnchangedFormulaEmitsNoOperations()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] <[C] [V]> *", "$1~[voi:+] $2 $3 <a i> [V]");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        Assert.Empty(Lower(cache, "[C] <[C] [V]> *", "$1~[voi:+] $2 $3 <a i> [V]").Operations);
    }

    [Fact]
    public void AnEditedModifyKeepsItsClassWhenOnlyTheColumnMoves()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] [V] *", "$1~[voi:+] $2 $3");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var lowering = Lower(cache, null, "$2~[voi:+] $1 $3");
        Assert.DoesNotContain(lowering.Operations, operation => operation.Kind.Contains("phPhonData/createNaturalClasses", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lowering.Operations, operation => operation.Kind == "grammar/moModifyFromInput/setContent");
    }

    [Fact]
    public void AReplacedModificationClassIsNeverDeleted()
    {
        var (path, _) = Start();
        Import(path, "one", "[C] *", "$1~[cons:+] $2");
        Import(path, "two", "[C] *", "$1~[son:-] $2");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        Assert.Equal(2, cache.LangProject.PhonologicalDataOA.NaturalClassesOS.OfType<IPhNCFeatures>().Count(c =>
            c.Name.BestAnalysisAlternative.Text.StartsWith("Created automatically for rule", StringComparison.Ordinal)));
    }

    // ---- refusals

    [Theory]
    [InlineData("<a", null, RuleNotation.Syntax)]
    [InlineData("z *", null, RuleNotation.UnknownPhoneme)]
    [InlineData("[Zed] *", null, RuleNotation.UnknownNaturalClass)]
    [InlineData(null, "$3", RuleNotation.IndexOutOfRange)]
    [InlineData("[voi:α] *", null, RuleNotation.ContextNotAllowed)]
    [InlineData("[C]{1,3} *", null, RuleNotation.ContextNotAllowed)]
    [InlineData(null, "$1 *", RuleNotation.ContextNotAllowed)]
    [InlineData(null, "<a a>", RefusalCodes.RuleNotationUnsupportedClass)]
    [InlineData("[cons:+ voi:+x] *", null, "fs-notation.unknown-value")]
    public void ABadTextIsRefusedByCodeAndStagesNothing(string? input, string? output, string code)
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        Refused(cache, input, output, code);
    }

    [Fact]
    public void ATextPastTheLimitIsRefused()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        Refused(cache, string.Join(" ", Enumerable.Repeat("*", 17)), "$1", RefusalCodes.RuleNotationLimitExceeded);
        Refused(cache, null, string.Join(" ", Enumerable.Repeat("$1", 257)), RefusalCodes.RuleNotationLimitExceeded);
    }

    [Fact]
    public void ACodeTwoPhonemesShareIsRefusedAsAmbiguous()
    {
        using var cache = pristine.NewScratch();
        Build(cache, ambiguous: true);
        new FwDataProjectLoader().Save(cache);
        Refused(cache, "p *", null, RuleNotation.AmbiguousLabel);
    }

    [Fact]
    public void AnInputOnlyChangeThatWouldLeaveAnOutputPointingAtARemovedColumnIsRefused()
    {
        var (path, _) = Start();
        Import(path, "setup", "[C] *", "$1 $2 a");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        Refused(cache, "*", null, RefusalCodes.RuleNotationDanglingIndex);
    }

    [Fact]
    public void AnEditAgainstAChangedProjectIsStale()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        var plan = PlanEdit(cache, process => process.Set("output", new RecordScalar("a $1")), out var schema);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var process = cache.LangProject.LexDbOA.Entries.Single(e => e.Guid == EntryId).AlternateFormsOS.OfType<IMoAffixProcess>().Single();
            process.OutputOS.Clear();
            var copy = cache.ServiceLocator.GetInstance<IMoCopyFromInputFactory>().Create();
            process.OutputOS.Add(copy);
            copy.ContentRA = process.InputOS[0];
            var insert = cache.ServiceLocator.GetInstance<IMoInsertPhonesFactory>().Create();
            process.OutputOS.Add(insert);
            insert.ContentRS.Add(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.First(phoneme => phoneme.CodesOS[0].Representation.get_String(cache.DefaultVernWs).Text == "i"));
        });
        var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.Empty(lowering.Operations);
        Assert.Equal(RefusalCodes.RecordImportValueStale, Assert.Single(lowering.Refusals).Code);
    }

    [Fact]
    public void AnEmptyFormulaIsRefused()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        var plan = PlanEdit(cache, process => process.Fields.RemoveAll(field => field.Key == "input"), out var schema);
        var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.Empty(lowering.Operations);
        Assert.Equal(RuleNotation.Syntax, Assert.Single(lowering.Refusals).Code);
    }

    [Fact]
    public void ARefusalOfOneFieldRefusesItsSiblingToo()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        var lowering = Lower(cache, "[C] *", "$9");
        Assert.Empty(lowering.Operations);
        Assert.Equal(2, lowering.Refusals.Count);
        Assert.Equal(2, lowering.RefusedChanges!.Count);
    }

    private static void AddSegmentContext(LcmCache cache)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var process = cache.LangProject.LexDbOA.Entries.Single(e => e.Guid == EntryId).AlternateFormsOS.OfType<IMoAffixProcess>().Single();
            var segment = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
            process.InputOS.Insert(0, segment);
            segment.FeatureStructureRA = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.First();
        });
    }

    [Fact]
    public void AContextDescriptionStaysWritableThroughTheInputParts()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        AddSegmentContext(cache);
        var plan = PlanEdit(cache, process => ((RecordObject)((RecordList)process["input-parts"]!).Items[0])
            .Set("description", new RecordScalar("lead")), out var schema);
        Assert.Empty(plan.ChangeSet.Refusals);
        var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.Empty(lowering.Refusals);
        Assert.Contains(lowering.Operations, operation => operation.Kind == "text/setParagraphs");
    }

    [Fact]
    public void AMemberCannotBeRemovedThroughThePartsKey()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        var plan = PlanEdit(cache, process => ((RecordList)process["input-parts"]!).Items.Clear(), out var schema);
        var lowering = plan.ChangeSet.Refusals.Count > 0 ? null : new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.True(plan.ChangeSet.Refusals.Count > 0 || lowering!.Refusals.Count > 0);
    }

    private static void BuildUnrenderable(LcmCache cache)
    {
        Build(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var process = cache.LangProject.LexDbOA.Entries.Single(e => e.Guid == EntryId).AlternateFormsOS.OfType<IMoAffixProcess>().Single();
            process.InputOS.Clear();
            process.OutputOS.Clear();
            process.InputOS.Add(cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create());
            process.OutputOS.Add(cache.ServiceLocator.GetInstance<IMoCopyFromInputFactory>().Create());
        });
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    public void AnUnrenderableFormulaLocksItsKey(string key)
    {
        using var cache = pristine.NewScratch();
        BuildUnrenderable(cache);
        new FwDataProjectLoader().Save(cache);
        var edit = PlanEdit(cache, process => process.Set(key, new RecordScalar("x")), out _);
        Assert.NotEmpty(edit.ChangeSet.Refusals);
    }
}
