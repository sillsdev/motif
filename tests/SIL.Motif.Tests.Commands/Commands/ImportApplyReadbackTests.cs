using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// An imported record file becomes a Draft, a person Applies it, and the reopened project holds exactly the edit.
/// One case per catalog kind of the custom-field, sense status and type, sense reference-set and Publish In families;
/// the kind list comes from the Runner's registry, so a new kind with no case fails
/// <see cref="EveryCatalogKindOfTheFamiliesHasExactlyOneCase"/>.
/// </summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ImportApplyReadbackTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string DraftName = "readback";

    /// <summary>Kinds whose case is known to fail, with the reason; the case then must fail, so a fix turns it red until the entry is removed.</summary>
    private static readonly Dictionary<string, string> KnownFailures = new(StringComparer.Ordinal);

    private sealed record Seeded(Guid Entry, Guid Control, Guid Sense, Guid Example, IReadOnlyDictionary<string, Guid> Items);

    private sealed record Case(string Kind, Action<LcmCache, Seeded>? Prepare, Action<RecordDocument, Seeded> Edit,
        Func<LcmCache, Seeded, string> Read, Func<Seeded, string> Before, Func<Seeded, string> After);

    private static readonly string[] SetFields =
        ["usage-types", "dialect-labels", "domain-types", "semantic-domains", "anthro-codes", "do-not-publish-in"];

    private static string Pascal(string key) => string.Concat(key.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string Custom(string name) => "custom-LexEntry-" + name;

    private static string Link(Guid item) =>
        "[zzitem](/objects/" + item.ToString("D") + "){.name guid=" + item.ToString("D") + "}";

    private static RecordObject SenseAt(RecordDocument d) => (RecordObject)((RecordList)d.Data["senses"]!).Items[0];

    private static RecordObject ExampleAt(RecordDocument d) => (RecordObject)((RecordList)SenseAt(d)["examples"]!).Items[0];

    private static readonly Case[] Cases = BuildCases();

    private static Case[] BuildCases()
    {
        var cases = new List<Case>();

        // --- custom fields: one set and one clear kind for each shape FieldWorks writes ---
        cases.Add(new("customField/setString", null,
            (d, _) => d.Data[Custom("ZedString")] = new RecordScalar("zzstring"),
            (c, s) => CustomText(c, s, "ZedString"), _ => "", _ => "zzstring"));
        cases.Add(new("customField/clearString",
            (c, s) => Do(c, () => c.DomainDataByFlid.SetString(Hvo(c, s.Entry), Flid(c, "ZedString"),
                TsStringUtils.MakeString("zzstringold", c.DefaultAnalWs))),
            (d, _) => d.Data[Custom("ZedString")] = null,
            (c, s) => CustomText(c, s, "ZedString"), _ => "zzstringold", _ => ""));
        cases.Add(new("customField/setInteger", null,
            (d, _) => d.Data[Custom("ZedInteger")] = RecordScalar.Of(7),
            (c, s) => c.DomainDataByFlid.get_IntProp(Hvo(c, s.Entry), Flid(c, "ZedInteger")).ToString(), _ => "0", _ => "7"));
        cases.Add(new("customField/clearInteger",
            (c, s) => Do(c, () => c.DomainDataByFlid.SetInt(Hvo(c, s.Entry), Flid(c, "ZedInteger"), 5)),
            (d, _) => d.Data[Custom("ZedInteger")] = null,
            (c, s) => c.DomainDataByFlid.get_IntProp(Hvo(c, s.Entry), Flid(c, "ZedInteger")).ToString(), _ => "5", _ => "0"));
        cases.Add(new("customField/setAlternative", null,
            (d, _) => d.Data[Custom("ZedMultiUnicode")] = new RecordAlternatives().Add("en", new RecordScalar("zzmulti")),
            (c, s) => CustomAlternative(c, s, "ZedMultiUnicode"), _ => "", _ => "zzmulti"));
        cases.Add(new("customField/clearAlternative",
            (c, s) => Do(c, () => c.DomainDataByFlid.SetMultiStringAlt(Hvo(c, s.Entry), Flid(c, "ZedMultiUnicode"), c.DefaultAnalWs,
                TsStringUtils.MakeString("zzold", c.DefaultAnalWs))),
            (d, _) => ((RecordAlternatives)d.Data[Custom("ZedMultiUnicode")]!).Items.Clear(),
            (c, s) => CustomAlternative(c, s, "ZedMultiUnicode"), _ => "zzold", _ => ""));
        cases.Add(new("customField/setReference", null,
            (d, s) => d.Data[Custom("ZedReference")] = new RecordScalar(Link(s.Items["custom"])),
            (c, s) => CustomAtomic(c, s, "ZedReference"), _ => "", s => G(s.Items["custom"])));
        cases.Add(new("customField/clearReference",
            (c, s) => Do(c, () => c.DomainDataByFlid.SetObjProp(Hvo(c, s.Entry), Flid(c, "ZedReference"), Hvo(c, s.Items["custom"]))),
            (d, _) => d.Data[Custom("ZedReference")] = null,
            (c, s) => CustomAtomic(c, s, "ZedReference"), s => G(s.Items["custom"]), _ => ""));
        cases.Add(new("customField/addReference", null,
            (d, s) => d.Data[Custom("ZedReferences")] = new RecordList().Add(new RecordScalar(Link(s.Items["custom"]))),
            (c, s) => CustomCollection(c, s, "ZedReferences"), _ => "", s => G(s.Items["custom"])));
        cases.Add(new("customField/removeReference",
            (c, s) => Do(c, () => c.DomainDataByFlid.Replace(Hvo(c, s.Entry), Flid(c, "ZedReferences"), 0, 0, [Hvo(c, s.Items["custom"])], 1)),
            (d, _) => ((RecordList)d.Data[Custom("ZedReferences")]!).Items.Clear(),
            (c, s) => CustomCollection(c, s, "ZedReferences"), s => G(s.Items["custom"]), _ => ""));

        // --- sense status and sense type ---
        foreach (var (key, name) in new[] { ("status", "Status"), ("sense-type", "SenseType") })
        {
            Func<LcmCache, Seeded, string> read = (c, s) => key == "status"
                ? Guid(Sense(c, s).StatusRA) : Guid(Sense(c, s).SenseTypeRA);
            Action<LcmCache, Seeded> attach = (c, s) => Do(c, () =>
            {
                if (key == "status") Sense(c, s).StatusRA = (ICmPossibility)c.ServiceLocator.GetObject(s.Items[key]);
                else Sense(c, s).SenseTypeRA = (ICmPossibility)c.ServiceLocator.GetObject(s.Items[key]);
            });
            cases.Add(new("lexical/lexSense/set" + name, null,
                (d, s) => SenseAt(d).Set(key, new RecordScalar(Link(s.Items[key]))), read, _ => "", s => G(s.Items[key])));
            cases.Add(new("lexical/lexSense/clear" + name, attach,
                (d, _) => SenseAt(d).Fields.RemoveAll(f => f.Key == key), read, s => G(s.Items[key]), _ => ""));
        }

        // --- sense reference sets, including Publish In ---
        foreach (var key in SetFields)
        {
            var field = key;
            Func<LcmCache, Seeded, string> read = (c, s) => SenseMembers(Sense(c, s), field);
            cases.Add(new("lexical/lexSense/addRef" + Pascal(field), null,
                (d, s) => SenseAt(d).Set(field, new RecordList().Add(new RecordScalar(Link(s.Items[field])))),
                read, _ => "", s => G(s.Items[field])));
            cases.Add(new("lexical/lexSense/removeRef" + Pascal(field),
                (c, s) => Do(c, () => AttachToSense(c, s, field)),
                (d, _) => SenseAt(d).Fields.RemoveAll(f => f.Key == field),
                read, s => G(s.Items[field]), _ => ""));
        }

        // --- Publish In on an example sentence ---
        Func<LcmCache, Seeded, string> readExample = (c, s) =>
            string.Join(",", c.ServiceLocator.GetInstance<ILexExampleSentenceRepository>().GetObject(s.Example)
                .DoNotPublishInRC.Select(item => G(item.Guid)).Order(StringComparer.Ordinal));
        cases.Add(new("lexical/lexExampleSentence/addRefDoNotPublishIn", null,
            (d, s) => ExampleAt(d).Set("do-not-publish-in", new RecordList().Add(new RecordScalar(Link(s.Items["example-publish"])))),
            readExample, _ => "", s => G(s.Items["example-publish"])));
        cases.Add(new("lexical/lexExampleSentence/removeRefDoNotPublishIn",
            (c, s) => Do(c, () => c.ServiceLocator.GetInstance<ILexExampleSentenceRepository>().GetObject(s.Example)
                .DoNotPublishInRC.Add((ICmPossibility)c.ServiceLocator.GetObject(s.Items["example-publish"]))),
            (d, _) => ExampleAt(d).Fields.RemoveAll(f => f.Key == "do-not-publish-in"),
            readExample, s => G(s.Items["example-publish"]), _ => ""));
        return [.. cases];
    }

    // ---------------------------------------------------------------- reading the project back the way FieldWorks would

    private static string G(Guid guid) => guid.ToString("D");

    private static string Guid(ICmObject? item) => item is null ? "" : G(item.Guid);

    private static void Do(LcmCache cache, Action action) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, action);

    private static int Hvo(LcmCache cache, Guid guid) => cache.ServiceLocator.GetObject(guid).Hvo;

    private static int Flid(LcmCache cache, string name) => cache.MetaDataCacheAccessor.GetFieldId("LexEntry", name, true);

    private static ILexSense Sense(LcmCache cache, Seeded seeded) =>
        cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(seeded.Sense);

    private static string CustomText(LcmCache cache, Seeded seeded, string name) =>
        cache.DomainDataByFlid.get_StringProp(Hvo(cache, seeded.Entry), Flid(cache, name))?.Text ?? "";

    private static string CustomAlternative(LcmCache cache, Seeded seeded, string name) =>
        cache.DomainDataByFlid.get_MultiStringAlt(Hvo(cache, seeded.Entry), Flid(cache, name), cache.DefaultAnalWs)?.Text ?? "";

    private static string CustomAtomic(LcmCache cache, Seeded seeded, string name)
    {
        var hvo = cache.DomainDataByFlid.get_ObjectProp(Hvo(cache, seeded.Entry), Flid(cache, name));
        return hvo == 0 ? "" : G(cache.ServiceLocator.GetObject(hvo).Guid);
    }

    private static string CustomCollection(LcmCache cache, Seeded seeded, string name)
    {
        var data = cache.DomainDataByFlid;
        var entry = Hvo(cache, seeded.Entry);
        var flid = Flid(cache, name);
        return string.Join(",", Enumerable.Range(0, data.get_VecSize(entry, flid))
            .Select(index => G(cache.ServiceLocator.GetObject(data.get_VecItem(entry, flid, index)).Guid)).Order(StringComparer.Ordinal));
    }

    private static string SenseMembers(ILexSense sense, string field)
    {
        IEnumerable<ICmObject> members = field switch
        {
            "usage-types" => sense.UsageTypesRC,
            "dialect-labels" => sense.DialectLabelsRS,
            "domain-types" => sense.DomainTypesRC,
            "semantic-domains" => sense.SemanticDomainsRC,
            "anthro-codes" => sense.AnthroCodesRC,
            "do-not-publish-in" => sense.DoNotPublishInRC,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        return string.Join(",", members.Select(member => G(member.Guid)).Order(StringComparer.Ordinal));
    }

    private static void AttachToSense(LcmCache cache, Seeded seeded, string field)
    {
        var sense = Sense(cache, seeded);
        var item = cache.ServiceLocator.GetObject(seeded.Items[field]);
        switch (field)
        {
            case "usage-types": sense.UsageTypesRC.Add((ICmPossibility)item); break;
            case "dialect-labels": sense.DialectLabelsRS.Add((ICmPossibility)item); break;
            case "domain-types": sense.DomainTypesRC.Add((ICmPossibility)item); break;
            case "semantic-domains": sense.SemanticDomainsRC.Add((ICmSemanticDomain)item); break;
            case "anthro-codes": sense.AnthroCodesRC.Add((ICmAnthroItem)item); break;
            case "do-not-publish-in": sense.DoNotPublishInRC.Add((ICmPossibility)item); break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    // ---------------------------------------------------------------- the seeded project

    private static readonly (string Name, CellarPropertyType Type)[] CustomFields =
    [
        ("ZedInteger", CellarPropertyType.Integer), ("ZedMultiUnicode", CellarPropertyType.MultiUnicode),
        ("ZedString", CellarPropertyType.String), ("ZedReference", CellarPropertyType.ReferenceAtomic),
        ("ZedReferences", CellarPropertyType.ReferenceCollection),
    ];

    private static ICmPossibilityList EnsureList(LcmCache cache, string key)
    {
        var lists = cache.LangProject;
        ICmPossibilityList Fresh() => cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
        return key switch
        {
            "status" => lists.StatusOA ??= Fresh(),
            "sense-type" => lists.LexDbOA.SenseTypesOA ??= Fresh(),
            "usage-types" => lists.LexDbOA.UsageTypesOA ??= Fresh(),
            "dialect-labels" => lists.LexDbOA.DialectLabelsOA ??= Fresh(),
            "domain-types" => lists.LexDbOA.DomainTypesOA ??= Fresh(),
            "semantic-domains" => lists.SemanticDomainListOA ??= Fresh(),
            "anthro-codes" => lists.AnthroListOA ??= Fresh(),
            _ => lists.LexDbOA.PublicationTypesOA ??= Fresh(),
        };
    }

    private static ICmPossibility NewItem(LcmCache cache, string listKey)
    {
        var list = EnsureList(cache, listKey);
        ICmPossibility made = listKey switch
        {
            "semantic-domains" => cache.ServiceLocator.GetInstance<ICmSemanticDomainFactory>().Create(),
            "anthro-codes" => cache.ServiceLocator.GetInstance<ICmAnthroItemFactory>().Create(),
            _ => cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create(),
        };
        list.PossibilitiesOS.Add(made);
        made.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("zzitem", cache.DefaultAnalWs));
        return made;
    }

    /// <summary>One edited entry with a sense and an example, a control entry the import never touches, the custom fields and one list item for every list a case writes.</summary>
    private static Seeded Seed(LcmCache cache)
    {
        Seeded? seeded = null;
        Do(cache, () =>
        {
            var services = cache.ServiceLocator;
            var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
            var possibility = metadata.GetClassId("CmPossibility");
            var publicationList = EnsureList(cache, "do-not-publish-in");
            foreach (var (name, type) in CustomFields)
            {
                var reference = type is CellarPropertyType.ReferenceAtomic or CellarPropertyType.ReferenceCollection;
                metadata.AddCustomField("LexEntry", name, type, reference ? possibility : 0, string.Empty,
                    name == "ZedString" ? WritingSystemServices.kwsAnal : 0, reference ? publicationList.Guid : System.Guid.Empty);
            }

            ILexEntry MakeEntry(string form, string gloss, out ILexSense sense)
            {
                var entry = services.GetInstance<ILexEntryFactory>().Create();
                var allomorph = services.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = allomorph;
                allomorph.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString(form, cache.DefaultVernWs));
                sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString(gloss, cache.DefaultAnalWs));
                return entry;
            }

            var entry = MakeEntry("zzword", "zzgloss", out var sense);
            var example = services.GetInstance<ILexExampleSentenceFactory>().Create();
            sense.ExamplesOS.Add(example);
            example.Example.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("zzexample", cache.DefaultVernWs));
            var control = MakeEntry("zzcontrol", "zzcontrolgloss", out _);

            var items = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var key in SetFields.Append("status").Append("sense-type"))
                items[key] = NewItem(cache, key).Guid;
            items["custom"] = NewItem(cache, "do-not-publish-in").Guid;
            items["example-publish"] = NewItem(cache, "do-not-publish-in").Guid;
            seeded = new Seeded(entry.Guid, control.Guid, sense.Guid, example.Guid, items);
        });
        new FwDataProjectLoader().Save(cache);
        return seeded!;
    }

    private static string ExportText(LcmCache cache, Guid entry)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        return new ProjectRecordExporter(cache, schema).Export(entry).Text!;
    }

    private static HashSet<(Guid, string)> Objects(LcmCache cache) => cache.ServiceLocator.GetInstance<ICmObjectRepository>()
        .AllInstances().Select(item => (item.Guid, item.ClassName)).ToHashSet();

    // ---------------------------------------------------------------- the kinds the catalog offers

    /// <summary>The kinds of the three families, from the Runner's registry: those the record lowerer authors for a custom field, a sense or an example sentence.</summary>
    private static string[] CatalogKinds()
    {
        OperationRegistryBootstrap.Initialize();
        var family = new Regex(@"^(customField/.+|lexical/(lexSense|lexExampleSentence)/(setStatus|clearStatus|setSenseType|clearSenseType|addRef\w+|removeRef\w+))$");
        return OperationHandlerRegistry.RegisteredKinds
            .Where(kind => OperationKindRegistry.IsKnown(kind) && family.IsMatch(kind))
            .Where(kind => OperationHandlerRegistry.FieldWrites(kind).Any(write => RecordLoweringRules.Lowers(kind, write)))
            .Order(StringComparer.Ordinal).ToArray();
    }

    public static TheoryData<string> CaseKinds()
    {
        var data = new TheoryData<string>();
        foreach (var kind in Cases.Select(c => c.Kind).Order(StringComparer.Ordinal)) data.Add(kind);
        return data;
    }

    [Fact]
    public void EveryCatalogKindOfTheFamiliesHasExactlyOneCase()
    {
        var kinds = CatalogKinds();
        Assert.Empty(kinds.Except(Cases.Select(c => c.Kind)).Order());
        Assert.Empty(Cases.Select(c => c.Kind).Except(kinds).Order());
        Assert.Equal(Cases.Length, Cases.Select(c => c.Kind).Distinct().Count());
        Assert.All(KnownFailures.Keys, kind => Assert.Contains(kind, kinds));
    }

    // ---------------------------------------------------------------- stage, Apply as a person, reopen, read back

    private static string FinalizeAndApply(string path)
    {
        DraftRationale.Author(path, DraftName, "Imported record", "The record file was edited and imported.");
        var finalized = ProposalCommands.Finalize(new(path, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(path, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        return finalized.Value.ProposalId;
    }

    [Theory]
    [MemberData(nameof(CaseKinds))]
    public void AnImportedEditAppliesAndReadsBackAndNothingElseChanges(string kind)
    {
        if (KnownFailures.TryGetValue(kind, out var reason))
        {
            Assert.ThrowsAny<Exception>(() => ApplyAndReadBack(kind));
            Assert.False(string.IsNullOrWhiteSpace(reason));
            return;
        }
        ApplyAndReadBack(kind);
    }

    private void ApplyAndReadBack(string kind)
    {
        var item = Assert.Single(Cases, c => c.Kind == kind);
        string path, edited, controlBefore;
        HashSet<(Guid, string)> objectsBefore;
        Seeded seeded;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            seeded = Seed(cache);
            if (item.Prepare is not null)
            {
                item.Prepare(cache, seeded);
                new FwDataProjectLoader().Save(cache);
            }
            Assert.Equal(item.Before(seeded), item.Read(cache, seeded));
            controlBefore = ExportText(cache, seeded.Control);
            objectsBefore = Objects(cache);

            var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
            var exporter = new ProjectRecordExporter(cache, schema);
            var read = RecordFileReader.Read(exporter.Export(seeded.Entry).Text!, schema.Schema, exporter.CreateReadOptions());
            Assert.False(read.HasErrors);
            item.Edit(read.Document!, seeded);
            edited = RecordFileWriter.Write(read.Document!, schema.Schema);
            var plan = new ProjectRecordImporter(exporter).Plan(edited, new CatalogWritableFields(schema));
            Assert.Empty(plan.ChangeSet.Refusals);
            Assert.NotEmpty(plan.ChangeSet.Changes);

            Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Record import")).Succeeded);
            var staged = ProposalCommands.StageRecordImport(path, DraftName, schema, plan);
            Assert.True(staged.Succeeded, staged.Refusal?.Message);
            Assert.Empty(staged.Value!.Lowering.Refusals);
            Assert.Equal(kind, Assert.Single(staged.Value.Lowering.Operations).Kind);
        }

        FinalizeAndApply(path);

        using var reloaded = new FwDataProjectLoader().LoadCache(path);
        Assert.Equal(item.After(seeded), item.Read(reloaded, seeded));
        Assert.Equal(controlBefore, ExportText(reloaded, seeded.Control));
        var objectsAfter = Objects(reloaded);
        Assert.Empty(objectsBefore.Except(objectsAfter));
        Assert.DoesNotContain(objectsAfter.Except(objectsBefore), o => o.Item2 is "LexEntry" or "LexSense" or "LexExampleSentence" or "CmPossibility");

        // The record as the project now exports it plans no change, and the file already applied plans none either.
        var schemaAfter = ProjectRecordSchema.Generate(reloaded, ProjectRecordSchema.LexiconEntryOverlay);
        var importer = new ProjectRecordImporter(new ProjectRecordExporter(reloaded, schemaAfter));
        var catalog = new CatalogWritableFields(schemaAfter);
        Assert.True(importer.Plan(ExportText(reloaded, seeded.Entry), catalog).IsEmpty);
        var again = importer.Plan(edited, catalog);
        Assert.Empty(again.ChangeSet.Changes);
        Assert.NotEqual(ProjectRecordBaseVerdict.Fresh, again.BaseVerdict);
    }

    // ---------------------------------------------------------------- a failure leaves the project as it was

    private static BoundDryRunAnchor AnchorFor(LcmCache cache, Proposal proposal) => new(
        IntentDigest: IntentDigest.Compute(proposal),
        FootprintDigest: FootprintProbe.ComputeCurrentFootprintDigest(cache, proposal),
        EffectDigest: "sha256:" + new string('0', 64),
        RunnerVersion: "test",
        LibLcmVersion: "test",
        ProjectionVersion: SnapshotFields.ProjectionVersion,
        DryRunAtUtc: "20260101T000000Z");

    private static string Gloss(LcmCache cache, Seeded seeded) =>
        Sense(cache, seeded).Gloss.get_String(cache.DefaultAnalWs).Text;

    [Fact]
    public void ARunnerFailureAfterTheImportedOperationRollsTheWholeProposalBack()
    {
        using var cache = pristine.NewScratch();
        var seeded = Seed(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(seeded.Entry).Text!, schema.Schema, exporter.CreateReadOptions());
        ((RecordAlternatives)((RecordObject)((RecordList)read.Document!.Data["senses"]!).Items[0])["gloss"]!)
            .Add("en", new RecordScalar("zzedited"));
        var plan = new ProjectRecordImporter(exporter)
            .Plan(RecordFileWriter.Write(read.Document!, schema.Schema), new CatalogWritableFields(schema));
        var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        var imported = Assert.Single(lowering.Operations);
        // The second operation's payload has no text, so it throws once the committing loop reads it.
        using var malformed = JsonDocument.Parse("{\"ws\":\"en\"}");
        var failing = new OperationEnvelope(CanonicalId.Mint(), imported.Kind, imported.Target, after: malformed.RootElement.Clone());
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, [imported, failing]);

        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(cache, proposal, AnchorFor(cache, proposal), "motif-tests"));

        Assert.Equal("zzgloss", Gloss(cache, seeded));
        Assert.Empty(ProjectAppliedLog.ReadAll(cache));
    }

    [Fact]
    public void AStagedImportIsRefusedAtApplyWhenTheProjectMovedAfterTheDryRunAndChangesNothing()
    {
        string path;
        Seeded seeded;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            seeded = Seed(cache);
            var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
            var exporter = new ProjectRecordExporter(cache, schema);
            var read = RecordFileReader.Read(exporter.Export(seeded.Entry).Text!, schema.Schema, exporter.CreateReadOptions());
            ((RecordAlternatives)((RecordObject)((RecordList)read.Document!.Data["senses"]!).Items[0])["gloss"]!)
                .Add("en", new RecordScalar("zzedited"));
            var plan = new ProjectRecordImporter(exporter)
                .Plan(RecordFileWriter.Write(read.Document!, schema.Schema), new CatalogWritableFields(schema));
            Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Record import")).Succeeded);
            Assert.True(ProposalCommands.StageRecordImport(path, DraftName, schema, plan).Succeeded);
        }

        DraftRationale.Author(path, DraftName, "Imported record", "The record file was edited and imported.");
        var finalized = ProposalCommands.Finalize(new(path, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        Assert.True(DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId).Succeeded);

        using (var drifting = new FwDataProjectLoader().LoadCache(path))
        {
            Do(drifting, () => Sense(drifting, seeded).Gloss.set_String(drifting.DefaultAnalWs,
                TsStringUtils.MakeString("zzdrifted", drifting.DefaultAnalWs)));
            new FwDataProjectLoader().Save(drifting);
        }

        var applied = ProposalCommands.Apply(new(path, Version, finalized.Value.ProposalId));
        Assert.False(applied.Succeeded);

        using var reloaded = new FwDataProjectLoader().LoadCache(path);
        Assert.Equal("zzdrifted", Gloss(reloaded, seeded));
        Assert.Empty(ProjectAppliedLog.ReadAll(reloaded));
    }
}
