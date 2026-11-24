using System.Collections.Immutable;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.RecordStore.HandAnalysis;

internal sealed partial class StoreSnapshot
{
    private static readonly ImmutableArray<ReadLimitation> CircumfixLimitation = [new("circumfix-partners-unavailable", [],
        "Circumfix partner forms are not derived in this index.")];

    public ProjectRead Project
    {
        get
        {
            var project = Point.Hand.Project;
            return new ProjectRead.Available(new(Stamp, null, project.MorphTypes, project.WritingSystems, project.Categories,
                [new("settings-unavailable", [], "Hand-analysis line settings are not stored in this Home.")]));
        }
    }

    public ValueTask<CommandOutcome<Page<FormHit>>> FormsAsync(FormQuery query, PageRequest page, CancellationToken ct = default) =>
        Read(query, page, ct, (point, request) => Forms(point.Hand, query, request, ct));

    public ValueTask<CommandOutcome<Page<LexicalFact>>> LexiconAsync(LexiconQuery query, PageRequest page, CancellationToken ct = default) =>
        Read(query, page, ct, (point, request) => Lexicon(point.Hand, query, request, ct));

    public ValueTask<CommandOutcome<OccurrencePage>> OccurrencesAsync(OccurrenceQuery query, PageRequest page, CancellationToken ct = default) =>
        Read(query, page, ct, (point, request) => Occurrences(point.Hand, query, request, ct));

    private Page<FormHit> Forms(HandAnalysisProjection hand, FormQuery query, PagePosition request, CancellationToken ct)
    {
        var lex = hand.Lexicon;
        var alternatives = query.Alternatives.Select(a => (a.TypeId, a.Markers.Before, a.Markers.After)).ToHashSet();
        var entries = query.EntryIds?.ToHashSet();
        var candidates = new List<int>();
        if (alternatives.Count > 0 && entries is not { Count: 0 })
        {
            if (query.Mode == FormMatch.Equal)
            {
                var key = hand.CollationKey(query.Ws, WfiProjection.Nfd(query.NfdText));
                if (lex.FormsByKey.TryGetValue((query.Ws, key), out var rows))
                {
                    candidates.AddRange(rows.Where(r => Matches(hand, lex.Forms[r].Hit, alternatives)));
                }
            }
            else
                foreach (var pair in alternatives)
                    if (lex.PatternsByPair.TryGetValue(pair, out var rows))
                        candidates.AddRange(rows);
        }
        var chosen = candidates.Distinct().Where(r => entries is null || entries.Contains(lex.Forms[r].Hit.EntryId))
            .Select(r =>
            {
                ct.ThrowIfCancellationRequested();
                var row = lex.Forms[r];
                var sort = row.Hit.ComparisonKeys.GetValueOrDefault(query.Ws, "");
                return (Row: r, Hit: row.Hit with { DisplayKey = row.Hit.Forms.GetValueOrDefault(query.Ws, ""), SortKey = sort },
                    Encounter: row.Encounter);
            });
        var ordered = (query.Order switch
        {
            FormOrder.Usage => chosen.OrderByDescending(c => c.Hit.DistinctAnalysisUseCount).ThenBy(c => c.Encounter.RepositoryOrdinal)
                .ThenBy(c => c.Encounter.Ordinal),
            FormOrder.Chooser => chosen.OrderBy(c => c.Hit.SortKey, StringComparer.Ordinal).ThenBy(c => c.Encounter.RepositoryOrdinal)
                .ThenBy(c => c.Encounter.Ordinal),
            _ => chosen.OrderBy(c => c.Encounter.RepositoryOrdinal).ThenBy(c => c.Encounter.Ordinal)
        }).Select((c, i) => (Key: (long)i, c.Hit)).ToArray();
        return Paginate(request, ordered, r => r.Key, UseCountLimitation) is var rows2
            ? new(Stamp, rows2.Items.Select(r => r.Hit).ToImmutableArray(), rows2.Total, rows2.NextCursor, rows2.Complete, rows2.Limitations)
            : throw new InvalidOperationException();
    }

    private static bool Matches(HandAnalysisProjection hand, FormHit hit, HashSet<(Guid, string, string)> alternatives) =>
        hit.MorphTypeId is { } id && hand.Project.MorphTypeById.TryGetValue(id, out var type) &&
        hand.Project.PairsOf(type).Any(p => alternatives.Contains((id, p.Before, p.After)));

