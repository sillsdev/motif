using System;
using System.IO;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>A synthetic saved project with one main entry, and a record file that would create a second entry like it.</summary>
internal static class CreatedEntryFile
{
    /// <summary>Builds the project and, in its folder, the file.</summary>
    public static (string Project, string File) Build(PristineProjectFixture pristine)
    {
        using var cache = pristine.NewScratch();
        Guid entryId = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            form.Form.set_String(cache.DefaultVernWs, "zzword");
            var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.Name.set_String(cache.DefaultAnalWs, "zznoun");
            var msa = services.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = pos;
            var sense = services.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.MorphoSyntaxAnalysisRA = msa;
            sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss");
        });
        new FwDataProjectLoader().Save(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        if (read.HasErrors) throw new InvalidOperationException("The template entry does not export cleanly.");
        var document = read.Document!;
        document.Base = null;
        document.Data["head-word"] = null;
        document.Data["homograph-number"] = null;
        var renamed = new System.Collections.Generic.Dictionary<Guid, Guid>();
        Rename(document.Data, renamed);
        var text = RecordFileWriter.Write(document, schema.Schema);
        foreach (var pair in renamed) text = text.Replace(pair.Key.ToString("D"), pair.Value.ToString("D"), StringComparison.OrdinalIgnoreCase);
        var file = Path.Combine(Path.GetDirectoryName(cache.ProjectId.Path)!, "new-entry.md");
        File.WriteAllText(file, text);
        return (cache.ProjectId.Path, file);
    }

    /// <summary>Writes a copy of <paramref name="file"/> next to it, changed by <paramref name="edit"/>, and returns its path.</summary>
    public static string Edited(string project, string file, string name, Action<RecordDocument> edit)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var read = RecordFileReader.Read(File.ReadAllText(file), schema.Schema, new ProjectRecordExporter(cache, schema).CreateReadOptions());
        if (read.HasErrors) throw new InvalidOperationException("The file does not read cleanly.");
        edit(read.Document!);
        var copy = Path.Combine(Path.GetDirectoryName(file)!, name);
        File.WriteAllText(copy, RecordFileWriter.Write(read.Document!, schema.Schema));
        return copy;
    }

    private static void Rename(RecordObject item, System.Collections.Generic.Dictionary<Guid, Guid> renamed)
    {
        if (item.Guid is { } old) item.Guid = renamed[old] = Guid.NewGuid();
        foreach (var field in item.Fields)
        {
            if (field.Value is RecordObject child) Rename(child, renamed);
            else if (field.Value is RecordList list)
                foreach (var entry in list.Items.OfType<RecordObject>()) Rename(entry, renamed);
        }
    }
}
