using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// A seeded project and one record-file edit per catalog kind of the custom-field, sense status and type, sense
/// reference-set and Publish In families, for tests that drive <c>import</c> through the CLI and the MCP tool.
/// </summary>
public static class ImportFamilyScenarios
{
    /// <summary>The seeded entry, its sense and example, and the list items the edits point at.</summary>
    public sealed record Seeded(Guid Entry, Guid Sense, Guid Example, IReadOnlyDictionary<string, Guid> Items);

    /// <summary>One catalog kind, the project state it needs before the edit, and the edit that lowers to it.</summary>
    public sealed record Case(string Kind, Action<LcmCache, Seeded>? Prepare, Action<RecordDocument, Seeded> Edit);

    private static readonly string[] SetFields =
        ["usage-types", "dialect-labels", "domain-types", "semantic-domains", "anthro-codes", "do-not-publish-in"];

    private static readonly (string Name, CellarPropertyType Type)[] CustomFields =
    [
        ("ZedInteger", CellarPropertyType.Integer), ("ZedMultiUnicode", CellarPropertyType.MultiUnicode),
        ("ZedString", CellarPropertyType.String), ("ZedReference", CellarPropertyType.ReferenceAtomic),
        ("ZedReferences", CellarPropertyType.ReferenceCollection),
    ];

    /// <summary>Every case, set kinds first within each family.</summary>
    public static IReadOnlyList<Case> Cases { get; } = BuildCases();

    /// <summary>The cases whose edit needs nothing prepared, so one file can carry all of them at once.</summary>
    public static IEnumerable<Case> Additive => Cases.Where(item => item.Prepare is null);

    /// <summary>The operation count of a file that applies every <see cref="Additive"/> edit.</summary>
    public static int AdditiveCount => Additive.Count();

    private static string Pascal(string key) => string.Concat(key.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string Custom(string name) => "custom-LexEntry-" + name;

    private static string Link(Guid item) =>
        "[zzitem](/objects/" + item.ToString("D") + "){.name guid=" + item.ToString("D") + "}";

    private static RecordObject SenseAt(RecordDocument document) => (RecordObject)((RecordList)document.Data["senses"]!).Items[0];

    private static RecordObject ExampleAt(RecordDocument document) => (RecordObject)((RecordList)SenseAt(document)["examples"]!).Items[0];

    private static void Do(LcmCache cache, Action action) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, action);

    private static int Hvo(LcmCache cache, Guid guid) => cache.ServiceLocator.GetObject(guid).Hvo;

    private static int Flid(LcmCache cache, string name) => cache.MetaDataCacheAccessor.GetFieldId("LexEntry", name, true);

    private static ILexSense Sense(LcmCache cache, Seeded seeded) =>
        cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(seeded.Sense);

    private static ILexExampleSentence Example(LcmCache cache, Seeded seeded) =>
        cache.ServiceLocator.GetInstance<ILexExampleSentenceRepository>().GetObject(seeded.Example);

