using System.Text;
using SIL.LCModel;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Scripture;

/// <summary>Whether a stored approval still holds against the Source text and the analysis as they are now.</summary>
public enum ApprovalState
{
    /// <summary>The verse text, the word number and the analysis are all as they were when it was approved.</summary>
    Approved,

    /// <summary>Something changed. It is shown, not counted as approved, and never re-approved automatically.</summary>
    Stale,
}

/// <summary>One stored approval judged against the current text and project.</summary>
/// <param name="Anchor">The Occurrence location, such as <c>MAT 5:3#2</c>.</param>
/// <param name="Analysis">The analysis it was approved with.</param>
/// <param name="State">Whether it still holds.</param>
/// <param name="Reason">Why it is stale; null when it holds.</param>
public sealed record OccurrenceApproval(string Anchor, Guid Analysis, ApprovalState State, string? Reason);

/// <summary>What a read of a Source's approvals found.</summary>
/// <param name="Documents">The approvals not in conflict, by Passage such as <c>MAT 5</c>.</param>
/// <param name="Conflicts">The Occurrences approved with more than one analysis, in location order, with those analyses.</param>
/// <param name="Unreadable">
/// The Passages, or sub-item names, of approvals sub-items whose field cannot be read; nothing in those Passages is in
/// <paramref name="Documents"/> or <paramref name="Conflicts"/>, nor in any Passage when one of these is not a Passage.
/// </param>
public sealed record SourceApprovalSet(IReadOnlyDictionary<string, ScriptureApprovalDocument> Documents,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> Conflicts, IReadOnlyList<string> Unreadable);

/// <summary>
/// Reads the approved Occurrences of a Source from a project copy, as <see cref="SourceList"/> reads the Sources. It
/// never opens a unit of work. See <see cref="ScriptureApprovalFields"/> for where they are stored.
/// </summary>
public static class SourceApprovals
{
    /// <summary>The sentence a confirmation shows when its Apply is the first to add the approvals field.</summary>
    public const string AddsFieldSentence = "This Apply also adds the Motif Approvals field to your project.";

    /// <summary>Whether the project already has the approvals field, which the first Apply that needs it adds.</summary>
    public static bool HasField(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return SourceList.FindField(cache, ScriptureApprovalFields.ApprovalsField) != 0;
    }

    /// <summary>The approvals of each Passage of a Source by Passage, such as <c>MAT 5</c>; empty when it has none.</summary>
    public static IReadOnlyDictionary<string, ScriptureApprovalDocument> Read(LcmCache cache, string sourceId) =>
        ReadReporting(cache, sourceId).Documents;

    /// <summary>
    /// Every approval of a Source, read from the approvals sub-items of its item in the Motif Sources list: the union of
    /// every installation's sub-items. At each Occurrence only the entries no settle record overrules stand
    /// (<see cref="ScriptureApprovalBlob.Standing"/>). Every standing approval of one analysis is kept as that
    /// Occurrence's evidence, whichever installation made it. Different analyses standing at one Occurrence, as
    /// Send/Receive leaves it when two people approved it differently, are a conflict: it is reported with those analyses
    /// and is in no Passage's document. A sub-item whose field cannot be read is reported by its Passage, or by its name
    /// when that names no Passage, never dropped without a word; it may hold the settle records that decide what stands
    /// there, so nothing in that Passage, or in any Passage when its own is unknown, is read as approved or in conflict.
    /// The same holds when the sub-items arrived without the field that holds them.
    /// </summary>
    public static SourceApprovalSet ReadReporting(LcmCache cache, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var documents = new Dictionary<string, ScriptureApprovalDocument>(StringComparer.Ordinal);
        var conflicts = new SortedDictionary<string, IReadOnlyList<Guid>>(ScriptureApprovalDocument.LocationOrder);
        var unreadable = new SortedSet<string>(StringComparer.Ordinal);
        var source = ScriptureSourceLookup.FindItem(cache, sourceId);
        if (source is null) return new(documents, conflicts, [.. unreadable]);
        var flid = SourceList.FindField(cache, ScriptureApprovalFields.ApprovalsField);
        var byPassage = new Dictionary<string, List<ScriptureApprovalBlob>>(StringComparer.Ordinal);
        foreach (var item in source.SubPossibilitiesOS)
        {
            if (ScriptureApprovalFields.TryRead(cache, item, flid, out _) is not { } blob)
            {
                unreadable.Add(ScriptureApprovalFields.LabelOf(item));
                continue;
            }
            if (!byPassage.TryGetValue(blob.Passage, out var blobs)) byPassage[blob.Passage] = blobs = new();
            blobs.Add(blob);
        }
        var everyPassageUncertain = unreadable.Any(label => !ScriptureApprovalDocument.IsPassage(label));
        foreach (var (passage, blobs) in byPassage)
        {
            if (everyPassageUncertain || unreadable.Contains(passage)) continue;
            foreach (var anchor in blobs.SelectMany(blob => blob.Entries.Keys).Distinct())
            {
                var approvals = ScriptureApprovalBlob.Standing(blobs, anchor).Select(holding => holding.Entry.Approval)
                    .OfType<ScriptureApproval>().ToArray();
                var analyses = approvals.Select(approval => approval.Analysis).Distinct()
                    .OrderBy(guid => guid.ToString("D"), StringComparer.Ordinal).ToArray();
                if (analyses.Length == 0) continue;
                if (analyses.Length > 1)
                {
                    conflicts[anchor] = analyses;
                    continue;
                }
                if (!documents.TryGetValue(passage, out var document)) documents[passage] = document = new();
                document.SetEvidence(anchor, approvals);
            }
        }
        return new(documents, conflicts, [.. unreadable]);
    }

