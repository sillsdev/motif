using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A part of speech's Inflectable Features, edited in its record file: the edit stages, applies as a person, reads back
/// after reopen, and leaves a control part of speech alone. Every registered kind of the field has a case.
/// </summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PartOfSpeechFeaturesImportTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string DraftName = "pos-features";
    private const string Field = "inflectable-feats";
    private const string AddKind = "grammar/partOfSpeech/addRefInflectableFeats";
    private const string RemoveKind = "grammar/partOfSpeech/removeRefInflectableFeats";

    private sealed record Seeded(Guid Pos, Guid Control, Guid Held, Guid Free, Guid Phonological);

    private static void Do(LcmCache cache, Action action) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, action);

    private static string Link(Guid item) =>
        "[zzfeature](/objects/" + item.ToString("D") + "){.name guid=" + item.ToString("D") + "}";

    private static string Members(LcmCache cache, Guid pos) => string.Join(",",
        cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(pos).InflectableFeatsRC
            .Select(feature => feature.Guid.ToString("D")).Order(StringComparer.Ordinal));

    private static Seeded Seed(LcmCache cache)
    {
        Seeded? seeded = null;
        Do(cache, () =>
        {
            var services = cache.ServiceLocator;
            IFsClosedFeature Feature(IFsFeatureSystem system, string name)
            {
                var feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
                system.FeaturesOC.Add(feature);
                feature.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString(name, cache.DefaultAnalWs));
                return feature;
            }

            IPartOfSpeech Pos(string name)
            {
                var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
                cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
                pos.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString(name, cache.DefaultAnalWs));
                return pos;
            }

            var phonological = cache.LangProject.PhFeatureSystemOA ??= services.GetInstance<IFsFeatureSystemFactory>().Create();
            var grammatical = cache.LangProject.MsFeatureSystemOA ??= services.GetInstance<IFsFeatureSystemFactory>().Create();
            var held = Feature(grammatical, "zzheld");
            var free = Feature(grammatical, "zzfree");
            var pos = Pos("zzpos");
            pos.InflectableFeatsRC.Add(held);
            var control = Pos("zzcontrol");
            control.InflectableFeatsRC.Add(held);
            seeded = new Seeded(pos.Guid, control.Guid, held.Guid, free.Guid, Feature(phonological, "zzphon").Guid);
        });
        new FwDataProjectLoader().Save(cache);
        return seeded!;
    }

    private static string ExportText(LcmCache cache, Guid pos)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.ListItemOverlay);
        return new ProjectRecordExporter(cache, schema).Export(pos).Text!;
    }

    private static (ProjectRecordSchemaResult Schema, ProjectRecordExporter Exporter, string File) EditedFile(
        LcmCache cache, Guid pos, Action<RecordList> edit)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.ListItemOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(pos).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        edit((RecordList)read.Document!.Data[Field]!);
        return (schema, exporter, RecordFileWriter.Write(read.Document!, schema.Schema));
    }

    [Theory]
    [InlineData(AddKind)]
    [InlineData(RemoveKind)]
    public void AnImportedFeatureEditAppliesAndReadsBackAndNothingElseChanges(string kind)
    {
        var add = kind == AddKind;
        string path, controlBefore;
        Seeded seeded;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            seeded = Seed(cache);
            controlBefore = ExportText(cache, seeded.Control);
            Assert.Equal(seeded.Held.ToString("D"), Members(cache, seeded.Pos));

            var (schema, exporter, file) = EditedFile(cache, seeded.Pos, list =>
            {
                if (add) list.Add(new RecordScalar(Link(seeded.Free)));
                else list.Items.Clear();
            });
            var plan = new ProjectRecordImporter(exporter).Plan(file, new CatalogWritableFields(schema));
            Assert.Empty(plan.ChangeSet.Refusals);
            Assert.NotEmpty(plan.ChangeSet.Changes);

            Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Record import")).Succeeded);
            var staged = ProposalCommands.StageRecordImport(path, DraftName, schema, plan);
            Assert.True(staged.Succeeded, staged.Refusal?.Message);
            Assert.Empty(staged.Value!.Lowering.Refusals);
            Assert.Equal(kind, Assert.Single(staged.Value.Lowering.Operations).Kind);
        }

        DraftRationale.Author(path, DraftName, "Imported record", "The record file was edited and imported.");
        var finalized = ProposalCommands.Finalize(new(path, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        Assert.True(DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId).Succeeded);
        var applied = ProposalCommands.Apply(new(path, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);

        using var reloaded = new FwDataProjectLoader().LoadCache(path);
        var expected = add
            ? string.Join(",", new[] { seeded.Held, seeded.Free }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal))
            : "";
        Assert.Equal(expected, Members(reloaded, seeded.Pos));
        Assert.Equal(controlBefore, ExportText(reloaded, seeded.Control));
        Assert.Equal(seeded.Held.ToString("D"), Members(reloaded, seeded.Control));
    }

    [Fact]
    public void EveryRegisteredKindOfTheFieldHasACase()
    {
        OperationRegistryBootstrap.Initialize();
        var kinds = OperationHandlerRegistry.RegisteredKinds
            .Where(kind => OperationKindRegistry.IsKnown(kind) && Regex.IsMatch(kind, @"^grammar/partOfSpeech/(addRef|removeRef)InflectableFeats$"))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { AddKind, RemoveKind }, kinds);
        Assert.All(kinds, kind => Assert.Contains(OperationHandlerRegistry.FieldWrites(kind), write => RecordLoweringRules.Lowers(kind, write)));
    }

    [Fact]
    public void ARecordFileEditRunsThroughTheImportCommandAsTheAddKindAndWritesNothing()
    {
        string path, file;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            var seeded = Seed(cache);
            file = Path.Combine(Path.GetDirectoryName(path)!, "pos-features.md");
            File.WriteAllText(file, EditedFile(cache, seeded.Pos, list => list.Add(new RecordScalar(Link(seeded.Free)))).File);
        }
        var before = File.ReadAllBytes(path);

        var outcome = RecordImportCommand.Run(new(path, file));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(AddKind, Assert.Single(outcome.Value!.Operations).Kind);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void AFileMadeBeforeTheFeaturesChangedIsRefusedWholeAsStaleAndMakesNoDraft()
    {
        string path, file;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            var seeded = Seed(cache);
            file = Path.Combine(Path.GetDirectoryName(path)!, "pos-features.md");
            File.WriteAllText(file, EditedFile(cache, seeded.Pos, list => list.Add(new RecordScalar(Link(seeded.Free)))).File);
            Do(cache, () => cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(seeded.Pos).InflectableFeatsRC
                .Add((IFsFeatDefn)cache.ServiceLocator.GetObject(seeded.Free)));
            new FwDataProjectLoader().Save(cache);
        }

        foreach (var draft in new string?[] { null, "import-stale" })
        {
            var outcome = RecordImportCommand.Run(new(path, file, draft));
            Assert.False(outcome.Succeeded);
            Assert.Equal(RefusalCodes.RecordImportPlanRefused, outcome.Refusal!.Code);
            Assert.Equal("stale", outcome.Refusal.Facts["baseVerdict"]);
        }
        var listed = ProposalCommands.List(new(path, Version));
        Assert.True(!listed.Succeeded || listed.Value!.Proposals.Count == 0);
    }

    [Fact]
    public void AnAddAgainstAFeatureListThatMovedOnIsRefusedAsValueStale()
    {
        using var cache = pristine.NewScratch();
        var seeded = Seed(cache);
        var (schema, exporter, file) = EditedFile(cache, seeded.Pos, list => list.Add(new RecordScalar(Link(seeded.Free))));
        var plan = new ProjectRecordImporter(exporter).Plan(file, new CatalogWritableFields(schema));
        Assert.Empty(new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan).Refusals);

        Do(cache, () => cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(seeded.Pos).InflectableFeatsRC
            .Add((IFsFeatDefn)cache.ServiceLocator.GetObject(seeded.Free)));

        var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.Empty(lowering.Operations);
        Assert.Equal(RefusalCodes.RecordImportValueStale, Assert.Single(lowering.Refusals).Code);
    }

    private static Proposal Single(string kind, Guid target, Guid member) => new(
        new Dictionary<string, string> { ["grammar"] = "1.0" },
        CanonicalId.FromGuid(new Guid("11111111-2222-3333-4444-555555555555")), null,
        [new OperationEnvelope(CanonicalId.FromGuid(Guid.NewGuid()), kind, target: CanonicalId.FromGuid(target),
            after: System.Text.Json.JsonSerializer.SerializeToElement(new { member = CanonicalId.FromGuid(member).Value }))]);

    [Fact]
    public void AnAddRefusesAFeatureOfAnotherSystemAndAnObjectOfAnotherClassAndRepeatsChangeNothing()
    {
        using var cache = pristine.NewScratch();
        var seeded = Seed(cache);

        var wrongSystem = Assert.Throws<ReferenceFieldRefusal>(() => ScratchDryRun.Of(cache, Single(AddKind, seeded.Pos, seeded.Phonological)));
        Assert.Equal(RefusalCodes.ReferenceWrongList, wrongSystem.Code);
        var wrongClass = Assert.Throws<ReferenceFieldRefusal>(() => ScratchDryRun.Of(cache, Single(AddKind, seeded.Pos, seeded.Control)));
        Assert.Equal(RefusalCodes.ReferenceWrongClass, wrongClass.Code);

        var repeated = ScratchDryRun.Of(cache, Single(AddKind, seeded.Pos, seeded.Held));
        Assert.All(repeated.ExpectedEffects, effect => Assert.Equal(effect.Before.Values, effect.After.Values));
        var absent = ScratchDryRun.Of(cache, Single(RemoveKind, seeded.Pos, seeded.Free));
        Assert.All(absent.ExpectedEffects, effect => Assert.Equal(effect.Before.Values, effect.After.Values));
    }
}
