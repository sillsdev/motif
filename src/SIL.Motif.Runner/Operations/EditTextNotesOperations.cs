using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Execution;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that replaces the Notes of one existing Segment with a single Note.
/// </summary>
/// <remarks>
/// Targets the Segment. The closed <c>after</c> payload is <c>{ "ws", "note": { "id", "text" }, "expected": [text, ...] }</c>:
/// <c>expected</c> lists the Segment's current Note texts in <c>ws</c>, in order, and Apply stops if they differ.
/// The Note named by <c>id</c> is kept when the Segment already owns it and is created otherwise; every other Note of the
/// Segment is removed. Only the <c>ws</c> alternative of the kept Note is written.
/// </remarks>
public static class EditTextNotesOperationKinds
{
    public const string Set = "texts/segment/setNotes";
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, EditTextNotesHandler.Instance);
        OperationSlotRegistry.RegisterScalar(Set, SnapshotFields.TextSegmentNotes);
    }
#pragma warning restore CA2255
}

internal sealed class EditTextNotesHandler : IOperationHandler
{
    internal static readonly EditTextNotesHandler Instance = new();
    private const string Kind = EditTextNotesOperationKinds.Set;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (ws, tag, noteId, text, expected) = Parse(cache, operation);
        var (id, segment) = TargetResolution.Resolve<ISegment>(cache, operation, Kind);
        var current = Texts(segment, ws);
        if (!current.SequenceEqual(expected, StringComparer.Ordinal))
            throw new TextNotesStaleSourceException(id, string.Join(" | ", expected), string.Join(" | ", current));
        var before = Read(cache, segment);
        touchedTargets.Add(id);
        INote? kept = segment.NotesOS.FirstOrDefault(note => note.Guid == noteId.ToGuid());
        if (kept is null)
        {
            if (cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(noteId.ToGuid(), out _))
                throw new InvalidOperationException($"'{Kind}': note id '{noteId.Value}' is already used by another object.");
            kept = cache.ServiceLocator.GetInstance<INoteFactory>().Create(noteId.ToGuid());
            segment.NotesOS.Add(kept);
        }
        for (var index = segment.NotesOS.Count - 1; index >= 0; index--)
            if (segment.NotesOS[index].Guid != kept.Guid) segment.NotesOS.RemoveAt(index);
        kept.Content.set_String(ws, TsStringUtils.MakeString(text, ws));
        return new ExpectedEffect(id, SnapshotFields.TextSegmentNotes, before, Read(cache, segment));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, segment) = TargetResolution.Resolve<ISegment>(cache, operation, Kind);
        var current = Read(cache, segment);
        return new ExpectedEffect(id, SnapshotFields.TextSegmentNotes, current, current);
    }

    private static (int Ws, string Tag, CanonicalId NoteId, string Text, string[] Expected) Parse(
        LcmCache cache, OperationEnvelope operation)
    {
        var after = OperationSlotRegistry.After(operation);
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["ws", "note", "expected"], Kind);
        var tag = ClosedPayloadParsing.GetRequiredString(after, "ws", Kind);
        var ws = cache.WritingSystemFactory.GetWsFromStr(tag);
        if (ws == 0) throw new InvalidOperationException($"'{Kind}': writing system '{tag}' is not known to this project.");
        if (!after.TryGetProperty("note", out var note)) throw new ContractParseException($"'{Kind}' operation 'after.note' is required.");
        ClosedPayloadParsing.RequireObject(note, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(note, ["id", "text"], Kind);
        var noteId = ClosedPayloadParsing.GetRequiredCanonicalId(note, "id", Kind);
        var text = ClosedPayloadParsing.GetRequiredString(note, "text", Kind);
        if (!after.TryGetProperty("expected", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{Kind}' operation 'after.expected' is required and must be an array.");
        var expected = list.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
            ? item.GetString()! : throw new ContractParseException($"'{Kind}' operation 'after.expected' holds strings.")).ToArray();
        return (ws, tag, noteId, text, expected);
    }

    internal static string[] Texts(ISegment segment, int ws) =>
        segment.NotesOS.Select(note => note.Content.get_String(ws).Text ?? string.Empty).ToArray();

    private static Dictionary<string, string> Read(LcmCache cache, ISegment segment)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var note in segment.NotesOS)
        {
            var key = "note:" + index++;
            fields[key] = CanonicalId.FromGuid(note.Guid).Value;
            foreach (var ws in note.Content.AvailableWritingSystemIds.OrderBy(cache.WritingSystemFactory.GetStrFromWs, StringComparer.Ordinal))
                fields[key + ":" + cache.WritingSystemFactory.GetStrFromWs(ws)] = note.Content.get_String(ws).Text ?? string.Empty;
        }
        return fields;
    }
}

internal sealed class TextNotesStaleSourceException : ProposalRefusalException
{
    internal TextNotesStaleSourceException(CanonicalId segment, string expected, string actual)
        : base(RefusalCodes.TextNotesStaleSource, FailureReason.Refused,
            "The segment's Notes changed; compose the edit again.",
            new Dictionary<string, string> { ["segment"] = segment.Value, ["expectedNotes"] = expected,
                ["actualNotes"] = actual }) { }
}
