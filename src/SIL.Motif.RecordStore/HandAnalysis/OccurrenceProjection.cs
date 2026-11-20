using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>One native Text segment slot, in project order. Nothing here refers to a native record.</summary>
internal readonly record struct OccurrenceSlot(Guid Text, Guid Paragraph, Guid Segment, int Index, int WordPosition,
    bool SegmentStart, bool SegmentEnd, AttachmentKind Kind, Guid Target);

/// <summary>A hand-anchor record: a located occurrence in an external Source, attached to a native record.</summary>
internal sealed record AnchorOccurrence(Guid Id, Guid Target, string Raw, HandAnalysisScriptureAnchor? Typed);

/// <summary>Line slot kinds a native analysis can fill; the missing-slot predicate is indexed per kind and writing system.</summary>
internal static class SlotKinds
{
    public const string WordGloss = "word-gloss", WordCategory = "word-category", MorphForm = "morph-form";
    public const string LexEntry = "lex-entry", LexGloss = "lex-gloss", LexCategory = "lex-category";
    public static readonly string[] PerWs = [WordGloss, MorphForm, LexEntry, LexGloss];
    public static readonly string[] Unscoped = [WordCategory, LexCategory];
}

/// <summary>Reads every native Text, in project order, into attachment slots.</summary>
internal static class TextScan
{
    public static (ImmutableArray<OccurrenceSlot> Slots, int Unrecognized) Read(
        IReadOnlyDictionary<Guid, GenericRecord> native, NativeUsageSchema usage, CancellationToken ct)
    {
        int Need(string owner, string name, int type) => usage.FieldId(owner, name, type)
            ?? throw new IndexUnavailableException($"The retained schema does not define {owner}.{name} exactly once.");
        var contents = Need("Text", "Contents", 23);
        var paragraphs = Need("StText", "Paragraphs", 27);
        var segmentsField = Need("StTxtPara", "Segments", 27);
        var analyses = Need("Segment", "Analyses", 28);
        var slots = ImmutableArray.CreateBuilder<OccurrenceSlot>();
        var unrecognized = 0;
        foreach (var text in native.Values.Where(r => usage.Is(r.ClassId, "Text")).OrderBy(r => r.Provenance!.EncounterOrdinal))
        {
            var body = LexiconProjection.Atom(text, contents);
            if (body is null) continue;
            foreach (var paragraph in LexiconProjection.Seq(WfiProjection.Get(native, body.Value), paragraphs))
            foreach (var segment in LexiconProjection.Seq(WfiProjection.Get(native, paragraph), segmentsField))
            {
                ct.ThrowIfCancellationRequested();
                var members = LexiconProjection.Seq(WfiProjection.Get(native, segment), analyses).ToArray();
                var word = 0;
                for (var i = 0; i < members.Length; i++)
                {
                    var cls = WfiProjection.Get(native, members[i]).ClassId;
                    AttachmentKind? kind = usage.Is(cls, "WfiWordform") ? AttachmentKind.Wordform
                        : usage.Is(cls, "WfiAnalysis") ? AttachmentKind.Analysis
                        : usage.Is(cls, "WfiGloss") ? AttachmentKind.Gloss
                        : usage.Is(cls, "PunctuationForm") ? AttachmentKind.Punctuation : null;
                    if (kind is null) { unrecognized++; continue; }
                    slots.Add(new(text.Id, paragraph, segment, i, kind == AttachmentKind.Punctuation ? word : word++, i == 0,
                        i == members.Length - 1, kind.Value, members[i]));
                }
            }
        }
        return (slots.ToImmutable(), unrecognized);
    }

    /// <summary>Attachment counts by target, counting each anchor record once for its target.</summary>
    public static Dictionary<Guid, long> Counts(ImmutableArray<OccurrenceSlot> slots, IEnumerable<AnchorOccurrence> anchors)
    {
        var counts = new Dictionary<Guid, long>();
        foreach (var slot in slots) counts[slot.Target] = counts.GetValueOrDefault(slot.Target) + 1;
        foreach (var anchor in anchors) counts[anchor.Target] = counts.GetValueOrDefault(anchor.Target) + 1;
        return counts;
    }

    public static ImmutableArray<AnchorOccurrence> Anchors(IEnumerable<GenericRecord> records) => records
        .Select(r =>
        {
            if (HandAnalysisKinds.AnchorTarget(r) is not { } target) return null;
            var raw = r.Values["anchor"].GetString()!;
            string? Text(string name) => r.Values.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var source = Text("source") is { } s && Guid.TryParse(s, out var id) ? id : (Guid?)null;
            return new AnchorOccurrence(r.Id, target, raw, source is { } sid && Text("versification") is { } v && Text("verseHash") is { } h
                ? new HandAnalysisScriptureAnchor(sid, v, raw, h) : null);
        }).OfType<AnchorOccurrence>().OrderBy(a => a.Id).ToImmutableArray();
}

