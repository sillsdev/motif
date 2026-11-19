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
                if (writable)
                    Assert.True(catalog.CanWrite(declaring, name), $"{type.Name}.{field.Name}");
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
}
