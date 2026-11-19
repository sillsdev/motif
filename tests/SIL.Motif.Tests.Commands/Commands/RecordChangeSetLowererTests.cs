using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class RecordChangeSetLowererTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("motif-record-lowerer-").FullName;
    private readonly LcmCache _cache;
    private readonly ProjectRecordSchemaResult _schema;
    private readonly ProjectRecordExporter _exporter;
    private ILexEntry _entry = null!;
    private ILexSense _sense = null!, _second = null!;
    private ICmPossibility _publication = null!;

    public RecordChangeSetLowererTests()
    {
        _cache = NewLangProjFixture.CreateCache(_root);
        _schema = ProjectRecordSchema.Generate(_cache, ProjectRecordSchema.LexiconEntryOverlay);
        _exporter = new ProjectRecordExporter(_cache, _schema);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            _entry = services.GetInstance<ILexEntryFactory>().Create();
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            _entry.LexemeFormOA = form;
            form.Form.set_String(_cache.DefaultVernWs, TsStringUtils.MakeString("zzword", _cache.DefaultVernWs));
            _sense = services.GetInstance<ILexSenseFactory>().Create();
            _entry.SensesOS.Add(_sense);
            _sense.Gloss.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("zzgloss", _cache.DefaultAnalWs));
            _sense.Definition.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("zzdef", _cache.DefaultAnalWs));
            _second = services.GetInstance<ILexSenseFactory>().Create();
            _entry.SensesOS.Add(_second);
            _second.Gloss.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("zzother", _cache.DefaultAnalWs));
            _publication = services.GetInstance<ICmPossibilityFactory>().Create();
            _cache.LangProject.LexDbOA.PublicationTypesOA.PossibilitiesOS.Add(_publication);
            _publication.Name.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("zzpub", _cache.DefaultAnalWs));
            var held = services.GetInstance<ICmPossibilityFactory>().Create();
            _cache.LangProject.LexDbOA.PublicationTypesOA.PossibilitiesOS.Add(held);
            held.Name.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("zzheld", _cache.DefaultAnalWs));
            _entry.DoNotPublishInRC.Add(held);
        });
    }

    public void Dispose()
    {
        _cache.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private ProjectRecordPlan PlanEdit(Action<RecordDocument> edit, WritableFields? writable = null)
    {
        var export = _exporter.Export(_entry.Guid);
        var read = RecordFileReader.Read(export.Text!, _schema.Schema, _exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        edit(read.Document!);
        var text = RecordFileWriter.Write(read.Document!, _schema.Schema);
        return new ProjectRecordImporter(_exporter).Plan(text, writable);
    }

    private static RecordObject SenseAt(RecordDocument d, int index) =>
        (RecordObject)((RecordList)d.Data["senses"]!).Items[index];

    private RecordLowering Lower(ProjectRecordPlan plan) =>
        new RecordChangeSetLowerer(_schema, new RecordWritableFields(_schema)).Lower(_cache, plan);

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        operations.Select(op => op.Kind[..op.Kind.IndexOf('/')]).Distinct().ToDictionary(group => group, _ => "1.0"),
        CanonicalId.FromGuid(new Guid("11111111-2222-3333-4444-555555555555")), null, operations);

    private SIL.Motif.Model.DryRun.DryRun DryRun(RecordLowering lowering)
    {
        Assert.Empty(lowering.Refusals);
        Assert.NotEmpty(lowering.Operations);
        return ScratchDryRun.Of(_cache, Proposal(lowering.Operations));
    }

    [Fact]
    public void CatalogWritableFieldsAgreesWithPhaseThreesResolution()
    {
        var catalog = new CatalogWritableFields();
        var resolver = new RecordWritableFields(_schema);
        foreach (var type in _schema.Schema.ObjectTypes)
            foreach (var field in type.Fields)
            {
                var binding = _schema.FieldBinding(type.Name, field.Name);
                if (binding is null) continue;
                var (declaring, name, _) = binding.Value;
                var writable = new[] { RecordWriteVerb.Set, RecordWriteVerb.Clear, RecordWriteVerb.SetAlternative,
                        RecordWriteVerb.ClearAlternative, RecordWriteVerb.SetPlainRich, RecordWriteVerb.SetRich,
                        RecordWriteVerb.ClearRich, RecordWriteVerb.AddReference, RecordWriteVerb.RemoveReference,
                        RecordWriteVerb.MoveReference, RecordWriteVerb.MoveObject }
                    .Any(verb => resolver.Resolve(type.Name, field.Name, verb) is WritableFieldResolution.Covered);
                // The catalog offers no more than phase 3 covers, for the types phase 3 reaches; the agreement test holds the rest.
                var reached = resolver.Entries.Any(e => e.Key.Class == type.Name && e.Key.Field == field.Name);
                if (reached && catalog.CanWrite(declaring, name) && !writable)
                    Assert.True(field.Kind is RecordFieldKind.Object or RecordFieldKind.Sequence or RecordFieldKind.Collection,
                        $"{type.Name}.{field.Name}");
            }
        Assert.True(catalog.CanWrite("LexSense", "Gloss"));
        Assert.False(catalog.CanWrite("LexEntry", "HomographNumber"));
        Assert.True(catalog.CanCreate("LexEntry", "Senses", "LexSense"));
        Assert.True(catalog.CanDelete("LexSense", "Senses", "LexSense"));
    }

    [Fact]
    public void AMultiWritingSystemSetLowersToItsKindAndReadsBackAsTheDiff()
    {
        var lowering = Lower(PlanEdit(d => ((RecordAlternatives)SenseAt(d, 0)["gloss"]!).Add("en", new RecordScalar("zzchanged"))));
        var operation = Assert.Single(lowering.Operations);
        Assert.Equal("lexical/lexSense/setGloss", operation.Kind);
        var dryRun = DryRun(lowering);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.After.Values.Contains("zzchanged"));
    }

    [Fact]
    public void AFormattedValueLowersToWholeFieldReplacementWithExpectedFromTheProject()
    {
        var lowering = Lower(PlanEdit(d => ((RecordAlternatives)SenseAt(d, 0)["definition"]!)
            .Add("en", new RecordScalar("zzdef [bold]{.v}"))));
        var operation = Assert.Single(lowering.Operations);
        Assert.Equal("text/setRichString", operation.Kind);
        Assert.Contains("expected", operation.After!.Value.GetRawText());
        Assert.NotEmpty(DryRun(lowering).ExpectedEffects);
    }

    [Fact]
    public void AReferenceAddLowersToItsKind()
    {
        var link = "[zzpub](/objects/" + _publication.Guid.ToString("D") + "){.name guid=" + _publication.Guid.ToString("D") + "}";
        var refPlan = PlanEdit(d => ((RecordList)d.Data["do-not-publish-in"]!).Add(new RecordScalar(link)));
        var lowering = Lower(refPlan);
        var operation = Assert.Single(lowering.Operations);
        Assert.Equal(_publication.Guid.ToString("D"), CanonicalId.Parse(operation.After!.Value.GetProperty("member").GetString()!).ToGuid().ToString("D"));
        Assert.NotEmpty(DryRun(lowering).ExpectedEffects);
    }

    [Fact]
    public void AnOwnedSenseCreateAndDeleteLowerAndExecute()
    {
        var created = Lower(PlanEdit(d => ((RecordList)d.Data["senses"]!).Add(
            new RecordObject { Class = "LexSense" }.Set("gloss", new RecordAlternatives().Add("en", new RecordScalar("zznew"))))));
        Assert.Empty(created.Refusals);
        Assert.Contains(created.Operations, op => op.Kind == "lexical/lexEntry/createSenses");
        Assert.NotEmpty(DryRun(created).ExpectedEffects);

        var deleted = Lower(PlanEdit(d => ((RecordList)d.Data["senses"]!).Items.RemoveAt(1)));
        Assert.Contains(deleted.Operations, op => op.Kind == "lexical/lexSense/remove");
        Assert.NotEmpty(DryRun(deleted).ExpectedEffects);
    }

    [Fact]
    public void ARefusedCreateLeavesNoStateForWhatIsAnchoredOnIt()
    {
        var link = "[zzpub](/objects/" + _publication.Guid.ToString("D") + "){.name guid=" + _publication.Guid.ToString("D") + "}";
        var plan = PlanEdit(d =>
        {
            var senses = (RecordList)d.Data["senses"]!;
            // The first new sense carries a value no operation sets on a new object; the second is placed after it.
            senses.Add(new RecordObject { Class = "LexSense" }.Set("semantic-domains", new RecordList().Add(new RecordScalar(link))));
            senses.Add(new RecordObject { Class = "LexSense" }.Set("gloss", new RecordAlternatives().Add("en", new RecordScalar("zzsecond"))));
        });
        var creates = plan.ChangeSet.Changes.Where(c => c.Kind == RecordChangeKind.AddObject).ToArray();
        Assert.Equal(2, creates.Length);
        Assert.Equal(RecordAnchorKind.Added, creates[1].Anchor?.Kind);
        var lowering = Lower(plan);
        Assert.Equal(2, lowering.Refusals.Count);
        Assert.Equal(RefusalCodes.RecordImportShapeUnsupported, lowering.Refusals[0].Code);
        Assert.Equal(RefusalCodes.RecordImportNotExecutable, lowering.Refusals[1].Code);
        Assert.DoesNotContain(lowering.Operations, op => op.Kind.EndsWith("createSenses"));
    }

    [Fact]
    public void LoweringTheSamePlanTwiceGivesOneIntentDigest()
    {
        var plan = PlanEdit(d =>
        {
            ((RecordAlternatives)SenseAt(d, 0)["gloss"]!).Add("en", new RecordScalar("zzchanged"));
            ((RecordList)d.Data["senses"]!).Add(new RecordObject { Class = "LexSense" });
        });
        var first = IntentDigest.Compute(Proposal(Lower(plan).Operations));
        var second = IntentDigest.Compute(Proposal(Lower(plan).Operations));
        Assert.Equal(first, second);
    }

    [Fact]
    public void EveryRefusalNamesARegisteredCodeAndAClassAndField()
    {
        var plan = PlanEdit(d => d.Data["homograph-number"] = RecordScalar.Of(7));
        var lowering = Lower(plan);
        Assert.All(lowering.Refusals, refusal =>
        {
            Assert.Contains(refusal.Code, RefusalCatalog.Specs.Keys);
            Assert.NotEmpty(refusal.Class);
            Assert.NotEmpty(refusal.Field);
        });
        Assert.Equal(plan.ChangeSet.Changes.Count, lowering.Operations.Count + lowering.Refusals.Count);
    }

    [Fact]
    public void AWholeFileRefusalLowersNothing()
    {
        var plan = new ProjectRecordImporter(_exporter).Plan("not a record file");
        Assert.True(plan.ChangeSet.IsRefusedWhole);
        var lowering = Lower(plan);
        Assert.Empty(lowering.Operations);
        Assert.Equal(RefusalCodes.RecordImportPlanRefused, Assert.Single(lowering.Refusals).Code);
    }

    private static RecordChange NewChange(RecordChangeKind kind, Action<RecordChange> set)
    {
        var change = (RecordChange)Activator.CreateInstance(typeof(RecordChange),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, [kind], null)!;
        set(change);
        return change;
    }

    private static void Put(RecordChange change, string property, object? value) =>
        typeof(RecordChange).GetProperty(property)!.GetSetMethod(true)!.Invoke(change, [value]);

    /// <summary>The changes a covered (type, field, verb) pair is made of in a record file, aimed at a made-up object.</summary>
    private static IEnumerable<RecordChange> ChangesFor(RecordObjectType type, RecordField field, RecordWriteVerb verb,
        WritableFields catalog, (string DeclaringClass, string Field, bool IsCustom) binding)
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        RecordChange Make(RecordChangeKind kind, Action<RecordChange>? more = null) => NewChange(kind, change =>
        {
            Put(change, "ObjectGuid", owner);
            Put(change, "ObjectType", type.Name);
            Put(change, "Field", field.Name);
            more?.Invoke(change);
        });
        var ws = field.MultiWritingSystem ? "en" : null;
        switch (verb)
        {
            case RecordWriteVerb.Set:
                yield return Make(RecordChangeKind.Set, change =>
                {
                    Put(change, "New", field.Kind == RecordFieldKind.Boolean ? "true" : field.Kind == RecordFieldKind.Integer ? "1" : "x");
                    Put(change, "NewTarget", other);
                });
                break;
            case RecordWriteVerb.Clear: yield return Make(RecordChangeKind.Clear); break;
            case RecordWriteVerb.SetAlternative:
                yield return Make(RecordChangeKind.SetAlternative, c => { Put(c, "WritingSystem", "en"); Put(c, "New", "x"); });
                break;
            case RecordWriteVerb.ClearAlternative:
                yield return Make(RecordChangeKind.ClearAlternative, c => Put(c, "WritingSystem", "en"));
                break;
            case RecordWriteVerb.SetPlainRich or RecordWriteVerb.SetRich:
                yield return Make(RecordChangeKind.SetRich, c => { Put(c, "WritingSystem", ws); Put(c, "New", "x"); });
                break;
            case RecordWriteVerb.ClearRich:
                yield return Make(RecordChangeKind.ClearRich, c => Put(c, "WritingSystem", ws));
                break;
            case RecordWriteVerb.AddObject:
                foreach (var child in (field.Item ?? field).ObjectTypes.Where(t => catalog.CanCreate(binding.DeclaringClass, binding.Field, t.Name)))
                    yield return Make(RecordChangeKind.AddObject, c => Put(c, "Subtree", new RecordObject { Class = child.Name }));
                break;
            case RecordWriteVerb.MoveObject:
                yield return Make(RecordChangeKind.MoveObject, c => Put(c, "ItemGuid", other));
                break;
            case RecordWriteVerb.AddReference or RecordWriteVerb.InsertReference:
                yield return Make(RecordChangeKind.AddReference, c => Put(c, "NewTarget", other));
                break;
            case RecordWriteVerb.RemoveReference:
                yield return Make(RecordChangeKind.RemoveReference, c => Put(c, "OldTarget", other));
                break;
            case RecordWriteVerb.MoveReference:
                yield return Make(RecordChangeKind.MoveReference, c => Put(c, "NewTarget", other));
                break;
        }
    }

    [Fact]
    public void EveryCoveredPairIsLoweredOrRefusedByARegisteredCode_AndEveryOfferedPairIsLowered()
    {
        var catalog = new CatalogWritableFields();
        var resolver = new RecordWritableFields(_schema);
        var lowerer = new RecordChangeSetLowerer(_schema, resolver);
        var failures = new List<string>();
        var lowered = 0;
        var refused = 0;
        foreach (var ((typeName, fieldName, verb), resolution) in resolver.Entries)
        {
            if (resolution is not WritableFieldResolution.Covered)
                continue;
            var type = _schema.Schema.ObjectType(typeName)!;
            var field = type.Field(fieldName)!;
            var binding = _schema.FieldBinding(typeName, fieldName)!.Value;
            var handlerVerb = verb == RecordWriteVerb.InsertReference ? RecordWriteVerb.AddReference : verb;
            var declared = OperationHandlerRegistry.RegisteredKinds.SelectMany(kind =>
                OperationHandlerRegistry.FieldWrites(kind).Where(write => write.DeclaringClass == binding.DeclaringClass &&
                    write.Field == binding.Field && write.Verb == handlerVerb).Select(write => (Kind: kind, Write: write))).ToArray();
            var offered = declared.Any(item => RecordLoweringRules.Lowers(item.Kind, item.Write));
            var subject = $"{typeName}.{fieldName} {verb}";
            var catalogOffers = verb switch
            {
                RecordWriteVerb.AddObject => (field.Item ?? field).ObjectTypes.Any(child =>
                    catalog.CanCreate(binding.DeclaringClass, binding.Field, child.Name)),
                RecordWriteVerb.RemoveObject => (field.Item ?? field).ObjectTypes.Any(child =>
                    catalog.CanDelete(binding.DeclaringClass, binding.Field, child.Name)),
                _ => catalog.CanWrite(binding.DeclaringClass, binding.Field),
            };
            if (offered && !catalogOffers)
                failures.Add($"{subject}: the lowerer authors it but the catalog does not offer it.");
            if (verb == RecordWriteVerb.RemoveObject)
            {
                // A delete is built from the project's object, so only the sense delete (the one kind that is authored) is run here.
                if (offered && fieldName != "senses")
                    failures.Add($"{subject}: offered, but only a sense delete is authored.");
                continue;
            }
            foreach (var change in ChangesFor(type, field, verb, catalog, binding))
            {
                var result = lowerer.LowerChanges(_cache, "agreement", [change]);
                var unaccounted = result.Operations.Count + result.Refusals.Count != 1;
                if (unaccounted)
                    failures.Add($"{subject}: neither an operation nor a refusal.");
                foreach (var refusal in result.Refusals)
                {
                    refused++;
                    if (!RefusalCatalog.Specs.ContainsKey(refusal.Code))
                        failures.Add($"{subject}: refused by unregistered code {refusal.Code}.");
                    // Refusals about the project's state (the made-up object is not in it) and the anchor a made-up move lacks
                    // are about the input; a coverage or shape refusal of an offered pair is the bug this test is for.
                    var aboutInput = refusal.Code == RefusalCodes.RecordImportNotExecutable || refusal.Reason.Contains("MissingPlacement") ||
                        refusal.Reason.Contains("MissingChildClass");
                    if (offered && !aboutInput)
                        failures.Add($"{subject}: offered by the catalog but refused: {refusal.Code} {refusal.Reason}");
                }
                foreach (var operation in result.Operations)
                {
                    lowered++;
                    var accepts = OperationHandlerRegistry.FieldWrites(operation.Kind).Any(write =>
                        write.DeclaringClass == binding.DeclaringClass && write.Field == binding.Field);
                    if (!accepts)
                        failures.Add($"{subject}: lowered to {operation.Kind}, which does not declare the field.");
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Distinct()));
        Assert.True(lowered > 50, $"only {lowered} lowered, {refused} refused");
    }

    [Fact]
    public void ABooleanScalarSetLowersAndExecutes()
    {
        var lowering = Lower(PlanEdit(d => d.Data["do-not-use-for-parsing"] = RecordScalar.Of(true)));
        var operation = Assert.Single(lowering.Operations);
        Assert.EndsWith("DoNotUseForParsing", operation.Kind, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(DryRun(lowering).ExpectedEffects);
    }
}
