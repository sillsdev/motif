using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
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

    /// <summary>Why a covered pair is not offered: the Decision A families, each with its reason.</summary>
    private static readonly Dictionary<string, string> DecisionA = new()
    {
        ["bespoke-payload"] = "the kind's payload carries more than the verb and field (digests, paragraph keys, a census), so only its own composer authors it",
        ["create-composer"] = "the create kind takes its own payload (morph type, reference, forms), which only its composer authors",
        ["delete-composer"] = "the delete is built from the project by its own composer, which the lowerer calls only for senses",
    };

    private static string? DecisionAFamily(RecordWriteVerb verb, FieldWriteCapability write) => verb switch
    {
        RecordWriteVerb.AddObject => "create-composer",
        RecordWriteVerb.RemoveObject => "delete-composer",
        _ when write.Payload == FieldWritePayload.Bespoke => "bespoke-payload",
        _ => null,
    };

    private static IEnumerable<(string Kind, FieldWriteCapability Write)> Declarations(
        (string DeclaringClass, string Field, bool IsCustom) binding, RecordWriteVerb verb) =>
        OperationHandlerRegistry.RegisteredKinds.SelectMany(kind => OperationHandlerRegistry.FieldWrites(kind)
            .Where(write => write.DeclaringClass == binding.DeclaringClass && write.Field == binding.Field && write.Verb == verb)
            .Select(write => (kind, write)));

    private static readonly RecordWriteVerb[] WriteVerbs =
    [
        RecordWriteVerb.Set, RecordWriteVerb.Clear, RecordWriteVerb.SetAlternative, RecordWriteVerb.ClearAlternative,
        RecordWriteVerb.SetPlainRich, RecordWriteVerb.SetRich, RecordWriteVerb.ClearRich, RecordWriteVerb.MoveObject,
        RecordWriteVerb.AddReference, RecordWriteVerb.InsertReference, RecordWriteVerb.RemoveReference, RecordWriteVerb.MoveReference,
    ];

    [Fact]
    public void CatalogWritableFieldsAgreesExactlyWithPhaseThreesCoverage()
    {
        var catalog = new CatalogWritableFields();
        var resolver = new RecordWritableFields(_schema);
        var failures = new List<string>();
        var familiesSeen = new HashSet<string>();
        // Every (declaring class, field) phase 3 covers on some verb, gathered across the schema types that bind to it.
        var covered = resolver.Entries.Where(entry => entry.Value is WritableFieldResolution.Covered)
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
                    var family = declared.Select(d => DecisionAFamily(verb, d.Write)).FirstOrDefault(f => f is not null);
                    if (family is null)
                        failures.Add($"{subject}: not offered, and no Decision A family explains it.");
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
                if (_schema.FieldBinding(type.Name, field.Name) is not { } binding ||
                    !resolver.Entries.Any(e => e.Key.Class == type.Name && e.Key.Field == field.Name))
                    continue;
                if (catalog.CanWrite(binding.DeclaringClass, binding.Field) &&
                    !WriteVerbs.Any(verb => resolver.Entries.TryGetValue((type.Name, field.Name, verb), out var r) &&
                        r is WritableFieldResolution.Covered && Declarations(binding, verb == RecordWriteVerb.InsertReference ? RecordWriteVerb.AddReference : verb)
                            .Any(d => RecordLoweringRules.Lowers(d.Kind, d.Write))))
                    failures.Add($"{type.Name}.{field.Name}: the adapter offers a write phase 3 does not cover and the lowerer does not lower.");
            }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Distinct()));
        Assert.All(familiesSeen, family => Assert.Contains(family, DecisionA.Keys));
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
    private const int UnexercisedAbstractReferencePairs = 22;
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
        var catalog = new CatalogWritableFields();
        var resolver = new RecordWritableFields(_schema);
        var lowerer = new RecordChangeSetLowerer(_schema, resolver);
        var failures = new List<string>();
        var gaps = new SortedSet<string>(StringComparer.Ordinal);
        var lowered = 0;
        var skipped = 0;
        var unexercised = new List<string>();
        foreach (var ((typeName, fieldName, verb), resolution) in resolver.Entries)
        {
            if (resolution is not WritableFieldResolution.Covered)
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
                    if (!OperationHandlerRegistry.FieldWrites(operation.Kind).Any(write =>
                            write.DeclaringClass == binding.DeclaringClass && write.Field == binding.Field))
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
        var catalog = new CatalogWritableFields();
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
            if (resolution is not WritableFieldResolution.Covered || _schema.Schema.ObjectType(typeName)?.Field(fieldName) is not { } field
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
}
