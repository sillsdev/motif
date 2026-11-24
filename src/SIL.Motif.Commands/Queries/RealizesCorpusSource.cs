using System.Diagnostics;
using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Query;

namespace SIL.Motif.Commands.Queries;

/// <summary>A morpheme as PanGloss names it in a parse: by entry and analysis, or by one id that is either.</summary>
internal sealed record RealizesTarget(string? EntryId, string? MsaId, string? AnyId, bool? IsRoot = null)
{
    /// <summary>Whether a morph of the index names the morpheme; the batch gives a form and an analysis id, not the entry.</summary>
    public bool Matches(IndexedMorph morph) => AnyId is { } any
        ? string.Equals(morph.Form, any, StringComparison.OrdinalIgnoreCase) || string.Equals(morph.Msa, any, StringComparison.OrdinalIgnoreCase)
        : string.Equals(morph.Msa, MsaId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The corpus source of the <c>attested</c> tier. The parse index (one <c>pangloss batch --analyses --morph-spans</c> over the corpus) says which
/// word types the grammar analyses with the morpheme and where on the surface it sits; each matching word gives its piece directly. A
/// morph PanGloss could not place (a typed null) or whose spans do not tile the surface stands in as its whole word, with the set incomplete.
/// </summary>
internal static class RealizesCorpusSource
{
    public const string ParseSource = "corpus-parse";
    public const string WholeWordSource = "corpus-parse-whole-word";

    public static (RealizesVariants Variants, RealizesParseReport Report) Collect(RealizesParseIndex index, (bool Built, double Seconds) built,
        RealizesTarget target, IReadOnlyList<(string Form, long Tokens)> corpus, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        int noAnalysis = 0, unsegmentable = 0, capped = 0, timedOut = 0, failed = 0, matched = 0, placed = 0, wholeWord = 0, inconsistent = 0;
        var unplacedReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var unplacedWords = 0;
        var forms = new Dictionary<(string Form, string Source), SortedSet<string>>();
        int atStart = 0, atEnd = 0, inside = 0;
        var terms = new HashSet<string>(StringComparer.Ordinal);
        void Add(string form, string source, string word, string term)
        {
            if (form.Length == 0) return;
            terms.Add(term);
            if (!forms.TryGetValue((form, source), out var words)) forms[(form, source)] = words = new SortedSet<string>(StringComparer.Ordinal);
            words.Add(word);
        }
        foreach (var word in corpus)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = index.Words.GetValueOrDefault(word.Form) ?? new IndexedWord(IndexedStatus.Failed, []);
            switch (entry.Status)
            {
                case IndexedStatus.Unsegmentable: unsegmentable++; continue;
                case IndexedStatus.Failed: failed++; continue;
                case IndexedStatus.Capped: capped++; break;
                case IndexedStatus.TimedOut: timedOut++; break;
            }
            if (entry.Analyses.Count == 0) { noAnalysis++; continue; }
            var carriers = entry.Analyses.Where(analysis => analysis.Morphs.Any(target.Matches)).ToList();
            if (carriers.Count == 0) continue;
            matched++;
            var found = false;
            var standIn = false;
            var reasons = new HashSet<string>(StringComparer.Ordinal);
            var original = word.Form.Normalize(NormalizationForm.FormD);
            foreach (var analysis in carriers)
                foreach (var morph in analysis.Morphs.Where(target.Matches))
                {
                    if (analysis.Problem is not null) { standIn = true; reasons.Add("inconsistent"); continue; }
                    if (morph.Runs is null) { standIn = true; reasons.Add(morph.Reason ?? "morph_spans_null"); continue; }
                    if (morph.Runs.Count == 0 || morph.Runs.All(run => run.Text.Length == 0)) { standIn = true; reasons.Add("no_segment_left"); continue; }
                    // The word's own case is kept when its NFD spelling lines up with the surface.
                    var text = original.Length == analysis.Surface.Length ? original : analysis.Surface;
                    foreach (var run in morph.Runs)
                    {
                        if (run.Text.Length == 0 || run.Start < 0 || run.Start + run.Text.Length > text.Length) { standIn = true; reasons.Add("inconsistent"); continue; }
                        var piece = text.Substring(run.Start, run.Text.Length).Normalize(NormalizationForm.FormC);
                        found = true;
                        if (run.Start == 0 && run.Text.Length < text.Length) atStart++;
                        else if (run.Start + run.Text.Length == text.Length && run.Start > 0) atEnd++;
                        else inside++;
                        Add(piece, ParseSource, word.Form, Term(text, run.Text, run.Start, target));
                    }
                }
            if (found) placed++;
            if (standIn || !found)
            {
                wholeWord++;
                if (reasons.Contains("inconsistent") && reasons.Count == 1) inconsistent++;
                else { unplacedWords++; foreach (var reason in reasons.Where(reason => reason != "inconsistent")) unplacedReasons[reason] = unplacedReasons.GetValueOrDefault(reason) + 1; }
                Add(word.Form, WholeWordSource, word.Form, Term(word.Form, word.Form, -1, target));
            }
        }
        var list = new List<RealizesReason>();
        if (capped + timedOut > 0)
            list.Add(new RealizesReason("parse_stopped", Format($"{capped} words stopped at the parser's work limit and {timedOut} at its word timeout, so analyses may be missing.")));
        if (unsegmentable > 0)
            list.Add(new RealizesReason("unsegmentable", Format($"{unsegmentable} words could not be segmented with the grammar's characters, so they were not parsed.")));
        if (failed > 0)
            list.Add(new RealizesReason("parse_failed", Format($"{failed} words got no answer from the batch parse.")));
        if (unplacedWords > 0)
            list.Add(new RealizesReason("piece_unplaced", Format($"{unplacedWords} matching words have a reading where PanGloss placed no surface span for the morpheme ({string.Join(", ", unplacedReasons.Select(pair => $"{pair.Key} {pair.Value}"))}), so the whole word stands in and the search finds only that word.")));
        if (inconsistent > 0)
            list.Add(new RealizesReason("piece_inconsistent", Format($"{inconsistent} matching words have spans that do not add up to the word's surface, so the morpheme's piece is not trusted and the whole word stands in.")));
        var anchor = atStart > 0 && atEnd == 0 && inside == 0 ? "start" : atEnd > 0 && atStart == 0 && inside == 0 ? "end" : "none";
        var report = new RealizesParseReport(corpus.Count, corpus.Count - failed, matched, noAnalysis, unsegmentable, capped, timedOut, failed,
            Math.Round(clock.Elapsed.TotalSeconds + built.Seconds, 1), placed, wholeWord, built.Built, built.Seconds);
        return (new RealizesVariants(Merge(forms), anchor, list, Terms: [.. terms.Order(StringComparer.Ordinal)]), report);
    }

    /// <summary>
    /// The search term for one piece: the piece with the context its open side attested. A piece at the start of its word is a
    /// prefix, searched as the word start through the piece; any later piece is searched with what follows it to the word end, so
    /// <c>a</c> of tinu+a+ng is <c>ang$</c>. A whole word stands for itself exactly. A root of more than two characters is bare.
    /// </summary>
    internal static string Term(string word, string piece, int start, RealizesTarget target)
    {
        if (start < 0) return RealizesPatternCompiler.Mark(Nfc(word), "start") + RealizesPatternCompiler.EndMarker;
        if (target.IsRoot == true && piece.Length > 2) return Nfc(piece);
        return start == 0 ? RealizesPatternCompiler.Mark(Nfc(word[..(start + piece.Length)]), "start")
            : RealizesPatternCompiler.Mark(Nfc(word[start..]), "end");
    }

    private static string Nfc(string text) => text.Normalize(NormalizationForm.FormC);

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static List<RealizesForm> Merge(Dictionary<(string Form, string Source), SortedSet<string>> forms) =>
    [
        .. forms.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key.Form, StringComparer.Ordinal)
            .Select(pair => new RealizesForm(pair.Key.Form, RealizesTiers.Attested, pair.Key.Source, pair.Value.Count, [.. pair.Value.Take(3)])),
    ];
}