/// <summary>
/// The occurrence half of a pinned point: native Text slots, their attachment and wordform indexes, and per
/// slot-kind missing positions so a next-incomplete query touches only incomplete words.
/// </summary>
internal sealed class OccurrenceProjection
{
    private readonly RowCounter counter;

    private OccurrenceProjection(RowCounter counter) => this.counter = counter;
    private static readonly int[] None = [];

    public CountedRows<OccurrenceSlot> Slots { get; private init; } = null!;
    public ImmutableArray<AnchorOccurrence> Anchors { get; private init; }
    public int Unrecognized { get; private init; }
    public ImmutableDictionary<Guid, (int Start, int End)> TextRange { get; private init; } = ImmutableDictionary<Guid, (int, int)>.Empty;
    public ImmutableDictionary<(Guid Segment, int Index), int> BySlot { get; private init; } =
        ImmutableDictionary<(Guid, int), int>.Empty;
    public ImmutableDictionary<Guid, int[]> ByTarget { get; private init; } = ImmutableDictionary<Guid, int[]>.Empty;
    public ImmutableDictionary<Guid, int[]> ByWordform { get; private init; } = ImmutableDictionary<Guid, int[]>.Empty;
    public ImmutableDictionary<Guid, ImmutableArray<LineSlot>> Filled { get; private init; } =
        ImmutableDictionary<Guid, ImmutableArray<LineSlot>>.Empty;
    public ImmutableDictionary<(string Kind, string Ws), int[]> Missing { get; private init; } =
        ImmutableDictionary<(string, string), int[]>.Empty;
    public int[] Words { get; private init; } = None;

    /// <summary>Prefix counts of slots by attachment kind (index = <see cref="AttachmentKind"/>), so a range is counted without a scan.</summary>
    public int[][] KindPrefix { get; private init; } = [];
    public ImmutableDictionary<Guid, string> Fingerprints { get; private init; } = ImmutableDictionary<Guid, string>.Empty;
    public IReadOnlyDictionary<Guid, long> Counts { get; private init; } = ImmutableDictionary<Guid, long>.Empty;

    /// <summary>Slots read by lookups since the projection was built.</summary>
    public long Touched => counter.Value;

    /// <summary>The wordform, analysis and gloss a slot resolves to.</summary>
    public static (Guid? Wordform, Guid? Analysis, Guid? Gloss) Resolve(in OccurrenceSlot slot, WfiProjection wfi) => slot.Kind switch
    {
        AttachmentKind.Wordform => (slot.Target, null, null),
        AttachmentKind.Analysis => (wfi.Readings.TryGetValue(slot.Target, out var a) ? a.Fact.WordformId : null, slot.Target, null),
        AttachmentKind.Gloss when wfi.GlossOwner.TryGetValue(slot.Target, out var owner) =>
            (wfi.Readings[owner].Fact.WordformId, owner, slot.Target),
        _ => (null, null, null)
    };

    public static OccurrenceProjection Build((ImmutableArray<OccurrenceSlot> Slots, int Unrecognized) scan,
        ImmutableArray<AnchorOccurrence> anchors, Dictionary<Guid, long> counts, WfiProjection wfi, LexiconProjection lex,
        ProjectProjection project, CancellationToken ct)
    {
        var slots = scan.Slots;
        var ranges = new Dictionary<Guid, (int, int)>();
        var bySlot = new Dictionary<(Guid, int), int>(slots.Length);
        var byTarget = new Dictionary<Guid, List<int>>();
        var byWordform = new Dictionary<Guid, List<int>>();
        var fingerprints = new Dictionary<Guid, IncrementalHash>();
        var filledCache = new Dictionary<Guid, ImmutableArray<LineSlot>>();
        var tags = project.WritingSystems.Select(w => w.Tag).ToArray();
        var universe = SlotKinds.PerWs.SelectMany(kind => tags.Select(ws => (kind, ws)))
            .Concat(SlotKinds.Unscoped.Select(kind => (kind, ws: ""))).ToArray();
        var missing = universe.ToDictionary(u => u, _ => new List<int>());
        var words = new List<int>();
        for (var i = 0; i < slots.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var slot = slots[i];
            ranges[slot.Text] = ranges.TryGetValue(slot.Text, out var r) ? (r.Item1, i + 1) : (i, i + 1);
            bySlot[(slot.Segment, slot.Index)] = i;
            AddTo(byTarget, slot.Target, i);
            if (!fingerprints.TryGetValue(slot.Segment, out var hash))
                fingerprints[slot.Segment] = hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(slot.Target.ToByteArray());
            if (slot.Kind == AttachmentKind.Punctuation) continue;
            words.Add(i);
            if (Resolve(slot, wfi).Wordform is { } wordform) AddTo(byWordform, wordform, i);
            if (!filledCache.TryGetValue(slot.Target, out var filled))
                filledCache[slot.Target] = filled = FilledSlots(slot, wfi, lex, tags);
            var present = filled.ToHashSet();
            foreach (var u in universe)
                if (!present.Contains(new(u.kind, u.ws))) missing[u].Add(i);
        }
        var counter = new RowCounter();
        return new(counter)
        {
            KindPrefix = Prefix(slots), Slots = new(slots, counter), Anchors = anchors, Unrecognized = scan.Unrecognized,
            TextRange = ranges.ToImmutableDictionary(p => p.Key, p => (p.Value.Item1, p.Value.Item2)),
            BySlot = bySlot.ToImmutableDictionary(), ByTarget = byTarget.ToImmutableDictionary(p => p.Key, p => p.Value.ToArray()),
            ByWordform = byWordform.ToImmutableDictionary(p => p.Key, p => p.Value.ToArray()),
            Filled = filledCache.ToImmutableDictionary(), Words = [.. words],
            Missing = missing.ToImmutableDictionary(p => p.Key, p => p.Value.ToArray()),
            Fingerprints = fingerprints.ToImmutableDictionary(p => p.Key, p => Convert.ToHexStringLower(p.Value.GetHashAndReset())),
            Counts = counts
        };
    }