    private Page<LexicalFact> Lexicon(HandAnalysisProjection hand, LexiconQuery query, PagePosition request, CancellationToken ct)
    {
        var lex = hand.Lexicon;
        var found = new SortedSet<int>();
        var limits = ImmutableArray.CreateBuilder<ReadLimitation>();
        switch (query)
        {
            case LexiconQuery.ByIds ids:
                foreach (var id in ids.EntryIds) Add(lex.EntryIndex.TryGetValue(id, out var e) ? e : -1, id);
                foreach (var id in ids.SenseIds) Add(lex.SenseEntry.TryGetValue(id, out var e) ? e : -1, id);
                foreach (var id in ids.MsaIds) Add(lex.MsaEntry.TryGetValue(id, out var e) ? e : -1, id);
                foreach (var id in ids.RefIds) Add(lex.RefEntry.TryGetValue(id, out var e) ? e : -1, id);
                break;
            case LexiconQuery.Search search:
                if (lex.Search.TryGetValue((search.Field, search.Ws), out var index))
                {
                    var text = WfiProjection.Nfd(search.NfdText);
                    if (search.Match == LexicalMatch.Equal)
                    {
                        if (index.ByKey.TryGetValue(hand.CollationKey(search.Ws, text), out var rows))
                            foreach (var row in rows) found.Add(row);
                    }
                    else
                    {
                        var low = LowerBound(index.Sorted, text);
                        for (var i = low; i < index.Sorted.Length && index.Sorted[i].Nfd.StartsWith(text, StringComparison.Ordinal); i++)
                        {
                            found.Add(index.Sorted[i].Entry);
                        }
                    }
                }
                break;
            default: throw new ArgumentException("Unknown lexicon query.", nameof(query));
        }
        void Add(int entry, Guid id)
        {
            if (entry < 0) limits.Add(new("id-not-found", [id], "No lexical record has this id."));
            else found.Add(entry);
        }
        var ordered = found.Select(e =>
        {
            ct.ThrowIfCancellationRequested();
            return (Key: (long)e, Fact: lex.Entries[e].Fact with { CircumfixPartners = [] });
        }).ToArray();
        return Paginate(request, ordered, r => r.Key, [.. limits, .. CircumfixLimitation]) is var p
            ? new(Stamp, p.Items.Select(r => r.Fact).ToImmutableArray(), p.Total, p.NextCursor, p.Complete, p.Limitations)
            : throw new InvalidOperationException();
    }

