using System.Diagnostics;
using System.Globalization;
using SIL.Motif.Contract.Query;

namespace SIL.Motif.Commands.Queries;

/// <summary>A morpheme as PanGloss names it in a parse: by entry and analysis, or by one id that is either.</summary>
internal sealed record RealizesTarget(string? EntryId, string? MsaId, string? AnyId)
{
    public bool Matches(ParsedMorph morph) => AnyId is { } any
        ? string.Equals(morph.EntryId, any, StringComparison.OrdinalIgnoreCase) || string.Equals(morph.MsaId, any, StringComparison.OrdinalIgnoreCase)
        : string.Equals(morph.EntryId, EntryId, StringComparison.OrdinalIgnoreCase) &&
          (MsaId is null || string.Equals(morph.MsaId, MsaId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The corpus source of the <c>attested</c> tier: parses chosen words of an unannotated corpus with the grammar, keeps the analyses
/// that contain the morpheme, and takes the morpheme's surface piece from the shape PanGloss reports for the analysis.
/// </summary>
internal static class RealizesCorpusSource
{
    public const string ParseSource = "corpus-parse";
    public const string WholeWordSource = "corpus-parse-whole-word";

    public static (RealizesVariants Variants, RealizesParseReport Report) Collect(IRealizesParser parser, RealizesTarget target,
        IReadOnlyList<(string Form, long Tokens)> corpus, int sample, CancellationToken cancellationToken)
    {
        var chosen = RealizesSample.Choose(corpus, sample);
        var clock = Stopwatch.StartNew();
        var forms = new Dictionary<(string Form, string Source), SortedSet<string>>();
        int withMorpheme = 0, noAnalysis = 0, unsegmentable = 0, capped = 0, timedOut = 0, failed = 0, unknownPiece = 0;
        int atStart = 0, atEnd = 0, inside = 0;
        string? firstFailure = null;
        foreach (var word in chosen)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parse = parser.Parse(word.Form, cancellationToken);
            switch (parse.Status)
            {
                case WordParseStatus.Unsegmentable: unsegmentable++; continue;
                case WordParseStatus.Failed: failed++; firstFailure ??= parse.Detail; continue;
                case WordParseStatus.Capped: capped++; break;
                case WordParseStatus.TimedOut: timedOut++; break;
            }
            if (parse.Analyses.Count == 0) { noAnalysis++; continue; }
            var carried = false;
            foreach (var analysis in parse.Analyses)
            {
                var pieces = SurfaceShape.Pieces(word.Form, analysis.Surface, analysis.Morphs.Count);
                for (var i = 0; i < analysis.Morphs.Count; i++)
                {
                    if (!target.Matches(analysis.Morphs[i])) continue;
                    carried = true;
                    string form, source;
                    if (pieces is null) { form = word.Form; source = WholeWordSource; unknownPiece++; }
                    else
                    {
                        (form, var start) = pieces[i];
                        source = ParseSource;
                        if (start == 0 && form.Length < word.Form.Length) atStart++;
                        else if (start + form.Length == word.Form.Length && start > 0) atEnd++;
                        else inside++;
                    }
                    if (form.Length == 0) continue;
                    if (!forms.TryGetValue((form, source), out var words)) forms[(form, source)] = words = new SortedSet<string>(StringComparer.Ordinal);
                    words.Add(word.Form);
                }
            }
            if (carried) withMorpheme++;
        }
        var list = Merge(forms);
        var reasons = new List<RealizesReason>();
        if (chosen.Count < corpus.Count)
            reasons.Add(new RealizesReason("sampled", Format($"{chosen.Count} of {corpus.Count} word types were parsed, chosen by frequency; a form only the other words carry is not in the set.")));
        if (capped + timedOut > 0)
            reasons.Add(new RealizesReason("parse_stopped", Format($"{capped} words stopped at the parser's work limit and {timedOut} at its word timeout, so analyses may be missing.")));
        if (unsegmentable > 0)
            reasons.Add(new RealizesReason("unsegmentable", Format($"{unsegmentable} words could not be segmented with the grammar's characters, so they were not parsed.")));
        if (failed > 0)
            reasons.Add(new RealizesReason("parse_failed", Format($"{failed} words got no answer from the resident parser") + (firstFailure is null ? "." : ": " + firstFailure)));
        if (unknownPiece > 0)
            reasons.Add(new RealizesReason("piece_unknown", Format($"{unknownPiece} analyses gave no surface piece for the morpheme, so the whole word stands in and the search finds only that word.")));
        var anchor = atStart > 0 && atEnd == 0 && inside == 0 ? "start" : atEnd > 0 && atStart == 0 && inside == 0 ? "end" : "none";
        var report = new RealizesParseReport(corpus.Count, chosen.Count, withMorpheme, noAnalysis, unsegmentable, capped, timedOut, failed,
            chosen.Count < corpus.Count ? sample : null, Math.Round(clock.Elapsed.TotalSeconds, 1));
        return (new RealizesVariants(list, anchor, reasons), report);
    }

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static List<RealizesForm> Merge(Dictionary<(string Form, string Source), SortedSet<string>> forms) =>
    [
        .. forms.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key.Form, StringComparer.Ordinal)
            .Select(pair => new RealizesForm(pair.Key.Form, RealizesTiers.Attested, pair.Key.Source, pair.Value.Count, [.. pair.Value.Take(3)])),
    ];
}