    /// <summary>The fingerprint of each verse's text in a Passage by verse such as <c>MAT 5:3</c>.</summary>
    public static IReadOnlyDictionary<string, string> VerseFingerprints(ParatextPassage passage) => passage.Verses
        .GroupBy(verse => verse.AnchorReference)
        .ToDictionary(group => group.Key, group => ScriptureApprovalDocument.Fingerprint(string.Join("\n", group.Select(verse => verse.Text))),
            StringComparer.Ordinal);

    /// <summary>
    /// The tokeniser that numbers a Source's words: the project's default vernacular writing system, as FieldWorks
    /// draws word boundaries. The Source index and every stored Occurrence anchor use it, so their word numbers agree.
    /// </summary>
    public static IWordTokeniser Tokeniser(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return new WritingSystemWordTokeniser(cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem);
    }

    /// <summary>
    /// Judges each approval of a Passage against its current text, where <paramref name="passage"/> is the Source's
    /// Passage as it reads now. An approval is Stale when its verse text, the word at its word number, or its
    /// analysis is not what it was when approved. An Occurrence is Approved when any of its evidence still holds; when
    /// none does, the reason is the first evidence's. Words are compared in Unicode NFD, the form FieldWorks keeps a
    /// wordform in and the form a saved project gives back.
    /// </summary>
    public static IReadOnlyList<OccurrenceApproval> Evaluate(
        LcmCache cache, ScriptureApprovalDocument document, ParatextPassage passage)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(passage);
        var verses = VerseFingerprints(passage);
        var words = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var word in ParatextPassages.Tokenize(passage, Tokeniser(cache))) words[word.Anchor] = word.Surface;
        var objects = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var result = new List<OccurrenceApproval>();
        foreach (var (anchor, approval) in document.Approvals)
        {
            var evidence = document.Evidence.GetValueOrDefault(anchor) ?? [approval];
            var reasons = evidence.Select(each => Reason(each, anchor, verses, words, objects)).ToArray();
            var reason = reasons.Contains(null) ? null : reasons[0];
            result.Add(new(anchor, approval.Analysis, reason is null ? ApprovalState.Approved : ApprovalState.Stale, reason));
        }
        return result;
    }

    private static string? Reason(ScriptureApproval approval, string anchor, IReadOnlyDictionary<string, string> verses,
        IReadOnlyDictionary<string, string> words, ICmObjectRepository objects)
    {
        if (!verses.TryGetValue(ScriptureApprovalDocument.VerseOf(anchor), out var verse) || verse != approval.Verse)
            return "The verse text changed.";
        if (!words.TryGetValue(anchor, out var surface) ||
            surface.Normalize(NormalizationForm.FormD) != approval.Word.Normalize(NormalizationForm.FormD))
            return "The word at this number changed.";
        if (!objects.TryGetObject(approval.Analysis, out var found) || found is not IWfiAnalysis analysis)
            return "The analysis is no longer in the project.";
        return ScriptureApprovalFields.Signature(analysis) != approval.Signature ? "The analysis changed." : null;
    }
}