    private static int LowerBound((string Nfd, int Entry)[] sorted, string value)
    {
        int low = 0, high = sorted.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (string.CompareOrdinal(sorted[mid].Nfd, value) < 0) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    private OccurrencePage Occurrences(HandAnalysisProjection hand, OccurrenceQuery query, PagePosition request, CancellationToken ct)
    {
        var occ = hand.Occurrences;
        IReadOnlyList<int> positions;
        bool forward = true;
        (int Start, int End)? span = null;
        switch (query)
        {
            case OccurrenceQuery.Exact exact:
                positions = exact.Occurrence.Anchor is HandAnalysisTextAnchor at && occ.BySlot.TryGetValue((at.SegmentId, at.Index), out var slot)
                    && occ.Slots[slot].Text == at.TextId ? [slot] : [];
                break;
            case OccurrenceQuery.Range range:
                var (start, end) = TextSpan(occ, range.Scope);
                forward = range.Direction == OccurrenceDirection.Forward;
                var lo = start; var hi = end;
                var from = range.From is { } f ? Resolve(occ, f, start, end) : (int?)null;
                var through = range.Through is { } t ? Resolve(occ, t, start, end) : (int?)null;
                if (forward) { lo = from ?? start; hi = through is { } th ? th + 1 : end; }
                else { lo = through ?? start; hi = from is { } fr ? fr + 1 : end; }
                hi = Math.Max(lo, hi);
                span = (lo, hi);
                positions = range.MissingVisibleSlots.IsEmpty ? new Span(lo, hi, forward)
                    : new Reversible(occ.MissingIn(range.MissingVisibleSlots, lo, hi), forward);
                break;
            case OccurrenceQuery.Members members:
                positions = Members(hand, members, ct);
                break;
            case OccurrenceQuery.Uses uses:
                positions = Uses(hand, uses, ct);
                break;
            default: throw new ArgumentException("Unknown occurrence query.", nameof(query));
        }
        var all = positions;
        var pageRows = Paginate(request, all, p => forward ? p : occ.Slots.Length - p,
            [new("occurrence-binding-unavailable", [], "Source revision, binding, wordform surface and translation are unavailable; Source-resident occurrences are outside the store."),
             new("occurrence-anchor-records-opaque", [], "Anchor records are counted in use counts; their locations are not typed Source anchors.")]);
        var facts = pageRows.Items.Select(p =>
        {
            ct.ThrowIfCancellationRequested();
            return Fact(hand, p);
        }).ToImmutableArray();
        long bare = 0, analysis = 0, gloss = 0, punct = 0;
        if (span is { } s && query is OccurrenceQuery.Range { MissingVisibleSlots.IsEmpty: true })
        {
            bare = occ.KindPrefix[(int)AttachmentKind.Wordform][s.End] - occ.KindPrefix[(int)AttachmentKind.Wordform][s.Start];
            analysis = occ.KindPrefix[(int)AttachmentKind.Analysis][s.End] - occ.KindPrefix[(int)AttachmentKind.Analysis][s.Start];
            gloss = occ.KindPrefix[(int)AttachmentKind.Gloss][s.End] - occ.KindPrefix[(int)AttachmentKind.Gloss][s.Start];
            punct = occ.KindPrefix[(int)AttachmentKind.Punctuation][s.End] - occ.KindPrefix[(int)AttachmentKind.Punctuation][s.Start];
        }
        else
            for (var i = 0; i < all.Count; i++)
                switch (occ.Slots[all[i]].Kind)
                {
                    case AttachmentKind.Wordform: bare++; break;
                    case AttachmentKind.Analysis: analysis++; break;
                    case AttachmentKind.Gloss: gloss++; break;
                    default: punct++; break;
                }
        return new(new(Stamp, facts, pageRows.Total, pageRows.NextCursor, pageRows.Complete, pageRows.Limitations),
            new(bare, analysis, gloss, null, null, null, punct, false));
    }

    private (int Start, int End) TextSpan(OccurrenceProjection occ, HandAnalysisBarePopulation scope)
    {
        if (scope is not HandAnalysisBareTextPopulation text)
            throw new QueryRefused(new(RefusalCodes.HandAnalysisSourceUnavailable, FailureReason.Refused,
                "A chapter population is resolved against a Source, which this index does not hold."));
        return occ.TextRange.TryGetValue(text.TextId, out var span) ? span : (0, 0);
    }

    private static int? Resolve(OccurrenceProjection occ, HandAnalysisAnchor anchor, int start, int end)
    {
        if (anchor is not HandAnalysisTextAnchor text)
            throw new QueryRefused(new(RefusalCodes.HandAnalysisSourceUnavailable, FailureReason.Refused,
                "A Source anchor cannot be resolved by this index."));
        return occ.BySlot.TryGetValue((text.SegmentId, text.Index), out var slot) && slot >= start && slot < end ? slot : null;
    }

    private IReadOnlyList<int> Members(HandAnalysisProjection hand, OccurrenceQuery.Members query, CancellationToken ct)
    {
        var occ = hand.Occurrences;
        var wordforms = new List<Guid>();
        foreach (var key in query.Wordforms)
            if (hand.Wfi.ByKey.TryGetValue((key.Ws, WfiProjection.Nfd(key.NfdCaseForm)), out var ids)) wordforms.AddRange(ids);
        IEnumerable<int> rows;
        if (query.Wordforms.IsEmpty) rows = occ.Words;
        else rows = wordforms.Distinct().SelectMany(w => occ.ByWordform.GetValueOrDefault(w, [])).Order();
        var (start, end) = ScopeSpan(occ, query.Scope);
        var states = query.States.IsEmpty ? null : query.States.ToHashSet();
        var result = new List<int>();
        foreach (var position in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (position < start || position >= end) continue;
            if (states is null || states.Contains(StateOf(occ.Slots[position].Kind))) result.Add(position);
        }
        return result;
    }

    private (int Start, int End) ScopeSpan(OccurrenceProjection occ, OccurrenceScope scope) => scope switch
    {
        OccurrenceScope.ChapterOrText c => TextSpan(occ, c.Population),
        OccurrenceScope.Corpus { Population: var p } when p.CorpusId == Stamp.ProjectId => (0, occ.Slots.Length),
        OccurrenceScope.Corpus => throw new QueryRefused(new(RefusalCodes.HandAnalysisSourceUnavailable, FailureReason.Refused,
            "The corpus is not this project.")),
        _ => throw new ArgumentException("Unknown occurrence scope.", nameof(scope))
    };

    private IReadOnlyList<int> Uses(HandAnalysisProjection hand, OccurrenceQuery.Uses query, CancellationToken ct)
    {
        var occ = hand.Occurrences;
        var (start, end) = query.Scope is { } scope ? ScopeSpan(occ, scope) : (0, occ.Slots.Length);
        var positions = new SortedSet<int>();
        foreach (var target in query.Targets)
            foreach (var analysis in AnalysesOf(hand, target))
            {
                ct.ThrowIfCancellationRequested();
                if (analysis.Slots is { } direct)
                    foreach (var p in direct) positions.Add(p);
            }
        var result = positions.Where(p => p >= start && p < end).ToList();
        return result;
    }

    private static IEnumerable<(Guid Id, int[] Slots)> AnalysesOf(HandAnalysisProjection hand, ReferenceTarget target)
    {
        var occ = hand.Occurrences;
        var wfi = hand.Wfi;
        int[] Direct(Guid id) => occ.ByTarget.GetValueOrDefault(id, []);
        IEnumerable<(Guid, int[])> OfAnalysis(Guid analysis) => wfi.Readings.TryGetValue(analysis, out var r)
            ? [(analysis, Direct(analysis).Concat(r.Fact.Glosses.SelectMany(g => Direct(g.Id))).ToArray())] : [];
        switch (target.Kind)
        {
            case ReferenceKind.Wordform:
                return occ.ByWordform.TryGetValue(target.Id, out var words) ? [(target.Id, words)] : [];
            case ReferenceKind.Analysis: return OfAnalysis(target.Id);
            case ReferenceKind.Gloss: return [(target.Id, Direct(target.Id))];
            case ReferenceKind.Bundle:
                return wfi.BundleOwner.TryGetValue(target.Id, out var owner) ? OfAnalysis(owner) : [];
            case ReferenceKind.Entry:
                var lex = hand.Lexicon;
                if (!lex.EntryIndex.TryGetValue(target.Id, out var e)) return [];
                var fact = lex.Entries[e].Fact;
                var members = fact.Senses.Select(s => s.Id).Concat(fact.Msas.Select(m => m.Id))
                    .Concat(lex.FormsByEntry.GetValueOrDefault(target.Id, []).Select(i => lex.Forms[i].Hit.Id));
                return members.SelectMany(m => lex.AnalysesUsing.GetValueOrDefault(m, [])).Distinct().SelectMany(OfAnalysis);
            default:
                return hand.Lexicon.AnalysesUsing.GetValueOrDefault(target.Id, []).SelectMany(OfAnalysis);
        }
    }

    private static OccurrenceState StateOf(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Wordform => OccurrenceState.Bare,
        AttachmentKind.Analysis => OccurrenceState.Analysis,
        AttachmentKind.Gloss => OccurrenceState.Gloss,
        _ => OccurrenceState.Punctuation
    };

    private OccurrenceFact Fact(HandAnalysisProjection hand, int position)
    {
        var occ = hand.Occurrences;
        var slot = occ.Slots[position];
        var (wordform, analysis, gloss) = OccurrenceProjection.Resolve(slot, hand.Wfi);
        return new(Stamp, new HandAnalysisTextAnchor(slot.Text, slot.Paragraph, slot.Segment, slot.Index, slot.WordPosition),
            null, null, null, position, slot.SegmentStart, slot.SegmentEnd, StateOf(slot.Kind),
            new(slot.Kind, slot.Target), wordform, analysis is { } a ? new AnalysisRef.Native(a) : null, gloss, [], [], null, false, null,
            null, ImmutableDictionary<Guid, string>.Empty.Add(slot.Segment, occ.Fingerprint(slot.Segment)),
            occ.Filled.GetValueOrDefault(slot.Target, []),
            [new("occurrence-binding-unavailable", [], "Binding, wordform surface, translation and Source availability are not stored in this Home.")]);
    }

    private sealed class Span(int low, int high, bool forward) : IReadOnlyList<int>
    {
        public int Count => high - low;
        public int this[int i] => forward ? low + i : high - 1 - i;
        public IEnumerator<int> GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Reversible(int[] items, bool forward) : IReadOnlyList<int>
    {
        public int Count => items.Length;
        public int this[int i] => forward ? items[i] : items[^(i + 1)];
        public IEnumerator<int> GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
