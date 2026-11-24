using System.Diagnostics;
using System.Globalization;
using SIL.Motif.Contract.Query;

namespace SIL.Motif.Commands.Queries;

/// <summary>A morpheme as PanGloss names it in a parse: by entry and analysis, or by one id that is either.</summary>
internal sealed record RealizesTarget(string? EntryId, string? MsaId, string? AnyId, bool? IsRoot = null)
{
    /// <summary>Whether a morph of a trace names the morpheme.</summary>
    public bool Matches(ParsedMorph morph) => AnyId is { } any
        ? string.Equals(morph.EntryId, any, StringComparison.OrdinalIgnoreCase) || string.Equals(morph.MsaId, any, StringComparison.OrdinalIgnoreCase)
        : string.Equals(morph.EntryId, EntryId, StringComparison.OrdinalIgnoreCase) &&
          (MsaId is null || string.Equals(morph.MsaId, MsaId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a morph of the batch index names the morpheme; the batch gives a form and an analysis id, not the entry.</summary>
    public bool Matches(IndexedMorph morph) => AnyId is { } any
        ? string.Equals(morph.Form, any, StringComparison.OrdinalIgnoreCase) || string.Equals(morph.Msa, any, StringComparison.OrdinalIgnoreCase)
        : string.Equals(morph.Msa, MsaId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The corpus source of the <c>attested</c> tier, in two stages. The parse index says which word types the grammar analyses with the
/// morpheme; then only those words (up to the sample size, most frequent first by weighted choice) are traced to read the morpheme's
/// surface piece from the shape PanGloss reports. A matching word that is not traced, or whose trace shows no piece, stands in whole.
/// </summary>
internal static class RealizesCorpusSource
{
    public const string ParseSource = "corpus-parse";
    public const string WholeWordSource = "corpus-parse-whole-word";

    public static (RealizesVariants Variants, RealizesParseReport Report) Collect(RealizesParseIndex index, (bool Built, double Seconds) built,
        Func<IRealizesParser> tracer, RealizesTarget target, IReadOnlyList<(string Form, long Tokens)> corpus, int sample,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        int noAnalysis = 0, unsegmentable = 0, capped = 0, timedOut = 0, failed = 0, unknownPiece = 0, traceFailed = 0;
        var matches = new List<(string Form, long Tokens)>();
        foreach (var word in corpus)
        {
            var entry = index.Words.GetValueOrDefault(word.Form) ?? new IndexedWord(IndexedStatus.Failed, []);
            switch (entry.Status)
            {
                case IndexedStatus.Unsegmentable: unsegmentable++; continue;
                case IndexedStatus.Failed: failed++; continue;
                case IndexedStatus.Capped: capped++; break;
                case IndexedStatus.TimedOut: timedOut++; break;
            }
            if (entry.Analyses.Count == 0) { noAnalysis++; continue; }
            if (entry.Analyses.Any(analysis => analysis.Any(target.Matches))) matches.Add(word);
        }
        var chosen = RealizesSample.Choose(matches, sample);
        var traced = chosen.Select(word => word.Form).ToHashSet(StringComparer.Ordinal);
        var forms = new Dictionary<(string Form, string Source), SortedSet<string>>();
        int atStart = 0, atEnd = 0, inside = 0;
        var terms = new HashSet<string>(StringComparer.Ordinal);
        void Add(string form, string source, string word, int start = -1)
        {
            if (form.Length == 0) return;
            terms.Add(Term(word, form, start, target));
            if (!forms.TryGetValue((form, source), out var words)) forms[(form, source)] = words = new SortedSet<string>(StringComparer.Ordinal);
            words.Add(word);
        }
        IRealizesParser? parser = null;
        foreach (var word in chosen)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parser ??= tracer();
            var parse = parser.Parse(word.Form, cancellationToken);
            var found = false;
            foreach (var analysis in parse.Analyses)
            {
                var pieces = SurfaceShape.Pieces(word.Form, analysis.Surface, analysis.Morphs.Count);
                for (var i = 0; i < analysis.Morphs.Count; i++)
                {
                    if (!target.Matches(analysis.Morphs[i]) || pieces is null) continue;
                    var (form, start) = pieces[i];
                    found = true;
                    if (start == 0 && form.Length < word.Form.Length) atStart++;
                    else if (start + form.Length == word.Form.Length && start > 0) atEnd++;
                    else inside++;
                    Add(form, ParseSource, word.Form, start);
                }
            }
            if (found) continue;
            if (parse.Analyses.Count == 0) traceFailed++;
            unknownPiece++;
            Add(word.Form, WholeWordSource, word.Form);
        }
        foreach (var word in matches.Where(word => !traced.Contains(word.Form))) Add(word.Form, WholeWordSource, word.Form);
        var reasons = new List<RealizesReason>();
        var untraced = matches.Count - chosen.Count;
        if (untraced > 0)
            reasons.Add(new RealizesReason("trace_capped", Format($"{chosen.Count} of {matches.Count} matching word types were traced for the morpheme's surface piece, chosen by frequency; the other {untraced} stand in whole, and a form only they carry is not in the set. Raise --sample, or pass 0 to trace every match.")));
        if (capped + timedOut > 0)
            reasons.Add(new RealizesReason("parse_stopped", Format($"{capped} words stopped at the parser's work limit and {timedOut} at its word timeout, so analyses may be missing.")));
        if (unsegmentable > 0)
            reasons.Add(new RealizesReason("unsegmentable", Format($"{unsegmentable} words could not be segmented with the grammar's characters, so they were not parsed.")));
        if (failed > 0)
            reasons.Add(new RealizesReason("parse_failed", Format($"{failed} words got no answer from the batch parse.")));
        if (unknownPiece > 0)
            reasons.Add(new RealizesReason("piece_unaligned", Format($"{unknownPiece} traced words have no analysis whose morph pieces add up to the word, so the morpheme's own piece is not proven ({traceFailed} had no analysis in the trace), so the whole word stands in and the search finds only that word.")));
        var anchor = atStart > 0 && atEnd == 0 && inside == 0 ? "start" : atEnd > 0 && atStart == 0 && inside == 0 ? "end" : "none";
        var report = new RealizesParseReport(corpus.Count, corpus.Count - failed, matches.Count, noAnalysis, unsegmentable, capped, timedOut, failed,
            untraced > 0 ? sample : null, Math.Round(clock.Elapsed.TotalSeconds + built.Seconds, 1), chosen.Count, built.Built, built.Seconds);
        return (new RealizesVariants(Merge(forms), anchor, reasons, Terms: [.. terms.Order(StringComparer.Ordinal)]), report);
    }

    /// <summary>
    /// The search term for one piece: the piece with the context its open side attested. A piece at the start of its word is a
    /// prefix, searched as the word start through the piece; any later piece is searched with what follows it to the word end, so
    /// <c>a</c> of tinu+a+ng is <c>ang$</c>. A whole word stands for itself exactly. A root of more than two characters is bare.
    /// </summary>
    internal static string Term(string word, string piece, int start, RealizesTarget target)
    {
        if (start < 0) return RealizesPatternCompiler.Mark(word, "start") + RealizesPatternCompiler.EndMarker;
        if (target.IsRoot == true && piece.Length > 2) return piece;
        return start == 0 ? RealizesPatternCompiler.Mark(word[..(start + piece.Length)], "start")
            : RealizesPatternCompiler.Mark(word[start..], "end");
    }

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static List<RealizesForm> Merge(Dictionary<(string Form, string Source), SortedSet<string>> forms) =>
    [
        .. forms.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key.Form, StringComparer.Ordinal)
            .Select(pair => new RealizesForm(pair.Key.Form, RealizesTiers.Attested, pair.Key.Source, pair.Value.Count, [.. pair.Value.Take(3)])),
    ];
}
