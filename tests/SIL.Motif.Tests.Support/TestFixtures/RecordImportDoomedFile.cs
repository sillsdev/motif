using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>A saved project and an import file with one good change and one doomed change.</summary>
public static class RecordImportDoomedFile
{
    /// <summary>Edits the first gloss and sets the last status to a sense-type member.</summary>
    public static (string Project, string File) Create(PristineProjectFixture pristine, string folder)
    {
        Directory.CreateDirectory(folder);
        using var cache = pristine.NewScratch();
        Guid entryId = default, typeItem = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.Form.set_String(cache.DefaultVernWs, "zzword");
            for (var index = 0; index < 2; index++)
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss" + index);
            }
            var status = cache.LangProject.StatusOA ??= services.GetInstance<ICmPossibilityListFactory>().Create();
            status.PossibilitiesOS.Add(services.GetInstance<ICmPossibilityFactory>().Create());
            var types = cache.LangProject.LexDbOA.SenseTypesOA ??= services.GetInstance<ICmPossibilityListFactory>().Create();
            var item = services.GetInstance<ICmPossibilityFactory>().Create();
            types.PossibilitiesOS.Add(item);
            typeItem = item.Guid;
        });
        new FwDataProjectLoader().Save(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        var items = ((RecordList)read.Document!.Data["senses"]!).Items;
        ((RecordAlternatives)((RecordObject)items[0])["gloss"]!).Add("en", new RecordScalar("zzedited"));
        ((RecordObject)items[^1]).Set("status", new RecordScalar(
            "[zzitem](/objects/" + typeItem.ToString("D") + "){.name guid=" + typeItem.ToString("D") + "}"));
        var file = Path.Combine(folder, "doomed.md");
        File.WriteAllText(file, RecordFileWriter.Write(read.Document!, schema.Schema));
        return (cache.ProjectId.Path, file);
    }
}