    private static IReadOnlyList<Case> BuildCases()
    {
        var cases = new List<Case>();

        cases.Add(new("customField/setString", null, (d, _) => d.Data[Custom("ZedString")] = new RecordScalar("zzstring")));
        cases.Add(new("customField/clearString",
            (c, s) => Do(c, () => c.DomainDataByFlid.SetString(Hvo(c, s.Entry), Flid(c, "ZedString"),
                TsStringUtils.MakeString("zzstringold", c.DefaultAnalWs))),
            (d, _) => d.Data[Custom("ZedString")] = null));
        cases.Add(new("customField/setInteger", null, (d, _) => d.Data[Custom("ZedInteger")] = RecordScalar.Of(7)));
        cases.Add(new("customField/clearInteger",
            (c, s) => Do(c, () => c.DomainDataByFlid.SetInt(Hvo(c, s.Entry), Flid(c, "ZedInteger"), 5)),
            (d, _) => d.Data[Custom("ZedInteger")] = null));
        cases.Add(new("customField/setAlternative", null,
            (d, _) => d.Data[Custom("ZedMultiUnicode")] = new RecordAlternatives().Add("en", new RecordScalar("zzmulti"))));
        cases.Add(new("customField/clearAlternative",
            (c, s) => Do(c, () => c.DomainDataByFlid.SetMultiStringAlt(Hvo(c, s.Entry), Flid(c, "ZedMultiUnicode"), c.DefaultAnalWs,
                TsStringUtils.MakeString("zzold", c.DefaultAnalWs))),
            (d, _) => ((RecordAlternatives)d.Data[Custom("ZedMultiUnicode")]!).Items.Clear()));
        cases.Add(new("customField/setReference", null,
            (d, s) => d.Data[Custom("ZedReference")] = new RecordScalar(Link(s.Items["custom"]))));
        cases.Add(new("customField/clearReference",
            (c, s) => Do(c, () => c.DomainDataByFlid.SetObjProp(Hvo(c, s.Entry), Flid(c, "ZedReference"), Hvo(c, s.Items["custom"]))),
            (d, _) => d.Data[Custom("ZedReference")] = null));
        cases.Add(new("customField/addReference", null,
            (d, s) => d.Data[Custom("ZedReferences")] = new RecordList().Add(new RecordScalar(Link(s.Items["custom"])))));
        cases.Add(new("customField/removeReference",
            (c, s) => Do(c, () => c.DomainDataByFlid.Replace(Hvo(c, s.Entry), Flid(c, "ZedReferences"), 0, 0, [Hvo(c, s.Items["custom"])], 1)),
            (d, _) => ((RecordList)d.Data[Custom("ZedReferences")]!).Items.Clear()));

        foreach (var (key, name) in new[] { ("status", "Status"), ("sense-type", "SenseType") })
        {
            var field = key;
            cases.Add(new("lexical/lexSense/set" + name, null,
                (d, s) => SenseAt(d).Set(field, new RecordScalar(Link(s.Items[field])))));
            cases.Add(new("lexical/lexSense/clear" + name,
                (c, s) => Do(c, () =>
                {
                    var item = (ICmPossibility)c.ServiceLocator.GetObject(s.Items[field]);
                    if (field == "status") Sense(c, s).StatusRA = item;
                    else Sense(c, s).SenseTypeRA = item;
                }),
                (d, _) => SenseAt(d).Fields.RemoveAll(f => f.Key == field)));
        }

        foreach (var key in SetFields)
        {
            var field = key;
            cases.Add(new("lexical/lexSense/addRef" + Pascal(field), null,
                (d, s) => SenseAt(d).Set(field, new RecordList().Add(new RecordScalar(Link(s.Items[field]))))));
            cases.Add(new("lexical/lexSense/removeRef" + Pascal(field),
                (c, s) => Do(c, () => AttachToSense(c, s, field)),
                (d, _) => SenseAt(d).Fields.RemoveAll(f => f.Key == field)));
        }

        cases.Add(new("lexical/lexExampleSentence/addRefDoNotPublishIn", null,
            (d, s) => ExampleAt(d).Set("do-not-publish-in", new RecordList().Add(new RecordScalar(Link(s.Items["example-publish"]))))));
        cases.Add(new("lexical/lexExampleSentence/removeRefDoNotPublishIn",
            (c, s) => Do(c, () => Example(c, s).DoNotPublishInRC.Add((ICmPossibility)c.ServiceLocator.GetObject(s.Items["example-publish"]))),
            (d, _) => ExampleAt(d).Fields.RemoveAll(f => f.Key == "do-not-publish-in")));
        return cases;
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

    /// <summary>Seeds one entry with a sense and an example, custom fields and a list item for every edit, and saves the project.</summary>
    public static Seeded Seed(LcmCache cache)
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

            var entry = services.GetInstance<ILexEntryFactory>().Create();
            var allomorph = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = allomorph;
            allomorph.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("zzword", cache.DefaultVernWs));
            var sense = services.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.Gloss.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("zzgloss", cache.DefaultAnalWs));
            var example = services.GetInstance<ILexExampleSentenceFactory>().Create();
            sense.ExamplesOS.Add(example);
            example.Example.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("zzexample", cache.DefaultVernWs));

            var items = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var key in SetFields.Append("status").Append("sense-type"))
                items[key] = NewItem(cache, key).Guid;
            items["custom"] = NewItem(cache, "do-not-publish-in").Guid;
            items["example-publish"] = NewItem(cache, "do-not-publish-in").Guid;
            seeded = new Seeded(entry.Guid, sense.Guid, example.Guid, items);
        });
        new FwDataProjectLoader().Save(cache);
        return seeded!;
    }

    /// <summary>Returns the path of an exported entry file edited by <paramref name="edit"/>.</summary>
    public static string WriteEditedFile(LcmCache cache, Seeded seeded, string folder, string fileName,
        Action<RecordDocument, Seeded> edit)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(seeded.Entry).Text!, schema.Schema, exporter.CreateReadOptions());
        if (read.HasErrors) throw new InvalidOperationException("The seeded entry did not export to a readable record file.");
        edit(read.Document!, seeded);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, RecordFileWriter.Write(read.Document!, schema.Schema));
        return path;
    }

    /// <summary>Runs every <see cref="Additive"/> edit on one document.</summary>
    public static void EditAllAdditive(RecordDocument document, Seeded seeded)
    {
        foreach (var item in Additive) item.Edit(document, seeded);
    }

    /// <summary>Sets the sense's status to an item of the sense-type list, which no operation may do.</summary>
    public static void EditWrongList(RecordDocument document, Seeded seeded) =>
        SenseAt(document).Set("status", new RecordScalar(Link(seeded.Items["sense-type"])));

    /// <summary>Prepares the project for <paramref name="item"/> and saves it.</summary>
    public static void Prepare(LcmCache cache, Seeded seeded, Case item)
    {
        if (item.Prepare is null) return;
        item.Prepare(cache, seeded);
        new FwDataProjectLoader().Save(cache);
    }
}
