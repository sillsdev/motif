using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class StageRecordImportTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void AnEditedRecordFileIsStagedInADraftWithoutChangingTheProject()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Guid entryId = default, senseId = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.Form.set_String(cache.DefaultVernWs, "zzword");
            var sense = services.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss");
            senseId = sense.Guid;
        });
        new FwDataProjectLoader().Save(cache);

        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var export = exporter.Export(entryId);
        var read = RecordFileReader.Read(export.Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        var senses = (RecordList)read.Document!.Data["senses"]!;
        ((RecordAlternatives)((RecordObject)senses.Items[0])["gloss"]!).Add("en", new RecordScalar("zzedited"));
        var plan = new ProjectRecordImporter(exporter).Plan(RecordFileWriter.Write(read.Document!, schema.Schema));
        Assert.NotEmpty(plan.ChangeSet.Changes);

        var objectsBefore = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Count();
        var modifiedBefore = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entryId).DateModified;
        var fileBefore = SHA256.HashData(File.ReadAllBytes(path));

        const string draft = "record-import";
        var created = ProposalCommands.New(new(path, Version, draft, "Record import"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var staged = ProposalCommands.StageRecordImport(path, draft, schema, plan);
        Assert.True(staged.Succeeded, staged.Refusal?.Message);

        Assert.Empty(staged.Value!.Lowering.Refusals);
        var expected = Assert.Single(staged.Value.Lowering.Operations);
        Assert.Equal("lexical/lexSense/setGloss", expected.Kind);
        Assert.Equal(1, staged.Value.Composed.OperationCount);
        Assert.Equal(draft, staged.Value.Composed.DraftName);

        var shown = ProposalCommands.Show(new(path, Version, created.Value!.ProposalId));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal("Draft", shown.Value!.Status, ignoreCase: true);
        var stored = Assert.Single(shown.Value.Operations);
        Assert.Equal(expected.OperationId.Value, stored.OperationId);
        Assert.Equal(expected.Kind, stored.Kind);
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" },
            CanonicalId.FromGuid(new Guid("11111111-2222-3333-4444-555555555555")), null, staged.Value.Lowering.Operations);
        var digest = IntentDigest.Compute(proposal);
        Assert.False(string.IsNullOrEmpty(digest));
        // The same plan lowered again gives the same operations, so the same digest.
        var again = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.Equal(digest, IntentDigest.Compute(new Proposal(proposal.ContractVersions, proposal.ProposalId, null, again.Operations)));

        // Nothing was applied: the gloss, file, object count and modified time are as they were.
        var sense2 = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(senseId);
        Assert.Equal("zzgloss", sense2.Gloss.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal(objectsBefore, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Count());
        Assert.Equal(modifiedBefore, cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entryId).DateModified);
        Assert.Equal(fileBefore, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public void AnEditedCustomFieldIsStagedAsOneCustomFieldOperationThatReadsBackAndAppliesNothing()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Guid entryId = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            new FieldDescription(cache)
            {
                Class = cache.MetaDataCacheAccessor.GetClassId("LexEntry"), Name = "ZedRank", Type = CellarPropertyType.Integer,
            }.UpdateCustomField();
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var form = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.Form.set_String(cache.DefaultVernWs, "zzword");
        });
        new FwDataProjectLoader().Save(cache);

        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        read.Document!.Data["custom-LexEntry-ZedRank"] = RecordScalar.Of(9);
        var plan = new ProjectRecordImporter(exporter)
            .Plan(RecordFileWriter.Write(read.Document!, schema.Schema), new CatalogWritableFields(schema));
        Assert.Empty(plan.ChangeSet.Refusals);

        var fileBefore = SHA256.HashData(File.ReadAllBytes(path));
        const string draft = "custom-import";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Custom import")).Succeeded);
        var staged = ProposalCommands.StageRecordImport(path, draft, schema, plan);
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Empty(staged.Value!.Lowering.Refusals);
        var operation = Assert.Single(staged.Value.Lowering.Operations);
        Assert.Equal("customField/setInteger", operation.Kind);
        Assert.Equal(1, staged.Value.Composed.OperationCount);

        var dryRun = ScratchDryRun.Of(cache, new Proposal(new Dictionary<string, string> { ["customField"] = "1.0" },
            CanonicalId.FromGuid(new Guid("11111111-2222-3333-4444-555555555555")), null, staged.Value.Lowering.Operations));
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal("customField/ZedRank", effect.Field);
        Assert.Equal("9", effect.After["value"]);
        var flid = cache.MetaDataCacheAccessor.GetFieldId("LexEntry", "ZedRank", true);
        Assert.Equal(0, cache.DomainDataByFlid.get_IntProp(cache.ServiceLocator.GetObject(entryId).Hvo, flid));
        Assert.Equal(fileBefore, SHA256.HashData(File.ReadAllBytes(path)));
    }
}
