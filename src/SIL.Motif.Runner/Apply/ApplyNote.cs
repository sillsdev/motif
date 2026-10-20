using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.AppliedLog;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Apply;

/// <summary>What the Notebook note for an Apply says: the Proposal's title and its Rationale, as plain text.</summary>
/// <param name="Title">The Proposal's label.</param>
/// <param name="Rationale">The Proposal's Rationale (Markdown is kept as plain text), or <see langword="null"/>.</param>
public sealed record ApplyNote(string Title, string? Rationale);

/// <summary>
/// Writes the one Notebook record an Apply with a note adds, through the same generated operations a human
/// judgment record uses, inside the caller's unit of work so it reverses with the changes.
/// </summary>
public static class ApplyNoteWriter
{
    /// <summary>The name of the record type the notes carry, created under the Notebook's record types on first use.</summary>
    public const string RecordTypeName = "Motif change";

    /// <summary>Whether the project has a Research Notebook to hold the note.</summary>
    public static bool CanWrite(LcmCache cache) => cache.LangProject.ResearchNotebookOA is not null;

    internal static void Write(LcmCache cache, ApplyNote note, AppliedLogEntry entry, string message)
    {
        var notebook = cache.LangProject.ResearchNotebookOA
            ?? throw new InvalidOperationException("The project has no Research Notebook to hold the note.");
        var recordType = FindOrCreateRecordType(cache, notebook);
        var analysisWs = cache.ServiceLocator.WritingSystems.DefaultAnalysisWritingSystem.Id;

        var paragraphs = new List<string>();
        if (!string.IsNullOrWhiteSpace(note.Rationale))
            paragraphs.AddRange(note.Rationale!.Split('\n')
                .Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0));
        paragraphs.Add($"Receipt: Proposal {CanonicalId.FromGuid(entry.ProposalId).Value}, applied " +
                       $"{entry.TimestampUtc}, intent {entry.IntentDigest[..12]}, {entry.Mode}.");
        if (!string.IsNullOrWhiteSpace(message)) paragraphs.Add($"Message: {message}");

        var recordId = CanonicalId.Mint();
        var notebookId = CanonicalId.FromGuid(notebook.Guid);
        var descriptionId = CanonicalId.Mint();
        var recordCreate = CanonicalId.Mint();
        var descriptionCreate = CanonicalId.Mint();
        var operations = new List<OperationEnvelope>
        {
            new(recordCreate, RnResearchNbkRecordsOperationKinds.Create, recordId, notebookId, Json(new { })),
            new(CanonicalId.Mint(), RnGenericRecTypeOperationKinds.SetType, target: recordId,
                after: Json(new { @ref = CanonicalId.FromGuid(recordType.Guid).Value }), dependsOn: After(recordCreate)),
            new(CanonicalId.Mint(), RnGenericRecTitleOperationKinds.Set, target: recordId,
                after: Json(new { ws = analysisWs, text = note.Title }), dependsOn: After(recordCreate)),
            new(descriptionCreate, RnGenericRecDescriptionOperationKinds.Create, descriptionId, recordId,
                Json(new { }), dependsOn: After(recordCreate)),
        };
        CanonicalId? previousParagraph = null;
        CanonicalId? previousCreate = null;
        foreach (var text in paragraphs)
        {
            var paragraphId = CanonicalId.Mint();
            var paragraphCreate = CanonicalId.Mint();
            var dependencies = new List<OperationDependency> { new(descriptionCreate) };
            if (previousCreate is { } created) dependencies.Add(new OperationDependency(created));
            operations.Add(new(paragraphCreate, StTextParagraphsOperationKinds.Create, paragraphId, descriptionId,
                Json(new { @class = "StTxtPara" }),
                placement: previousParagraph is { } anchor ? new Placement(anchor, null) : null,
                dependsOn: dependencies));
            operations.Add(new(CanonicalId.Mint(), StTxtParaContentsOperationKinds.Set, target: paragraphId,
                after: Json(new { ws = analysisWs, text }), dependsOn: After(paragraphCreate)));
            previousParagraph = paragraphId;
            previousCreate = paragraphCreate;
        }

        // The note's effects are discarded, so the effect digest stays the Proposal's.
        var discarded = new List<CanonicalId>();
        foreach (var operation in operations)
        {
            var handler = OperationHandlerRegistry.Resolve(operation.Kind, "Apply note");
            OperationEffectCapture.Apply(handler, cache, operation, discarded);
        }
    }

    private static ICmPossibility FindOrCreateRecordType(LcmCache cache, IRnResearchNbk notebook)
    {
        var list = notebook.RecTypesOA;
        if (list is null)
        {
            list = cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = list;
        }
        var existing = Descendants(list.PossibilitiesOS.Cast<ICmPossibility>()).FirstOrDefault(possibility =>
            possibility.Name.AvailableWritingSystemIds.Any(ws =>
                string.Equals(possibility.Name.get_String(ws).Text, RecordTypeName, StringComparison.Ordinal)));
        if (existing is not null) return existing;

        var created = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
        list.PossibilitiesOS.Add(created);
        var ws = cache.DefaultAnalWs;
        created.Name.set_String(ws, TsStringUtils.MakeString(RecordTypeName, ws));
        created.Abbreviation.set_String(ws, TsStringUtils.MakeString("Motif", ws));
        return created;
    }

    private static IEnumerable<ICmPossibility> Descendants(IEnumerable<ICmPossibility> possibilities)
    {
        foreach (var possibility in possibilities)
        {
            yield return possibility;
            foreach (var child in Descendants(possibility.SubPossibilitiesOS.Cast<ICmPossibility>()))
                yield return child;
        }
    }

    private static OperationDependency[] After(CanonicalId operationId) => [new OperationDependency(operationId)];

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
