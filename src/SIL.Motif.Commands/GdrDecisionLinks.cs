using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing.Markdown;

namespace SIL.Motif.Commands;

/// <summary>
/// Finds the objects a Draft changes that a Grammar Guide section does not link, for the warning <c>guide decide</c> gives when
/// the section it files a decision under links none of them. It reads only the section and the operations the decide has loaded.
/// </summary>
internal static class GdrDecisionLinks
{
    private const string PossibilityFieldPrefix = "CmPossibility.";

    /// <summary>
    /// The objects <paramref name="operations"/> change, in first-seen order, when <paramref name="section"/> links none of them;
    /// empty when it links one, or when the operations change no object other than Guide sections and Notes.
    /// </summary>
    public static IReadOnlyList<GdrUnlinkedObject> Unlinked(GdrShowResponse section, IReadOnlyList<OperationEnvelope> operations)
    {
        var self = CanonicalId.Parse(section.Record.Id).ToGuid();
        var touched = new Dictionary<Guid, (string Id, List<string> Kinds)>();
        foreach (var operation in operations)
        {
            if (operation.Target is not { } target || IsGuideOrNote(operation)) continue;
            var guid = target.ToGuid();
            if (guid == self) continue;
            if (!touched.TryGetValue(guid, out var entry)) touched[guid] = entry = (target.Value, []);
            if (!entry.Kinds.Contains(operation.Kind, StringComparer.Ordinal)) entry.Kinds.Add(operation.Kind);
        }
        if (touched.Count == 0) return [];
        foreach (var paragraph in section.Discussion)
            foreach (var run in paragraph.Runs)
                if (run.ObjData is { } data && DefaultObjectMapper.Instance.Describe(data) is { Guid: { } linked } && touched.ContainsKey(linked))
                    return [];
        return touched.Values.Select(entry => new GdrUnlinkedObject(entry.Id, entry.Kinds.ToArray())).ToArray();
    }

    // The Draft's other Guide edits and Notes are not the grammar a decision is about.
    private static bool IsGuideOrNote(OperationEnvelope operation) => operation.Kind switch
    {
        EditTextNotesOperationKinds.Set or CmPossibilityNameOperationKinds.SetName or CmPossibilityNameOperationKinds.ClearName
            or GdrSectionOperationKinds.Create or StyleCreateOperationKinds.Create => true,
        TextRichStringOperationKinds.Set => operation.After is { } after
            && TextRichStringPayload.Parse(after).Field.StartsWith(PossibilityFieldPrefix, StringComparison.Ordinal),
        TextParagraphsOperationKinds.Set => operation.After is { } after
            && TextParagraphsPayload.Parse(after).Field.StartsWith(PossibilityFieldPrefix, StringComparison.Ordinal),
        _ => false,
    };
}
