using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Execution;
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
        AddCustomFields(_cache);
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
            var sub = services.GetInstance<ILexSenseFactory>().Create();
            _sense.SensesOS.Add(sub);
            sub.Gloss.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("zzsub", _cache.DefaultAnalWs));
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

    /// <summary>One custom field of each type FieldWorks can create, one it can only hold, and one the record schema omits.</summary>
    internal static readonly (string Name, CellarPropertyType Type)[] CustomFields =
    [
        ("ZedInteger", CellarPropertyType.Integer), ("ZedMultiUnicode", CellarPropertyType.MultiUnicode),
        ("ZedMultiString", CellarPropertyType.MultiString), ("ZedString", CellarPropertyType.String),
        ("ZedReference", CellarPropertyType.ReferenceAtomic), ("ZedReferences", CellarPropertyType.ReferenceCollection),
        ("ZedBoolean", CellarPropertyType.Boolean), ("ZedDate", CellarPropertyType.GenDate),
        ("ZedText", CellarPropertyType.OwningAtomic),
    ];

    private static void AddCustomFields(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var possibility = metadata.GetClassId("CmPossibility");
        var list = default(Guid);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => list = cache.LangProject.LexDbOA.PublicationTypesOA.Guid);
        foreach (var (name, type) in CustomFields)
        {
            var reference = type is CellarPropertyType.ReferenceAtomic or CellarPropertyType.ReferenceCollection;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                metadata.AddCustomField("LexEntry", name, type, reference ? possibility : type == CellarPropertyType.OwningAtomic ? metadata.GetClassId("StText") : 0, string.Empty,
                    name == "ZedString" ? WritingSystemServices.kwsAnal : 0, reference ? list : Guid.Empty));
        }
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

    /// <summary>How the Runner declares the field: a custom one by its type.</summary>
    private (string Class, string Field) DeclaredAs((string DeclaringClass, string Field, bool IsCustom) binding) =>
        !binding.IsCustom ? (binding.DeclaringClass, binding.Field)
            : (CustomFieldOperationKinds.DeclaringClass, (CellarPropertyType)_cache.MetaDataCacheAccessor.GetFieldType(
                _cache.MetaDataCacheAccessor.GetFieldId(binding.DeclaringClass, binding.Field, true)) switch
            {
                CellarPropertyType.ReferenceAtomic => "ReferenceAtomic",
                CellarPropertyType.OwningAtomic => "StText",
                var other => other.ToString(),
            });

    private IEnumerable<(string Kind, FieldWriteCapability Write)> Declarations(
        (string DeclaringClass, string Field, bool IsCustom) binding, RecordWriteVerb verb)
    {
        var (declaring, field) = DeclaredAs(binding);
        return OperationHandlerRegistry.RegisteredKinds.SelectMany(kind => OperationHandlerRegistry.FieldWrites(kind)
            .Where(write => write.DeclaringClass == declaring && write.Field == field && write.Verb == verb)
            .Select(write => (kind, write)));
    }

    private static readonly RecordWriteVerb[] WriteVerbs =
    [
        RecordWriteVerb.Set, RecordWriteVerb.Clear, RecordWriteVerb.SetAlternative, RecordWriteVerb.ClearAlternative,
        RecordWriteVerb.SetPlainRich, RecordWriteVerb.SetRich, RecordWriteVerb.ClearRich, RecordWriteVerb.MoveObject,
        RecordWriteVerb.AddReference, RecordWriteVerb.InsertReference, RecordWriteVerb.RemoveReference, RecordWriteVerb.MoveReference,
    ];

    [Fact]
    public void CatalogWritableFieldsAgreesExactlyWithPhaseThreesCoverage()
    {
        var catalog = new CatalogWritableFields(_schema);
        var resolver = new RecordWritableFields(_schema);
        var failures = new List<string>();
        var familiesSeen = new HashSet<string>();
        // Every (declaring class, field) phase 3 covers on some verb, gathered across the schema types that bind to it.
        bool Structure(string className, string field) => RecordWritableFields.IsFeatureStructure(_schema.Schema.ObjectType(className)!.Field(field)!) ||
            RecordWritableFields.IsRuleFormula(_schema.Schema.ObjectType(className)!.Field(field)!);
        var covered = resolver.Entries.Where(entry => entry.Value is WritableFieldResolution.Covered { Owner: null } && !Structure(entry.Key.Class, entry.Key.Field))
            .Select(entry => (Entry: entry.Key, Binding: _schema.FieldBinding(entry.Key.Class, entry.Key.Field)!.Value))
            .GroupBy(item => (item.Binding.DeclaringClass, item.Binding.Field)).ToArray();
        foreach (var group in covered)
        {
            var (declaring, name) = group.Key;
            var offeredWrite = false;
            foreach (var item in group)
            {
                var verb = item.Entry.Verb == RecordWriteVerb.InsertReference ? RecordWriteVerb.AddReference : item.Entry.Verb;
                var declared = Declarations(item.Binding, verb).ToArray();
                var subject = $"{item.Entry.Class}.{item.Entry.Field} {item.Entry.Verb}";
                if (declared.Length == 0)
                {
                    failures.Add($"{subject}: phase 3 covers it but no kind declares it.");
                    continue;
                }
                var lowerable = declared.Any(d => RecordLoweringRules.Lowers(d.Kind, d.Write));
                if (verb == RecordWriteVerb.AddObject || verb == RecordWriteVerb.RemoveObject)
                {
                    var children = (_schema.Schema.ObjectType(item.Entry.Class)!.Field(item.Entry.Field)!.Item ??
                        _schema.Schema.ObjectType(item.Entry.Class)!.Field(item.Entry.Field)!).ObjectTypes.Select(t => t.Name);
                    foreach (var child in children)
                    {
                        var accepting = declared.Where(d => RecordLoweringRules.Lowers(d.Kind, d.Write) &&
                            (d.Write.ChildClasses.Count == 0 || d.Write.ChildClasses.Contains(child))).Any();
                        var offered = verb == RecordWriteVerb.AddObject ? catalog.CanCreate(declaring, name, child) : catalog.CanDelete(declaring, name, child);
                        if (offered != accepting)
                            failures.Add($"{subject} / {child}: the adapter says {offered}, the lowerable declarations say {accepting}.");
                    }
                }
                else if (lowerable)
                    offeredWrite = true;
                if (!lowerable)
                {
                    var family = declared.Select(d => RecordLoweringRules.NotLowered(d.Kind, d.Write)).FirstOrDefault(f => f is not null);
                    if (family is null)
                        failures.Add($"{subject}: not offered, and no not-lowered reason explains it.");
                    else
                        familiesSeen.Add(family);
                }
            }
            if (group.Any(item => WriteVerbs.Contains(item.Entry.Verb)) && catalog.CanWrite(declaring, name) != offeredWrite)
                failures.Add($"{declaring}.{name}: CanWrite is {catalog.CanWrite(declaring, name)}, the covered lowerable verbs say {offeredWrite}.");
        }
        // The other way: whatever the adapter offers phase 3 covers and the lowerer lowers.
        foreach (var type in _schema.Schema.ObjectTypes)
            foreach (var field in type.Fields)
            {
                if (RecordWritableFields.IsFeatureStructure(field) || RecordWritableFields.IsRuleFormula(field) ||
                    RecordWritableFields.IsRuleParts(type, field, _schema) ||
                    _schema.FieldBinding(type.Name, field.Name) is not { } binding ||
                    !resolver.Entries.Any(e => e.Key.Class == type.Name && e.Key.Field == field.Name))
                    continue;
                if (catalog.CanWrite(binding.DeclaringClass, binding.Field) &&
                    !WriteVerbs.Any(verb => resolver.Entries.TryGetValue((type.Name, field.Name, verb), out var r) &&
                        r is WritableFieldResolution.Covered && Declarations(binding, verb == RecordWriteVerb.InsertReference ? RecordWriteVerb.AddReference : verb)
                            .Any(d => RecordLoweringRules.Lowers(d.Kind, d.Write))))
                    failures.Add($"{type.Name}.{field.Name}: the adapter offers a write phase 3 does not cover and the lowerer does not lower.");
            }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Distinct()));
        Assert.All(familiesSeen, family => Assert.Contains(family, RecordLoweringRules.NotLoweredReasons.Keys));
        Assert.True(catalog.CanWrite("MoStemMsa", "MsFeatures"));
        Assert.True(catalog.CanWrite("MoInflAffMsa", "InflFeats"));
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

    /// <summary>A publication type the entry does not hold, so adding it is a real change to its list.</summary>
    private ICmPossibility UnheldPublication()
    {
        var fresh = default(ICmPossibility)!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            fresh = _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            _cache.LangProject.LexDbOA.PublicationTypesOA.PossibilitiesOS.Add(fresh);
            _entry.DoNotPublishInRC.Remove(fresh);
        });
        return fresh;
    }

    private static string Link(ICmPossibility target) =>
        "[zzpub](/objects/" + target.Guid.ToString("D") + "){.name guid=" + target.Guid.ToString("D") + "}";

    [Fact]
    public void AReferenceAddLowersToItsKind()
    {
        var target = UnheldPublication();
        var lowering = Lower(PlanEdit(d => ((RecordList)d.Data["do-not-publish-in"]!).Add(new RecordScalar(Link(target)))));
        var operation = Assert.Single(lowering.Operations);
        Assert.Equal(target.Guid.ToString("D"), CanonicalId.Parse(operation.After!.Value.GetProperty("member").GetString()!).ToGuid().ToString("D"));
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
    public void AReferenceChangeAgainstAListThatMovedOnIsRefusedAsStale()
    {
        var target = UnheldPublication();
        var addPlan = PlanEdit(d => ((RecordList)d.Data["do-not-publish-in"]!).Add(new RecordScalar(Link(target))));
        var removePlan = PlanEdit(d => ((RecordList)d.Data["do-not-publish-in"]!).Items.Clear());
        Assert.Empty(Lower(addPlan).Refusals);
        Assert.Empty(Lower(removePlan).Refusals);

        // The project changes after the plan was made: the added member arrives, the removed one leaves.
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _entry.DoNotPublishInRC.Clear();
            _entry.DoNotPublishInRC.Add(target);
        });
        var added = Lower(addPlan);
        Assert.Empty(added.Operations);
        Assert.Equal(RefusalCodes.RecordImportValueStale, Assert.Single(added.Refusals).Code);
        var removed = Lower(removePlan);
        Assert.Empty(removed.Operations);
        Assert.NotEmpty(removed.Refusals);
        Assert.All(removed.Refusals, refusal => Assert.Equal(RefusalCodes.RecordImportValueStale, refusal.Code));
    }

    [Fact]
    public void ARefusedCreateLeavesNoStateForWhatIsAnchoredOnIt()
    {
        RecordChange Create(RecordObject subtree, RecordAnchor anchor) => NewChange(RecordChangeKind.AddObject, change =>
        {
            Put(change, "ObjectGuid", _entry.Guid);
            Put(change, "ObjectType", "LexEntry");
            Put(change, "Field", "senses");
            Put(change, "Subtree", subtree);
            Put(change, "Anchor", anchor);
        });
        var addedBy = (RecordAnchor)typeof(RecordAnchor).GetMethod("AddedBy",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [0])!;
        // The first new sense carries a value no operation sets on a new object; the second is placed after it.
        var refused = new RecordObject { Class = "LexSense" }.Set("semantic-domains", new RecordList().Add(new RecordScalar(Link(_publication))));
        var anchored = new RecordObject { Class = "LexSense" }.Set("gloss", new RecordAlternatives().Add("en", new RecordScalar("zzsecond")));
        var lowering = new RecordChangeSetLowerer(_schema, new RecordWritableFields(_schema))
            .LowerChanges(_cache, "refused", [Create(refused, StartAnchor), Create(anchored, addedBy)]);
        Assert.Equal(2, lowering.Refusals.Count);
        Assert.Equal(RefusalCodes.RecordImportShapeUnsupported, lowering.Refusals[0].Code);
        Assert.Equal(RefusalCodes.RecordImportNotExecutable, lowering.Refusals[1].Code);
        Assert.Empty(lowering.Operations);
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

    private static readonly RecordAnchor StartAnchor = (RecordAnchor)typeof(RecordAnchor).GetField("Start",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;

    /// <summary>An object of the class (or a subclass) the project holds, or null.</summary>
    private Guid? RealObject(string className)
    {
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var id = metadata.GetClassId(className);
        var classes = metadata.GetAllSubclasses(id).Append(id).ToHashSet();
        return _cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
            .Where(item => classes.Contains(item.ClassID)).Select(item => (Guid?)item.Guid).FirstOrDefault();
    }

    private List<Guid> Members(Guid owner, (string DeclaringClass, string Field, bool IsCustom) binding)
    {
        var repository = _cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var instance = repository.GetObject(owner);
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var declaring = metadata.GetClassId(binding.DeclaringClass);
        if (instance.ClassID != declaring && !metadata.GetAllSubclasses(declaring).Contains(instance.ClassID))
            return [];
        var flid = metadata.GetFieldId(binding.DeclaringClass, binding.Field, true);
        var data = _cache.DomainDataByFlid;
        if (!IsVector((CellarPropertyType)_cache.MetaDataCacheAccessor.GetFieldType(flid)))
            return data.get_ObjectProp(instance.Hvo, flid) is var held and not 0 ? [repository.GetObject(held).Guid] : [];
        return Enumerable.Range(0, data.get_VecSize(instance.Hvo, flid))
            .Select(position => repository.GetObject(data.get_VecItem(instance.Hvo, flid, position)).Guid).ToList();
    }

    /// <summary>The changes of a covered pair aimed at a real object, or why none can be made.</summary>
    private (List<RecordChange> Changes, string? Gap) ChangesFor(RecordObjectType type, RecordField field, RecordWriteVerb verb,
        WritableFields catalog, (string DeclaringClass, string Field, bool IsCustom) binding)
    {
        var changes = new List<RecordChange>();
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var declaring = metadata.GetClassId(binding.DeclaringClass);
        var ownerClass = metadata.GetAllSubclasses(metadata.GetClassId(type.Name)).Contains(declaring) ? binding.DeclaringClass : type.Name;
        var (made, ownerWhy) = Instance(ownerClass);
        if (made is not { } owner)
            return (changes, ownerWhy!);
        var members = field.Kind is RecordFieldKind.Sequence or RecordFieldKind.Collection or RecordFieldKind.Object or RecordFieldKind.Reference
            ? Members(owner, binding) : [];
        if (members.Count == 0 && verb is RecordWriteVerb.MoveObject or RecordWriteVerb.MoveReference or RecordWriteVerb.RemoveReference or RecordWriteVerb.RemoveObject)
        {
            if (Seed(owner, type.Name, field, binding) is { } seedWhy)
                return (changes, seedWhy);
            members = Members(owner, binding);
            if (members.Count == 0)
            {
                _factoryFailures.Add($"seed {type.Name}.{field.Name} {verb}: member not in field");
                return (changes, CreationRefused);
            }
        }
        // A move needs a second member to place against, whatever order the fixture was built in.
        for (var tries = 0; verb is RecordWriteVerb.MoveObject && members.Count < 2 && tries < 2; tries++)
        {
            if (Seed(owner, type.Name, field, binding) is not null)
                break;
            members = Members(owner, binding);
        }
        var fresh = Guid.NewGuid();
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
                changes.Add(Make(RecordChangeKind.Set, change =>
                {
                    Put(change, "New", field.Kind == RecordFieldKind.Boolean ? "true" : field.Kind == RecordFieldKind.Integer ? "1" : "x");
                    Put(change, "NewTarget", field.Kind == RecordFieldKind.Reference ? RealTarget(binding) ?? fresh : fresh);
                }));
                break;
            case RecordWriteVerb.Clear: changes.Add(Make(RecordChangeKind.Clear)); break;
            case RecordWriteVerb.SetAlternative:
                changes.Add(Make(RecordChangeKind.SetAlternative, c => { Put(c, "WritingSystem", "en"); Put(c, "New", "x"); }));
                break;
            case RecordWriteVerb.ClearAlternative:
                changes.Add(Make(RecordChangeKind.ClearAlternative, c => Put(c, "WritingSystem", "en")));
                break;
            case RecordWriteVerb.SetPlainRich or RecordWriteVerb.SetRich:
                changes.Add(Make(RecordChangeKind.SetRich, c => { Put(c, "WritingSystem", ws); Put(c, "New", "x"); }));
                break;
            case RecordWriteVerb.ClearRich:
                changes.Add(Make(RecordChangeKind.ClearRich, c => Put(c, "WritingSystem", ws)));
                break;
            case RecordWriteVerb.AddObject:
                foreach (var child in (field.Item ?? field).ObjectTypes.Where(t => catalog.CanCreate(binding.DeclaringClass, binding.Field, t.Name)))
                    changes.Add(Make(RecordChangeKind.AddObject, c =>
                    {
                        Put(c, "Subtree", new RecordObject { Class = child.Name });
                        if (members.Count > 0 && field.Kind == RecordFieldKind.Sequence) Put(c, "Anchor", StartAnchor);
                    }));
                break;
            case RecordWriteVerb.MoveObject or RecordWriteVerb.MoveReference:
                                changes.Add(Make(verb == RecordWriteVerb.MoveObject ? RecordChangeKind.MoveObject : RecordChangeKind.MoveReference, c =>
                {
                    Put(c, verb == RecordWriteVerb.MoveObject ? "ItemGuid" : "NewTarget", members[0]);
                    Put(c, "Anchor", StartAnchor);
                }));
                break;
            case RecordWriteVerb.AddReference or RecordWriteVerb.InsertReference:
                changes.Add(Make(RecordChangeKind.AddReference, c =>
                {
                    Put(c, "NewTarget", fresh);
                    if (members.Count > 0 && verb == RecordWriteVerb.InsertReference) Put(c, "Anchor", StartAnchor);
                }));
                break;
            case RecordWriteVerb.RemoveReference:
                                changes.Add(Make(RecordChangeKind.RemoveReference, c => Put(c, "OldTarget", members[0])));
                break;
            case RecordWriteVerb.RemoveObject:
                // The one authored delete is the sense's, so it is aimed at a sense the project really holds.
                                changes.Add(Make(RecordChangeKind.RemoveObject, c => Put(c, "ItemGuid", members[^1])));
                break;
        }
        return (changes, null);
    }

    // Why liblcm cannot make an owned class generically: rules about classes, not pairs.
    /// <summary>Remove and Move on references whose target class is abstract with no standalone subclass.</summary>
    private const int UnexercisedAbstractReferencePairs = 26;
    private const string AbstractClass = "the class is abstract and no concrete subclass stands alone in the fixture";
    private const string NoOwnerChain = "no owning field leads from an object the project holds to the class";
    private const string CreationRefused = "only the class's own composer creates it, or no create kind does, so the fixture holds none";
    private static readonly string[] CreationRules = [AbstractClass, NoOwnerChain, CreationRefused];

    private static readonly CellarPropertyType[] OwningTypes =
        [CellarPropertyType.OwningAtomic, CellarPropertyType.OwningCollection, CellarPropertyType.OwningSequence];

    /// <summary>An instance the project holds, or one liblcm creates under an owner.</summary>
    private (Guid? Id, string? Why) Instance(string className, int depth = 0)
    {
        if (RealObject(className) is { } have)
            return (have, null);
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var clid = metadata.GetClassId(className);
        if (metadata.GetAbstract(clid))
        {
            foreach (var sub in metadata.GetAllSubclasses(clid).Where(item => !metadata.GetAbstract(item)))
                if (Instance(metadata.GetClassName(sub), depth + 1) is { Id: not null } found)
                    return found;
            return (null, AbstractClass);
        }
        if (depth > 3)
            return (null, NoOwnerChain);
        var why = NoOwnerChain;
        foreach (var ownerType in _schema.Schema.ObjectTypes)
        {
            foreach (var field in ownerType.Fields)
            {
                if (!(field.Item ?? field).ObjectTypes.Any(t => t.Name == className) || _schema.FieldBinding(ownerType.Name, field.Name) is not { } binding)
                    continue;
                var flid = metadata.GetFieldId(binding.DeclaringClass, binding.Field, true);
                var fieldType = (CellarPropertyType)metadata.GetFieldType(flid);
                if (!OwningTypes.Contains(fieldType))
                    continue;
                var (owner, ownerWhy) = Instance(ownerType.Name, depth + 1);
                if (owner is null)
                {
                    why = ownerWhy ?? why;
                    continue;
                }
                var made = Make(owner.Value, ownerType.Name, field, binding, className);
                if (made && RealObject(className) is { } created)
                    return (created, null);
                _factoryFailures.Add($"{className}: under {ownerType.Name}.{field.Name} made={made}");
                why = CreationRefused;
            }
        }
        return (null, why);
    }

    private static bool IsVector(CellarPropertyType type) => type is CellarPropertyType.OwningCollection or CellarPropertyType.OwningSequence
        or CellarPropertyType.ReferenceCollection or CellarPropertyType.ReferenceSequence;

    /// <summary>Creates a child through the lowerer and the Runner's create handler.</summary>
    private bool Make(Guid owner, string ownerType, RecordField field, (string DeclaringClass, string Field, bool IsCustom) binding, string className)
    {
        var anchored = field.Kind == RecordFieldKind.Sequence && Members(owner, binding).Count > 0;
        var change = NewChange(RecordChangeKind.AddObject, c =>
        {
            Put(c, "ObjectGuid", owner);
            Put(c, "ObjectType", ownerType);
            Put(c, "Field", field.Name);
            Put(c, "Subtree", new RecordObject { Class = className });
            if (anchored) Put(c, "Anchor", StartAnchor);
        });
        var lowering = new RecordChangeSetLowerer(_schema, new RecordWritableFields(_schema)).LowerChanges(_cache, Guid.NewGuid().ToString("N"), [change]);
        if (lowering.Refusals.Count != 0 || lowering.Operations.Count == 0)
            return MakeByFactory(owner, binding, className);
        try
        {
            NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            {
                foreach (var operation in lowering.Operations)
                    OperationHandlerRegistry.Resolve(operation.Kind, "fixture").ApplyAndCaptureEffect(_cache, operation, []);
            });
            return true;
        }
        catch (InvalidOperationException)
        {
            return MakeByFactory(owner, binding, className);
        }
    }

    private readonly SortedSet<string> _factoryFailures = [];

    /// <summary>Test setup, not lowering: the liblcm factory makes a class no create kind authors.</summary>
    private bool MakeByFactory(Guid ownerGuid, (string DeclaringClass, string Field, bool IsCustom) binding, string className)
    {
        var factoryType = typeof(ILexSenseFactory).Assembly.GetTypes().FirstOrDefault(t => t.IsInterface && t.Name == "I" + className + "Factory");
        if (factoryType is null)
        {
            _factoryFailures.Add(className + ": no factory");
            return false;
        }
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var flid = metadata.GetFieldId(binding.DeclaringClass, binding.Field, true);
        var fieldType = (CellarPropertyType)metadata.GetFieldType(flid);
        var owner = _cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(ownerGuid);
        var getInstance = typeof(ILexSenseFactory).Assembly.GetTypes().Where(t => t.IsSealed && t.IsAbstract)
            .SelectMany(t => t.GetMethods()).First(m => m.Name == "GetInstance" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
        var factory = getInstance.MakeGenericMethod(factoryType).Invoke(null, [_cache.ServiceLocator]);
        var creates = factoryType.GetMethods().Concat(factoryType.GetInterfaces().SelectMany(i => i.GetMethods()))
            .Where(m => m.Name == "Create").Select(m => (Method: m, Parameters: m.GetParameters())).ToArray();
        var repository = _cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        object? Argument(Type type) =>
            type == typeof(Guid) ? Guid.NewGuid() : type == typeof(int) ? 0 : type == typeof(string) ? "x"
            : type == typeof(ITsString) ? TsStringUtils.MakeString("x", _cache.DefaultAnalWs)
            : type.IsInstanceOfType(owner) ? owner
            : typeof(ICmObject).IsAssignableFrom(type) ? repository.AllInstances().FirstOrDefault(item => type.IsInstanceOfType(item)) : null;
        // An overload naming the owner places the child; a bare Create is attached by property.
        foreach (var create in creates.OrderBy(c => c.Parameters.Length))
        {
            var arguments = create.Parameters.Select(p => Argument(p.ParameterType)).ToArray();
            if (create.Parameters.Length == 0 || arguments.Any(argument => argument is null) || (!arguments.Any(argument => ReferenceEquals(argument, owner))))
                continue;
            try
            {
                NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => create.Method.Invoke(factory, arguments));
                _factoryFailures.Add(className + ": named create ok " + string.Join(",", create.Parameters.Select(p => p.ParameterType.Name)));
                return true;
            }
            catch (Exception e)
            {
                _factoryFailures.Add(className + ": " + (e.InnerException ?? e).GetType().Name);
            }
        }
        foreach (var create in creates.Where(c => c.Parameters.Length == 0))
        {
            try
            {
                NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
                {
                    var made = (ICmObject)create.Method.Invoke(factory, null)!;
                    var property = owner.GetType().GetProperty(binding.Field + "OS") ?? owner.GetType().GetProperty(binding.Field + "OC")
                        ?? owner.GetType().GetProperty(binding.Field + "OA") ?? throw new InvalidOperationException("no property " + binding.Field);
                    if (IsVector(fieldType))
                        {
                        var list = property.GetValue(owner)!;
                        list.GetType().GetMethods().First(m => m.Name == "Add" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsInstanceOfType(made)).Invoke(list, [made]);
                    }
                    else
                        property.SetValue(owner, made);
                });
                _factoryFailures.Add(className + ": bare create ok");
                return true;
            }
            catch (Exception e)
            {
                _factoryFailures.Add(className + ": " + (e.InnerException ?? e).GetType().Name);
            }
        }
        _factoryFailures.Add(className + ": no usable Create among " + string.Join(" | ", creates.Select(c => string.Join(",", c.Parameters.Select(p => p.ParameterType.Name)))));
        return false;
    }

    /// <summary>Gives an empty field one member: a created child, or an existing object.</summary>
    private string? Seed(Guid owner, string ownerType, RecordField field, (string DeclaringClass, string Field, bool IsCustom) binding)
    {
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var flid = metadata.GetFieldId(binding.DeclaringClass, binding.Field, true);
        var fieldType = (CellarPropertyType)metadata.GetFieldType(flid);
        var repository = _cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var hvo = repository.GetObject(owner).Hvo;
        var why = AbstractClass;
        if (binding.IsCustom && !OwningTypes.Contains(fieldType))
        {
            var publication = UnheldPublication().Hvo;
            NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            {
                if (IsVector(fieldType))
                    _cache.DomainDataByFlid.Replace(hvo, flid, 0, 0, [publication], 1);
                else
                    _cache.DomainDataByFlid.SetObjProp(hvo, flid, publication);
            });
            return null;
        }
        foreach (var target in (field.Item ?? field).ObjectTypes)
        {
            if (OwningTypes.Contains(fieldType))
            {
                var clid = metadata.GetClassId(target.Name);
                if (metadata.GetAbstract(clid))
                    continue;
                if (Make(owner, ownerType, field, binding, target.Name))
                    return null;
                why = CreationRefused;
                continue;
            }
            var (member, memberWhy) = Instance(target.Name);
            if (member is null)
            {
                why = memberWhy ?? why;
                continue;
            }
            var memberHvo = repository.GetObject(member.Value).Hvo;
            NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            {
                if (IsVector(fieldType))
                    _cache.DomainDataByFlid.Replace(hvo, flid, 0, 0, [memberHvo], 1);
                else
                    _cache.DomainDataByFlid.SetObjProp(hvo, flid, memberHvo);
            });
            return null;
        }
        return why;
    }

    [Fact]
    public void EveryCoveredPairIsLoweredOrRefusedByARegisteredCode_AndEveryOfferedPairIsLowered()
    {
        var catalog = new CatalogWritableFields(_schema);
        var resolver = new RecordWritableFields(_schema);
        var lowerer = new RecordChangeSetLowerer(_schema, resolver);
        var failures = new List<string>();
        var gaps = new SortedSet<string>(StringComparer.Ordinal);
        var lowered = 0;
        var skipped = 0;
        var unexercised = new List<string>();
        foreach (var ((typeName, fieldName, verb), resolution) in resolver.Entries)
        {
            if (resolution is not WritableFieldResolution.Covered { Owner: null })
                continue;
            var type = _schema.Schema.ObjectType(typeName)!;
            var field = type.Field(fieldName)!;
            var binding = _schema.FieldBinding(typeName, fieldName)!.Value;
            var handlerVerb = verb == RecordWriteVerb.InsertReference ? RecordWriteVerb.AddReference : verb;
            var offered = Declarations(binding, handlerVerb).Any(d => RecordLoweringRules.Lowers(d.Kind, d.Write));
            var subject = $"{typeName}.{fieldName} {verb}";
            var (changes, gap) = ChangesFor(type, field, verb, catalog, binding);
            if (gap is not null)
            {
                if (offered)
                {
                    gaps.Add(gap);
                    unexercised.Add(subject + " [" + gap + "]");
                }

                skipped++;
                continue;
            }
            foreach (var change in changes)
            {
                var result = lowerer.LowerChanges(_cache, "agreement", [change]);
                if (result.Operations.Count + result.Refusals.Count != 1)
                    failures.Add($"{subject}: neither an operation nor a refusal.");
                foreach (var refusal in result.Refusals)
                {
                    if (!RefusalCatalog.Specs.ContainsKey(refusal.Code))
                        failures.Add($"{subject}: refused by unregistered code {refusal.Code}.");
                    // Against a real object, any refusal of an offered pair is the bug: not-executable and value-stale included.
                    if (offered)
                        failures.Add($"{subject}: offered by the adapter but refused: {refusal.Code} {refusal.Reason}");
                }
                foreach (var operation in result.Operations)
                {
                    lowered++;
                    var (declaring, declaredField) = DeclaredAs(binding);
                    if (!OperationHandlerRegistry.FieldWrites(operation.Kind).Any(write =>
                            write.DeclaringClass == declaring && write.Field == declaredField))
                        failures.Add($"{subject}: lowered to {operation.Kind}, which does not declare the field.");
                    if (!offered)
                        failures.Add($"{subject}: lowered although the adapter does not offer it.");
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Distinct()));
        Assert.True(lowered > 50, $"only {lowered} lowered");
        // A pair whose class the fixture cannot create is skipped under a named rule.
        Assert.True(gaps.IsSubsetOf(CreationRules), string.Join(Environment.NewLine, gaps.Except(CreationRules)));
        Assert.True(unexercised.Count == UnexercisedAbstractReferencePairs && unexercised.All(item => item.EndsWith("[" + AbstractClass + "]", StringComparison.Ordinal)),
            $"{unexercised.Count} offered pairs unexercised: " + string.Join(" | ", unexercised));
    }

    private IMoMorphType MorphType(Guid id) => _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(id);

    private Guid NewPartOfSpeech()
    {
        var made = default(IPartOfSpeech)!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            made = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(made);
        });
        return made.Guid;
    }

    private Guid? RealTarget((string DeclaringClass, string Field, bool IsCustom) binding)
    {
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var target = metadata.GetDstClsName(metadata.GetFieldId(binding.DeclaringClass, binding.Field, true));
        return Instance(target).Id ?? (target == "CmPossibility" ? UnheldPublication().Guid : target == "PartOfSpeech" ? NewPartOfSpeech() : null);
    }

    [Fact]
    public void OneEachOfAReferenceSetAMoveAndAClassPropertyCreateExecuteInADryRun()
    {
        var catalog = new CatalogWritableFields(_schema);
        var resolver = new RecordWritableFields(_schema);
        var lowerer = new RecordChangeSetLowerer(_schema, resolver);
        var executed = new Dictionary<string, bool> { ["reference set"] = false, ["move"] = false, ["class-property create"] = false };
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var allomorph = _cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
                _entry.AlternateFormsOS.Add(allomorph);
                allomorph.MorphTypeRA = MorphType(MoMorphTypeTags.kguidMorphStem);
            }
        });
        var lastReason = new Dictionary<string, string>();
        var allReasons = new List<string>();
        foreach (var ((typeName, fieldName, verb), resolution) in resolver.Entries)
        {
            if (resolution is not WritableFieldResolution.Covered { Owner: null } || _schema.Schema.ObjectType(typeName)?.Field(fieldName) is not { } field
                || _schema.FieldBinding(typeName, fieldName) is not { } binding)
                continue;
            var declared = Declarations(binding, verb == RecordWriteVerb.InsertReference ? RecordWriteVerb.AddReference : verb)
                .Where(d => RecordLoweringRules.Lowers(d.Kind, d.Write)).ToArray();
            var family = declared.Length == 0 ? null
                : verb == RecordWriteVerb.Set && field.Kind == RecordFieldKind.Reference ? "reference set"
                : verb is RecordWriteVerb.MoveObject or RecordWriteVerb.MoveReference ? "move"
                : verb == RecordWriteVerb.AddObject && declared.Any(d => d.Write.CreatePayload == OwningCreatePayload.ClassProperty) ? "class-property create"
                : null;
            if (family is null || executed[family] || (family == "reference set" && RealTarget(binding) is null))
                continue;
            var (changes, gap) = ChangesFor(_schema.Schema.ObjectType(typeName)!, field, verb, catalog, binding);
            if (gap is not null || changes.Count == 0)
            {
                lastReason[family] = $"{typeName}.{fieldName} {verb}: {gap}";
                allReasons.Add(lastReason[family]);
                continue;
            }
            var lowering = lowerer.LowerChanges(_cache, Guid.NewGuid().ToString("N"), changes);
            if (lowering.Refusals.Count != 0 || lowering.Operations.Count == 0)
            {
                lastReason[family] = $"{typeName}.{fieldName} {verb}: refused {string.Join(",", lowering.Refusals.Select(r => r.Code + " " + r.Reason))}";
                allReasons.Add(lastReason[family]);
                continue;
            }
            NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            {
                foreach (var process in _cache.ServiceLocator.GetInstance<IMoAffixProcessRepository>().AllInstances().ToList())
                    _cache.DomainDataByFlid.DeleteObj(process.Hvo);
            });
            try
            {
                executed[family] = ScratchDryRun.Of(_cache, Proposal(lowering.Operations)).ExpectedEffects.Count > 0;
                if (!executed[family]) allReasons.Add($"{typeName}.{fieldName} {verb}: no effects from {string.Join(",", lowering.Operations.Select(o => o.Kind))}");
            }
            catch (Exception e)
            {
                // This pair's operation does not run on the fixture's object; the next pair of the family is tried.
                lastReason[family] = $"{typeName}.{fieldName} {verb}: {e.GetType().Name} {e.Message}";
                allReasons.Add(lastReason[family]);
            }
        }
        Assert.All(executed, family => Assert.True(family.Value, family.Key + " has no pair that lowers and dry-runs; last: " + lastReason.GetValueOrDefault(family.Key) + "\n" + string.Join("\n", allReasons.Distinct().Take(40))));
    }

    [Fact]
    public void EveryRegisteredFieldWriteStatesItsPayload()
    {
        var unstated = OperationHandlerRegistry.RegisteredKinds
            .SelectMany(kind => OperationHandlerRegistry.FieldWrites(kind).Select(write => (kind, write)))
            .Where(item => item.write.Payload == FieldWritePayload.Unspecified)
            .Select(item => item.kind + " " + item.write.DeclaringClass + "." + item.write.Field + " " + item.write.Verb)
            .ToArray();
        Assert.True(unstated.Length == 0, string.Join(Environment.NewLine, unstated));
    }

    [Fact]
    public void ABooleanScalarSetLowersAndExecutes()
    {
        var lowering = Lower(PlanEdit(d => d.Data["do-not-use-for-parsing"] = RecordScalar.Of(true)));
        var operation = Assert.Single(lowering.Operations);
        Assert.EndsWith("DoNotUseForParsing", operation.Kind, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(DryRun(lowering).ExpectedEffects);
    }

    private static string Custom(string name) => "custom-LexEntry-" + name;

    private SIL.Motif.Model.DryRun.DryRun DryRunOf(RecordLowering lowering, string kind)
    {
        Assert.Equal(kind, Assert.Single(lowering.Operations).Kind);
        return DryRun(lowering);
    }

    [Fact]
    public void CustomFieldsOfEveryWritableTypeLowerToOneKindAndReadBack()
    {
        var target = UnheldPublication();
        var expectations = new (string Field, Action<RecordDocument> Edit, string Kind, string Reads)[]
        {
            ("ZedInteger", d => d.Data[Custom("ZedInteger")] = RecordScalar.Of(7), "customField/setInteger", "7"),
            ("ZedMultiUnicode", d => d.Data[Custom("ZedMultiUnicode")] = new RecordAlternatives().Add("en", new RecordScalar("zzmulti")),
                "customField/setAlternative", "zzmulti"),
            ("ZedMultiString", d => d.Data[Custom("ZedMultiString")] = new RecordAlternatives().Add("en", new RecordScalar("zzrich")),
                "customField/setAlternative", "zzrich"),
            ("ZedString", d => d.Data[Custom("ZedString")] = new RecordScalar("zzstring"), "customField/setString", "zzstring"),
        };
        foreach (var (field, edit, kind, reads) in expectations)
        {
            var dryRun = DryRunOf(Lower(PlanEdit(edit)), kind);
            Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == "customField/" + field && effect.After.Values.Contains(reads));
        }

        var reference = DryRunOf(Lower(PlanEdit(d => d.Data[Custom("ZedReference")] = new RecordScalar(Link(target)))),
            "customField/setReference");
        Assert.Contains(reference.ExpectedEffects, effect => effect.Field == "customField/ZedReference" &&
            effect.After.Values.Any(value => CanonicalId.Parse(value).ToGuid() == target.Guid));
        var collection = DryRunOf(Lower(PlanEdit(d => d.Data[Custom("ZedReferences")] = new RecordList().Add(new RecordScalar(Link(target))))),
            "customField/addReference");
        Assert.Contains(collection.ExpectedEffects, effect => effect.Field == "customField/ZedReferences" && effect.After.Count == 1);
    }

    [Fact]
    public void ClearingACustomFieldLowersToItsClearKind()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var flid = _cache.MetaDataCacheAccessor.GetFieldId("LexEntry", "ZedInteger", true);
            _cache.DomainDataByFlid.SetInt(_entry.Hvo, flid, 5);
            flid = _cache.MetaDataCacheAccessor.GetFieldId("LexEntry", "ZedMultiUnicode", true);
            _cache.DomainDataByFlid.SetMultiStringAlt(_entry.Hvo, flid, _cache.DefaultAnalWs,
                TsStringUtils.MakeString("zzold", _cache.DefaultAnalWs));
        });
        var integer = DryRunOf(Lower(PlanEdit(d => d.Data[Custom("ZedInteger")] = null)), "customField/clearInteger");
        Assert.Contains(integer.ExpectedEffects, effect => effect.Before.Values.Contains("5") && effect.After.Values.Contains("0"));
        var multi = DryRunOf(Lower(PlanEdit(d => ((RecordAlternatives)d.Data[Custom("ZedMultiUnicode")]!).Items.Clear())),
            "customField/clearAlternative");
        Assert.Contains(multi.ExpectedEffects, effect => effect.Before.Values.Contains("zzold") && effect.After.Count == 0);
    }

    [Fact]
    public void ACustomFieldTypeNoKindWritesIsOfferedByNeitherLookupAndRefusedByCode()
    {
        var catalog = new CatalogWritableFields(_schema);
        Assert.True(catalog.CanWrite("LexEntry", "ZedInteger"));
        Assert.True(catalog.CanWrite("LexEntry", "ZedReferences"));
        Assert.False(catalog.CanWrite("LexEntry", "ZedBoolean"));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.CustomField),
            new RecordWritableFields(_schema).Resolve("LexEntry", Custom("ZedBoolean"), RecordWriteVerb.Set));
        Assert.Contains(_schema.OmittedCustomFields, omitted => omitted.Field == "ZedDate");
        var change = NewChange(RecordChangeKind.Set, c =>
        {
            Put(c, "ObjectGuid", _entry.Guid);
            Put(c, "ObjectType", "LexEntry");
            Put(c, "Field", Custom("ZedBoolean"));
            Put(c, "New", "true");
        });
        var lowering = new RecordChangeSetLowerer(_schema, new RecordWritableFields(_schema)).LowerChanges(_cache, "boolean", [change]);
        Assert.Equal(RefusalCodes.RecordImportFieldUncovered, Assert.Single(lowering.Refusals).Code);
        Assert.Empty(lowering.Operations);
    }

    [Fact]
    public void ADeleteOfANonSenseOwnedObjectIsRefusedWithATypedCodeAndNoOperation()
    {
        var change = NewChange(RecordChangeKind.RemoveObject, c =>
        {
            Put(c, "ObjectGuid", _entry.Guid);
            Put(c, "ObjectType", "LexEntry");
            Put(c, "Field", "allomorphs");
            Put(c, "ItemGuid", Guid.NewGuid());
        });
        var lowering = new RecordChangeSetLowerer(_schema, new RecordWritableFields(_schema)).LowerChanges(_cache, "delete", [change]);
        Assert.Equal(RefusalCodes.RecordImportFieldUncovered, Assert.Single(lowering.Refusals).Code);
        Assert.Empty(lowering.Operations);
    }

    [Fact]
    public void AChangeToAGenDateCustomFieldIsRefusedNotDropped()
    {
        var change = NewChange(RecordChangeKind.Set, c =>
        {
            Put(c, "ObjectGuid", _entry.Guid);
            Put(c, "ObjectType", "LexEntry");
            Put(c, "Field", Custom("ZedDate"));
            Put(c, "New", "AD 2000-1-1");
        });
        var lowering = new RecordChangeSetLowerer(_schema, new RecordWritableFields(_schema)).LowerChanges(_cache, "date", [change]);
        Assert.Equal(RefusalCodes.RecordImportFieldUncovered, Assert.Single(lowering.Refusals).Code);
        Assert.Empty(lowering.Operations);
    }

    [Fact]
    public void AnOwnedStTextCustomFieldIsWrittenAsParagraphsAndAGenDateOneIsReportedOmitted()
    {
        var fields = new RecordWritableFields(_schema);
        Assert.Equal(new WritableFieldResolution.Covered("text/setParagraphs"), fields.Resolve("LexEntry", Custom("ZedText"), RecordWriteVerb.SetRich));
        Assert.Equal(new WritableFieldResolution.Covered("text/setParagraphs"), fields.Resolve("LexEntry", Custom("ZedText"), RecordWriteVerb.ClearRich));
        Assert.True(new CatalogWritableFields(_schema).CanWrite("LexEntry", "ZedText"));
        var omitted = Assert.Single(_schema.OmittedCustomFields, item => item.Field == "ZedDate");
        Assert.Contains("GenDate", omitted.Reason);
        Assert.Null(_schema.FieldBinding("LexEntry", Custom("ZedDate")));
    }

    [Fact]
    public void AnOwnedStTextCustomFieldEditsLowerToOneParagraphsOperationAndApplyReadsBackWithUnchangedParagraphsKept()
    {
        var flid = _cache.MetaDataCacheAccessor.GetFieldId("LexEntry", "ZedText", true);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var text = (IStText)_cache.ServiceLocator.GetObject(_cache.DomainDataByFlid.MakeNewObject(StTextTags.kClassId, _entry.Hvo, flid, -2));
            foreach (var line in new[] { "zzfirst", "zzsecond" })
            {
                var paragraph = _cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                text.ParagraphsOS.Add(paragraph);
                paragraph.Contents = TsStringUtils.MakeString(line, _cache.DefaultAnalWs);
            }
        });
        var export = _exporter.Export(_entry.Guid);
        Assert.Contains("zzsecond", export.Text);

        var lowering = Lower(PlanEdit(d =>
        {
            var held = ((RecordScalar)d.Data[Custom("ZedText")]!).Text;
            d.Data[Custom("ZedText")] = new RecordScalar(held.Replace("zzsecond", "zzchanged") + "\n\nzzthird");
        }));
        var operation = Assert.Single(lowering.Operations);
        Assert.Equal("text/setParagraphs", operation.Kind);
        Assert.Equal("custom:LexEntry.ZedText", operation.After!.Value.GetProperty("field").GetString());
        var effect = Assert.Single(DryRun(lowering).ExpectedEffects);
        Assert.Equal("text/custom:LexEntry.ZedText", effect.Field);
        Assert.Contains("zzchanged", effect.After.Values);
        Assert.Contains("zzthird", effect.After.Values);

        var kept = ((IStText)_cache.ServiceLocator.GetObject(_cache.DomainDataByFlid.get_ObjectProp(_entry.Hvo, flid))).ParagraphsOS[0].Guid;
        Execute(operation);
        var after = (IStText)_cache.ServiceLocator.GetObject(_cache.DomainDataByFlid.get_ObjectProp(_entry.Hvo, flid));
        Assert.Equal(["zzfirst", "zzchanged", "zzthird"], after.ParagraphsOS.Cast<IStTxtPara>().Select(p => p.Contents.Text));
        Assert.Equal(kept, after.ParagraphsOS[0].Guid);

        var cleared = Lower(PlanEdit(d => d.Data[Custom("ZedText")] = null));
        Assert.Equal("text/setParagraphs", Assert.Single(cleared.Operations).Kind);
    }

    [Fact]
    public void AnOwnedStTextCustomFieldThatHoldsNothingIsCreatedByAnEdit()
    {
        var lowering = Lower(PlanEdit(d => d.Data[Custom("ZedText")] = new RecordScalar("zznew")));
        var operation = Assert.Single(lowering.Operations);
        Execute(operation);
        var flid = _cache.MetaDataCacheAccessor.GetFieldId("LexEntry", "ZedText", true);
        var made = (IStText)_cache.ServiceLocator.GetObject(_cache.DomainDataByFlid.get_ObjectProp(_entry.Hvo, flid));
        Assert.Equal("zznew", Assert.Single(made.ParagraphsOS.Cast<IStTxtPara>()).Contents.Text);
    }

    [Fact]
    public void AFileThatEditsAGenDateCustomFieldIsRefusedWholeNamingTheFieldAndNoOperationIsMade()
    {
        var text = _exporter.Export(_entry.Guid).Text!;
        var edited = text.Replace("record-data:\n", "record-data:\n  " + Custom("ZedDate") + ": \"AD 2030-1-1 Exact\"\n");
        Assert.NotEqual(text, edited);
        var lowering = Lower(new ProjectRecordImporter(_exporter).Plan(edited, new CatalogWritableFields(_schema)));
        var refusal = Assert.Single(lowering.Refusals);
        Assert.Equal(RefusalCodes.RecordImportPlanRefused, refusal.Code);
        Assert.Contains("custom field LexEntry.ZedDate", refusal.Reason);
        Assert.Contains("GenDate", refusal.Reason);
        Assert.Empty(lowering.Operations);
    }

    private void Execute(OperationEnvelope operation) => NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        OperationHandlerRegistry.Resolve(operation.Kind, "fixture").ApplyAndCaptureEffect(_cache, operation, []));

    [Fact]
    public void AddingASecondDialectLabelAfterTheHeldOneAppendsAndAnInsertBeforeItIsRefused()
    {
        var held = NewSetItem("dialect-labels");
        var added = NewSetItem("dialect-labels");
        AttachToSense("dialect-labels", held);

        var appended = Lower(PlanEdit(d => SenseAt(d, 0).Set("dialect-labels",
            new RecordList().Add(new RecordScalar(LinkTo(held))).Add(new RecordScalar(LinkTo(added))))));
        Assert.Empty(appended.Refusals);
        var operation = Assert.Single(appended.Operations);
        Assert.Equal("lexical/lexSense/addRefDialectLabels", operation.Kind);
        Assert.Null(operation.Placement);
        var effect = Assert.Single(DryRun(appended).ExpectedEffects);
        Assert.Contains(effect.After.Values, value => CanonicalId.Parse(value).ToGuid() == added.Guid);

        var inserted = Lower(PlanEdit(d => SenseAt(d, 0).Set("dialect-labels",
            new RecordList().Add(new RecordScalar(LinkTo(added))).Add(new RecordScalar(LinkTo(held))))));
        Assert.Empty(inserted.Operations);
        Assert.Equal(RefusalCodes.RecordImportFieldUncovered, Assert.Single(inserted.Refusals).Code);
    }

    [Fact]
    public void ClearingEveryCustomFieldShapeLowersToItsKindAndReadsBack()
    {
        var held = UnheldPublication();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var metadata = _cache.MetaDataCacheAccessor;
            var data = _cache.DomainDataByFlid;
            data.SetMultiStringAlt(_entry.Hvo, metadata.GetFieldId("LexEntry", "ZedMultiString", true), _cache.DefaultAnalWs,
                TsStringUtils.MakeString("zzrichold", _cache.DefaultAnalWs));
            data.SetString(_entry.Hvo, metadata.GetFieldId("LexEntry", "ZedString", true),
                TsStringUtils.MakeString("zzstringold", _cache.DefaultAnalWs));
            data.SetObjProp(_entry.Hvo, metadata.GetFieldId("LexEntry", "ZedReference", true), held.Hvo);
            data.Replace(_entry.Hvo, metadata.GetFieldId("LexEntry", "ZedReferences", true), 0, 0, [held.Hvo], 1);
        });
        var rich = DryRunOf(Lower(PlanEdit(d => ((RecordAlternatives)d.Data[Custom("ZedMultiString")]!).Items.Clear())),
            "customField/clearAlternative");
        Assert.Contains(rich.ExpectedEffects, effect => effect.Field == "customField/ZedMultiString" &&
            effect.Before.Values.Contains("zzrichold") && effect.After.Count == 0);
        var text = DryRunOf(Lower(PlanEdit(d => d.Data[Custom("ZedString")] = null)), "customField/clearString");
        Assert.Contains(text.ExpectedEffects, effect => effect.Before.Values.Contains("zzstringold") && effect.After.Count == 0);
        var reference = DryRunOf(Lower(PlanEdit(d => d.Data[Custom("ZedReference")] = null)), "customField/clearReference");
        Assert.Contains(reference.ExpectedEffects, effect => effect.Before.Count == 1 && effect.After.Count == 0);
        var collection = DryRunOf(Lower(PlanEdit(d => ((RecordList)d.Data[Custom("ZedReferences")]!).Items.Clear())),
            "customField/removeReference");
        Assert.Contains(collection.ExpectedEffects, effect => effect.Before.Count == 1 && effect.After.Count == 0);
    }

    private void RefusedWith(string code, string kind, object after, Guid? target = null)
    {
        var operation = new OperationEnvelope(CanonicalId.FromGuid(Guid.NewGuid()), kind,
            target: CanonicalId.FromGuid(target ?? _entry.Guid), after: JsonSerializer.SerializeToElement(after));
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(() => ScratchDryRun.Of(_cache, Proposal([operation])));
        Assert.Equal(code, refusal.Code);
        Assert.Contains(code, RefusalCatalog.Specs.Keys);
    }

    [Fact]
    public void TheRunnerRefusesACustomFieldOperationThePlanCouldNotHaveSeenWithARegisteredCode()
    {
        RefusedWith(RefusalCodes.CustomFieldMissing, "customField/setInteger", new { field = "ZedGone", value = 1 });
        RefusedWith(RefusalCodes.CustomFieldNotCustom, "customField/setInteger", new { field = "HomographNumber", value = 1 });
        RefusedWith(RefusalCodes.CustomFieldWrongType, "customField/setInteger", new { field = "ZedString", value = 1 });
        RefusedWith(RefusalCodes.CustomFieldWrongType, "customField/setString", new { field = "ZedInteger", ws = "en", text = "zz" });
        RefusedWith(RefusalCodes.CustomFieldWrongClass, "customField/setReference",
            new { field = "ZedReference", @ref = CanonicalId.FromGuid(_entry.Guid).Value });
        RefusedWith(RefusalCodes.CustomFieldWrongList, "customField/setReference",
            new { field = "ZedReference", @ref = CanonicalId.FromGuid(NewPartOfSpeech()).Value });
        RefusedWith(RefusalCodes.CustomFieldWrongClass, "customField/addReference",
            new { field = "ZedReferences", member = CanonicalId.FromGuid(_entry.Guid).Value });
        RefusedWith(RefusalCodes.CustomFieldWrongList, "customField/addReference",
            new { field = "ZedReferences", member = CanonicalId.FromGuid(NewPartOfSpeech()).Value });
        RefusedWith(RefusalCodes.CustomFieldWritingSystem, "customField/setString", new { field = "ZedString", ws = "zz-nope", text = "zz" });
        // The field's selector is analysis, so a vernacular tag is refused.
        RefusedWith(RefusalCodes.CustomFieldWritingSystem, "customField/setString",
            new { field = "ZedString", ws = _cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id, text = "zz" });
    }

    private ICmPossibility NewListItem(Func<ICmPossibilityList> list)
    {
        var made = default(ICmPossibility)!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            made = _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            list().PossibilitiesOS.Add(made);
            made.Name.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("zzitem", _cache.DefaultAnalWs));
        });
        return made;
    }

    private ICmPossibilityList StatusList() => _cache.LangProject.StatusOA ??=
        _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();

    private ICmPossibilityList SenseTypeList() => _cache.LangProject.LexDbOA.SenseTypesOA ??=
        _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();

    private static string LinkTo(ICmPossibility target) =>
        "[zzitem](/objects/" + target.Guid.ToString("D") + "){.name guid=" + target.Guid.ToString("D") + "}";

    [Fact]
    public void SenseStatusAndSenseTypeSetAndClearLowerToTheirKindsAndReadBack()
    {
        var status = NewListItem(StatusList);
        var type = NewListItem(SenseTypeList);
        var senseDocument = (Action<RecordDocument> edit) => PlanEdit(d => edit(d));
        var set = Lower(senseDocument(d =>
        {
            SenseAt(d, 0).Set("status", new RecordScalar(LinkTo(status)));
            SenseAt(d, 0).Set("sense-type", new RecordScalar(LinkTo(type)));
        }));
        Assert.Empty(set.Refusals);
        Assert.Equal(["lexical/lexSense/setSenseType", "lexical/lexSense/setStatus"], set.Operations.Select(op => op.Kind).Order().ToArray());
        var effects = DryRun(set).ExpectedEffects;
        Assert.Contains(effects, e => e.Field == "lexical/lexSense/status" &&
            e.After.Values.Any(v => CanonicalId.Parse(v).ToGuid() == status.Guid));
        Assert.Contains(effects, e => e.Field == "lexical/lexSense/senseType" &&
            e.After.Values.Any(v => CanonicalId.Parse(v).ToGuid() == type.Guid));

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _sense.StatusRA = status;
            _sense.SenseTypeRA = type;
        });
        var cleared = Lower(senseDocument(d =>
        {
            SenseAt(d, 0).Fields.RemoveAll(f => f.Key == "status");
            SenseAt(d, 0).Fields.RemoveAll(f => f.Key == "sense-type");
        }));
        Assert.Equal(["lexical/lexSense/clearSenseType", "lexical/lexSense/clearStatus"], cleared.Operations.Select(op => op.Kind).Order().ToArray());
        Assert.All(DryRun(cleared).ExpectedEffects, e => Assert.Single(e.Before.Values));
        Assert.True(new CatalogWritableFields(_schema).CanWrite("LexSense", "Status"));
        Assert.True(new CatalogWritableFields(_schema).CanWrite("LexSense", "SenseType"));
    }

    [Fact]
    public void TheRunnerRefusesASenseStatusOrTypeFromAnotherListWithARegisteredCode()
    {
        var elsewhere = CanonicalId.FromGuid(_publication.Guid).Value;
        NewListItem(StatusList);
        NewListItem(SenseTypeList);
        RefusedWith(RefusalCodes.ReferenceWrongList, "lexical/lexSense/setStatus", new { @ref = elsewhere }, _sense.Guid);
        RefusedWith(RefusalCodes.ReferenceWrongList, "lexical/lexSense/setSenseType", new { @ref = elsewhere }, _sense.Guid);
        var swapped = CanonicalId.FromGuid(NewListItem(SenseTypeList).Guid).Value;
        RefusedWith(RefusalCodes.ReferenceWrongList, "lexical/lexSense/setStatus", new { @ref = swapped }, _sense.Guid);
    }

    [Fact]
    public void ASubPossibilityOfTheFieldsListIsAcceptedAndAnAbsentListRefuses()
    {
        var parent = NewListItem(SenseTypeList);
        var child = default(ICmPossibility)!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            child = _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            parent.SubPossibilitiesOS.Add(child);
        });
        var accepted = new OperationEnvelope(CanonicalId.FromGuid(Guid.NewGuid()), "lexical/lexSense/setSenseType",
            target: CanonicalId.FromGuid(_sense.Guid), after: JsonSerializer.SerializeToElement(new { @ref = CanonicalId.FromGuid(child.Guid).Value }));
        Assert.NotEmpty(ScratchDryRun.Of(_cache, Proposal([accepted])).ExpectedEffects);

        Assert.Null(_cache.LangProject.StatusOA);
        RefusedWith(RefusalCodes.ReferenceWrongList, "lexical/lexSense/setStatus",
            new { @ref = CanonicalId.FromGuid(child.Guid).Value }, _sense.Guid);
    }

    private ICmPossibilityList EnsureList(string field) => field switch
    {
        "usage-types" => _cache.LangProject.LexDbOA.UsageTypesOA ??= NewList(),
        "dialect-labels" => _cache.LangProject.LexDbOA.DialectLabelsOA ??= NewList(),
        "domain-types" => _cache.LangProject.LexDbOA.DomainTypesOA ??= NewList(),
        "semantic-domains" => _cache.LangProject.SemanticDomainListOA ??= NewList(),
        "anthro-codes" => _cache.LangProject.AnthroListOA ??= NewList(),
        "do-not-publish-in" => _cache.LangProject.LexDbOA.PublicationTypesOA ??= NewList(),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private ICmPossibilityList NewList() => _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();

    private ICmPossibility NewSetItem(string field)
    {
        ICmPossibility made = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var list = EnsureList(field);
            made = field switch
            {
                "semantic-domains" => _cache.ServiceLocator.GetInstance<ICmSemanticDomainFactory>().Create(),
                "anthro-codes" => _cache.ServiceLocator.GetInstance<ICmAnthroItemFactory>().Create(),
                _ => _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create(),
            };
            list.PossibilitiesOS.Add(made);
            made.Name.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("zzitem", _cache.DefaultAnalWs));
        });
        return made;
    }

    private void AttachToSense(string field, ICmPossibility item) => NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
    {
        switch (field)
        {
            case "usage-types": _sense.UsageTypesRC.Add(item); break;
            case "dialect-labels": _sense.DialectLabelsRS.Add(item); break;
            case "domain-types": _sense.DomainTypesRC.Add(item); break;
            case "semantic-domains": _sense.SemanticDomainsRC.Add((ICmSemanticDomain)item); break;
            case "anthro-codes": _sense.AnthroCodesRC.Add((ICmAnthroItem)item); break;
            case "do-not-publish-in": _sense.DoNotPublishInRC.Add(item); break;
        }
    });

    [Theory]
    [InlineData("usage-types", "UsageTypes")]
    [InlineData("dialect-labels", "DialectLabels")]
    [InlineData("domain-types", "DomainTypes")]
    [InlineData("semantic-domains", "SemanticDomains")]
    [InlineData("anthro-codes", "AnthroCodes")]
    [InlineData("do-not-publish-in", "DoNotPublishIn")]
    public void ASenseReferenceSetAddAndRemoveLowerToTheirKindsAndReadBack(string field, string name)
    {
        var item = NewSetItem(field);
        var added = Lower(PlanEdit(d => SenseAt(d, 0).Set(field, new RecordList().Add(new RecordScalar(LinkTo(item))))));
        Assert.Empty(added.Refusals);
        var addOp = Assert.Single(added.Operations);
        Assert.Equal("lexical/lexSense/addRef" + name, addOp.Kind);
        var addEffect = Assert.Single(DryRun(added).ExpectedEffects);
        Assert.Empty(addEffect.Before.Values);
        Assert.Contains(addEffect.After.Values, v => CanonicalId.Parse(v).ToGuid() == item.Guid);

        AttachToSense(field, item);
        var removed = Lower(PlanEdit(d => SenseAt(d, 0).Fields.RemoveAll(f => f.Key == field)));
        Assert.Empty(removed.Refusals);
        Assert.Equal("lexical/lexSense/removeRef" + name, Assert.Single(removed.Operations).Kind);
        var removeEffect = Assert.Single(DryRun(removed).ExpectedEffects);
        Assert.Contains(removeEffect.Before.Values, v => CanonicalId.Parse(v).ToGuid() == item.Guid);
        Assert.Empty(removeEffect.After.Values);
        Assert.True(new CatalogWritableFields(_schema).CanWrite("LexSense", name));
    }

    [Theory]
    [InlineData("usage-types", "UsageTypes")]
    [InlineData("dialect-labels", "DialectLabels")]
    [InlineData("domain-types", "DomainTypes")]
    [InlineData("semantic-domains", "SemanticDomains")]
    [InlineData("anthro-codes", "AnthroCodes")]
    [InlineData("do-not-publish-in", "DoNotPublishIn")]
    public void ASenseReferenceSetAddRefusesAnObjectFromAnotherListWithAWrongListCode(string field, string name)
    {
        NewSetItem(field);
        ICmPossibility elsewhere = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var other = NewList();
            _cache.LangProject.ConfidenceLevelsOA ??= other;
            elsewhere = field switch
            {
                "semantic-domains" => _cache.ServiceLocator.GetInstance<ICmSemanticDomainFactory>().Create(),
                "anthro-codes" => _cache.ServiceLocator.GetInstance<ICmAnthroItemFactory>().Create(),
                _ => _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create(),
            };
            _cache.LangProject.ConfidenceLevelsOA.PossibilitiesOS.Add(elsewhere);
        });
        RefusedWith(RefusalCodes.ReferenceWrongList, "lexical/lexSense/addRef" + name,
            new { member = CanonicalId.FromGuid(elsewhere.Guid).Value }, _sense.Guid);
    }

    [Fact]
    public void ASenseReferenceSetAddRefusesAWrongClassAndAcceptsASubPossibility()
    {
        var parent = NewSetItem("usage-types");
        ICmPossibility child = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            child = _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            parent.SubPossibilitiesOS.Add(child);
        });
        var accepted = new OperationEnvelope(CanonicalId.FromGuid(Guid.NewGuid()), "lexical/lexSense/addRefUsageTypes",
            target: CanonicalId.FromGuid(_sense.Guid), after: JsonSerializer.SerializeToElement(new { member = CanonicalId.FromGuid(child.Guid).Value }));
        Assert.NotEmpty(ScratchDryRun.Of(_cache, Proposal([accepted])).ExpectedEffects);

        var wrongClass = new OperationEnvelope(CanonicalId.FromGuid(Guid.NewGuid()), "lexical/lexSense/addRefSemanticDomains",
            target: CanonicalId.FromGuid(_sense.Guid), after: JsonSerializer.SerializeToElement(new { member = CanonicalId.FromGuid(parent.Guid).Value }));
        var failure = Assert.ThrowsAny<ProposalRefusalException>(() => ScratchDryRun.Of(_cache, Proposal([wrongClass])));
        Assert.Equal(RefusalCodes.ReferenceWrongClass, failure.Code);
    }

    private OperationEnvelope SenseSetOp(string kind, ICmObject member, ICmObject? target = null) =>
        new(CanonicalId.FromGuid(Guid.NewGuid()), kind, target: CanonicalId.FromGuid((target ?? _sense).Guid),
            after: JsonSerializer.SerializeToElement(new { member = CanonicalId.FromGuid(member.Guid).Value }));

    [Fact]
    public void ASenseReferenceSetAddOfAMemberTheSenseHoldsAndRemoveOfAnAbsentOneChangeNothing()
    {
        var held = NewSetItem("usage-types");
        var absent = NewSetItem("usage-types");
        AttachToSense("usage-types", held);
        var duplicate = ScratchDryRun.Of(_cache, Proposal([SenseSetOp("lexical/lexSense/addRefUsageTypes", held)]));
        Assert.All(duplicate.ExpectedEffects, e => Assert.Equal(e.Before.Values, e.After.Values));
        var removeAbsent = ScratchDryRun.Of(_cache, Proposal([SenseSetOp("lexical/lexSense/removeRefUsageTypes", absent)]));
        Assert.All(removeAbsent.ExpectedEffects, e => Assert.Equal(e.Before.Values, e.After.Values));
    }

    [Fact]
    public void AnExampleSentencePublishInAddAndRemoveReadBackAndRefuseAnotherList()
    {
        ILexExampleSentence example = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            example = _cache.ServiceLocator.GetInstance<ILexExampleSentenceFactory>().Create();
            _sense.ExamplesOS.Add(example);
        });
        var publication = UnheldPublication();
        var add = ScratchDryRun.Of(_cache, Proposal([SenseSetOp("lexical/lexExampleSentence/addRefDoNotPublishIn", publication, example)]));
        var addEffect = Assert.Single(add.ExpectedEffects);
        Assert.Contains(addEffect.After.Values, v => CanonicalId.Parse(v).ToGuid() == publication.Guid);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => example.DoNotPublishInRC.Add(publication));
        var remove = ScratchDryRun.Of(_cache, Proposal([SenseSetOp("lexical/lexExampleSentence/removeRefDoNotPublishIn", publication, example)]));
        var removeEffect = Assert.Single(remove.ExpectedEffects);
        Assert.Contains(removeEffect.Before.Values, v => CanonicalId.Parse(v).ToGuid() == publication.Guid);
        Assert.Empty(removeEffect.After.Values);

        RefusedWith(RefusalCodes.ReferenceWrongList, "lexical/lexExampleSentence/addRefDoNotPublishIn",
            new { member = CanonicalId.FromGuid(NewSetItem("usage-types").Guid).Value }, example.Guid);
    }

    [Fact]
    public void ALexEntryReferenceAddRefusesAnObjectFromAnotherListWithAWrongListCode()
    {
        var usage = NewSetItem("usage-types");
        RefusedWith(RefusalCodes.ReferenceWrongList, "lexical/lexEntry/addRefDoNotPublishIn",
            new { member = CanonicalId.FromGuid(usage.Guid).Value });
        RefusedWith(RefusalCodes.ReferenceWrongList, "lexical/lexEntry/addRefDoNotShowMainEntryIn",
            new { member = CanonicalId.FromGuid(usage.Guid).Value });
        RefusedWith(RefusalCodes.ReferenceWrongList, "lexical/lexEntry/addRefDialectLabels",
            new { member = CanonicalId.FromGuid(_publication.Guid).Value });
    }

    private string Outcome(string kind, object after, Guid? target = null)
    {
        var operation = new OperationEnvelope(CanonicalId.FromGuid(Guid.NewGuid()), kind,
            target: CanonicalId.FromGuid(target ?? _entry.Guid), after: JsonSerializer.SerializeToElement(after));
        try
        {
            ScratchDryRun.Of(_cache, Proposal([operation]));
            return "accepted";
        }
        catch (ProposalRefusalException refusal)
        {
            return refusal.Code;
        }
        catch (InvalidOperationException)
        {
            return "InvalidOperationException";
        }
    }

    /// <summary>One row per (site, verb, member); the settled behaviour is that only a write checks the list, and a remove never does.</summary>
    [Fact]
    public void EveryReferenceSiteChecksListMembershipOnWritesAndNeverOnRemoves()
    {
        NewListItem(StatusList);
        var statusItem = CanonicalId.FromGuid(StatusList().PossibilitiesOS[0].Guid).Value;
        var inList = CanonicalId.FromGuid(UnheldPublication().Guid).Value;
        var otherList = CanonicalId.FromGuid(NewPartOfSpeech()).Value;
        var notAPossibility = CanonicalId.FromGuid(_entry.Guid).Value;
        var unknown = CanonicalId.FromGuid(Guid.NewGuid()).Value;
        var rows = new List<(string Row, string Actual, string Expected)>
        {
            ("stock set, in list", Outcome("lexical/lexSense/setStatus", new { @ref = statusItem }, _sense.Guid), "accepted"),
            ("stock set, other list", Outcome("lexical/lexSense/setStatus", new { @ref = otherList }, _sense.Guid), "reference.wrong-list"),
            ("stock set, wrong class", Outcome("lexical/lexSense/setStatus", new { @ref = notAPossibility }, _sense.Guid), "reference.wrong-class"),
            ("stock add, in list", Outcome("lexical/lexEntry/addRefDoNotPublishIn", new { member = inList }), "accepted"),
            ("stock add, other list", Outcome("lexical/lexEntry/addRefDoNotPublishIn", new { member = otherList }), "reference.wrong-list"),
            ("stock add, wrong class", Outcome("lexical/lexEntry/addRefDoNotPublishIn", new { member = notAPossibility }), "reference.wrong-class"),
            ("stock remove, in list", Outcome("lexical/lexEntry/removeRefDoNotPublishIn", new { member = inList }), "accepted"),
            ("stock remove, other list", Outcome("lexical/lexEntry/removeRefDoNotPublishIn", new { member = otherList }), "accepted"),
            ("stock remove, wrong class", Outcome("lexical/lexEntry/removeRefDoNotPublishIn", new { member = notAPossibility }), "accepted"),
            ("custom set, in list", Outcome("customField/setReference", new { field = "ZedReference", @ref = inList }), "accepted"),
            ("custom set, other list", Outcome("customField/setReference", new { field = "ZedReference", @ref = otherList }), "custom-field.wrong-list"),
            ("custom set, wrong class", Outcome("customField/setReference", new { field = "ZedReference", @ref = notAPossibility }), "custom-field.wrong-class"),
            ("custom add, in list", Outcome("customField/addReference", new { field = "ZedReferences", member = inList }), "accepted"),
            ("custom add, other list", Outcome("customField/addReference", new { field = "ZedReferences", member = otherList }), "custom-field.wrong-list"),
            ("custom add, wrong class", Outcome("customField/addReference", new { field = "ZedReferences", member = notAPossibility }), "custom-field.wrong-class"),
            ("custom remove, in list", Outcome("customField/removeReference", new { field = "ZedReferences", member = inList }), "accepted"),
            ("custom remove, other list", Outcome("customField/removeReference", new { field = "ZedReferences", member = otherList }), "accepted"),
            ("stock set, unknown id", Outcome("lexical/lexSense/setStatus", new { @ref = unknown }, _sense.Guid), "reference.unknown"),
            ("stock add, unknown id", Outcome("lexical/lexEntry/addRefDoNotPublishIn", new { member = unknown }), "reference.unknown"),
            ("stock remove, unknown id", Outcome("lexical/lexEntry/removeRefDoNotPublishIn", new { member = unknown }), "reference.unknown"),
            ("custom set, unknown id", Outcome("customField/setReference", new { field = "ZedReference", @ref = unknown }), "reference.unknown"),
            ("custom add, unknown id", Outcome("customField/addReference", new { field = "ZedReferences", member = unknown }), "reference.unknown"),
            ("custom remove, unknown id", Outcome("customField/removeReference", new { field = "ZedReferences", member = unknown }), "reference.unknown"),
            ("custom remove, wrong class", Outcome("customField/removeReference", new { field = "ZedReferences", member = notAPossibility }), "accepted"),
        };
        Assert.Equal(rows.Select(r => r.Row + " => " + r.Expected), rows.Select(r => r.Row + " => " + r.Actual));
    }

    [Fact]
    public void ARemoveOfAMemberTheFieldDoesNotHoldReportsNoChange()
    {
        var other = CanonicalId.FromGuid(NewPartOfSpeech()).Value;
        var wrongClass = CanonicalId.FromGuid(_sense.Guid).Value;
        foreach (var (kind, after) in new (string, object)[]
        {
            ("lexical/lexEntry/removeRefDoNotPublishIn", new { member = other }),
            ("lexical/lexEntry/removeRefDoNotPublishIn", new { member = wrongClass }),
            ("customField/removeReference", new { field = "ZedReferences", member = other }),
            ("customField/removeReference", new { field = "ZedReferences", member = wrongClass }),
        })
        {
            var operation = new OperationEnvelope(CanonicalId.FromGuid(Guid.NewGuid()), kind,
                target: CanonicalId.FromGuid(_entry.Guid), after: JsonSerializer.SerializeToElement(after));
            var effects = ScratchDryRun.Of(_cache, Proposal([operation])).ExpectedEffects;
            Assert.NotEmpty(effects);
            Assert.All(effects, effect => Assert.Equal(effect.Before.Values, effect.After.Values));
        }
    }
}