    private static int[][] Prefix(ImmutableArray<OccurrenceSlot> slots)
    {
        var prefix = Enumerable.Range(0, 4).Select(_ => new int[slots.Length + 1]).ToArray();
        for (var i = 0; i < slots.Length; i++)
            for (var k = 0; k < 4; k++) prefix[k][i + 1] = prefix[k][i] + ((int)slots[i].Kind == k ? 1 : 0);
        return prefix;
    }

    private static void AddTo(Dictionary<Guid, List<int>> map, Guid key, int position)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(position);
    }

    private static ImmutableArray<LineSlot> FilledSlots(in OccurrenceSlot slot, WfiProjection wfi, LexiconProjection lex, string[] tags)
    {
        var (_, analysis, gloss) = Resolve(slot, wfi);
        var filled = new List<LineSlot>();
        if (analysis is not { } id) return [];
        var reading = wfi.Readings[id].Fact;
        if (reading.CategoryId is not null) filled.Add(new(SlotKinds.WordCategory, ""));
        if (gloss is { } g && reading.Glosses.FirstOrDefault(x => x.Id == g) is { } fact)
            filled.AddRange(fact.Forms.Where(p => p.Value.Length > 0).Select(p => new LineSlot(SlotKinds.WordGloss, p.Key)));
        var bundles = reading.Bundles;
        if (bundles.IsEmpty) return [.. filled.OrderBy(s => s.Kind, StringComparer.Ordinal).ThenBy(s => s.Ws, StringComparer.Ordinal)];
        foreach (var ws in tags)
        {
            if (bundles.All(b => b.Forms.GetValueOrDefault(ws, "").Length > 0)) filled.Add(new(SlotKinds.MorphForm, ws));
            if (bundles.All(b => b.MorphId is not null && b.ConcreteForms.GetValueOrDefault(ws, "").Length > 0))
                filled.Add(new(SlotKinds.LexEntry, ws));
            if (bundles.All(b => b.SenseId is { } s && lex.SenseById.TryGetValue(s, out var sense) &&
                sense.Gloss.GetValueOrDefault(ws, "").Length > 0)) filled.Add(new(SlotKinds.LexGloss, ws));
        }
        if (bundles.All(b => b.MsaId is { } m && lex.MsaById.TryGetValue(m, out var msa) && (msa.CategoryId ?? msa.ToCategoryId) is not null))
            filled.Add(new(SlotKinds.LexCategory, ""));
        return [.. filled.OrderBy(s => s.Kind, StringComparer.Ordinal).ThenBy(s => s.Ws, StringComparer.Ordinal)];
    }

    /// <summary>Positions in [start, end) where any of the slots is empty, in position order.</summary>
    public int[] MissingIn(IEnumerable<LineSlot> required, int start, int end)
    {
        var parts = new List<int[]>();
        foreach (var slot in required)
        {
            var all = Missing.GetValueOrDefault((slot.Kind, slot.Ws), Words);
            var low = LowerBound(all, start);
            var high = LowerBound(all, end);
            parts.Add(all[low..high]);
        }
        return parts.Count == 1 ? parts[0] : [.. parts.SelectMany(p => p).Distinct().Order()];
    }

    public static int LowerBound(int[] sorted, int value)
    {
        int low = 0, high = sorted.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (sorted[mid] < value) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    public string Fingerprint(Guid segment) => Fingerprints.GetValueOrDefault(segment, "");
}
