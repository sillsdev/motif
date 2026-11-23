using System;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    /// <summary>One test per reason the exporter gives for a value it leaves out; <see cref="ExportReasonGuardTests"/> fails when a reason has none.</summary>
    public class ProjectRecordExportReasonTests
    {
        static ProjectRecordExport ExportNotebookRecord(ProjectFixture fixture, Action<IRnGenericRec, IStTxtPara> shape)
        {
            var cache = fixture.Cache;
            IRnGenericRec record = null!;
            fixture.Change(() =>
            {
                var notebook = cache.LangProject.ResearchNotebookOA ?? (cache.LangProject.ResearchNotebookOA = cache.ServiceLocator.GetInstance<IRnResearchNbkFactory>().Create());
                record = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create();
                notebook.RecordsOC.Add(record);
                record.ConclusionsOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                var para = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                record.ConclusionsOA.ParagraphsOS.Add(para);
                para.Contents = TsStringUtils.MakeString("some words", cache.DefaultAnalWs);
                shape(record, para);
            });
            var exporter = new ProjectRecordExporter(cache, ProjectRecordSchema.Generate(cache, ProjectRecordSchema.NotebookRecordOverlay));
            return exporter.Export(record.Guid);
        }

        [Fact]
        [ExportReason(nameof(ExportReasons.UndefinedParagraphStyle))]
        public void AParagraphStyleTheProjectLacksIsReportedAndTheParagraphIsExportedInNormal()
        {
            using var fixture = new ProjectFixture();
            var export = ExportNotebookRecord(fixture, (record, para) => para.StyleRules = TsStringUtils.MakeProps("No Such Style", fixture.Cache.DefaultAnalWs));
            var lost = Assert.Single(export.Unexported, u => u.Reason.Contains(ExportReasons.UndefinedParagraphStyle));
            Assert.Equal("No Such Style", lost.Value);
            Assert.Equal("Conclusions", lost.Field);
        }

        [Fact]
        [ExportReason(nameof(ExportReasons.StTextProperty))]
        public void AStructuredTextPropertyTheFileCannotHoldIsReported()
        {
            using var fixture = new ProjectFixture();
            var export = ExportNotebookRecord(fixture, (record, para) => record.ConclusionsOA.RightToLeft = true);
            var lost = Assert.Single(export.Unexported, u => u.Reason.Contains(ExportReasons.StTextProperty));
            Assert.Equal("RightToLeft", lost.Field);
        }

        [Fact]
        [ExportReason(nameof(ExportReasons.ParagraphAnalysis))]
        public void AParagraphPropertyTheFileCannotHoldIsReported()
        {
            using var fixture = new ProjectFixture();
            var export = ExportNotebookRecord(fixture, (record, para) => para.ParseIsCurrent = true);
            var lost = Assert.Single(export.Unexported, u => u.Reason.Contains(ExportReasons.ParagraphAnalysis));
            Assert.Equal("ParseIsCurrent", lost.Field);
        }

        [Fact]
        [ExportReason(nameof(ExportReasons.OwnedObjects))]
        public void ACustomOwningFieldWhoseClassTheOverlayDoesNotDeclareIsReported()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
            int destination = metadata.GetClassId("CmPossibility");
            int field = metadata.AddCustomField("LexEntry", "Holder", CellarPropertyType.OwningAtomic, destination);
            ILexEntry entry = null!;
            fixture.Change(() =>
            {
                entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                cache.DomainDataByFlid.MakeNewObject(destination, entry.Hvo, field, -2);
            });
            var result = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
            Assert.Contains(result.OmittedCustomFields, o => o.Field == "Holder");
            var export = new ProjectRecordExporter(cache, result).Export(entry.Guid);
            var lost = Assert.Single(export.Unexported, u => u.Field == "Holder");
            Assert.Contains(ExportReasons.OwnedObjects, lost.Reason);
        }
    }
}
