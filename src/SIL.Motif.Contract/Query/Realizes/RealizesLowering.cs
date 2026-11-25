using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Query;

/// <summary>The refusals of <c>realizes</c>, each with a typed code and the facts an agent needs to act on it.</summary>
public static class RealizesRefusals
{
    /// <summary><c>not realizes(...)</c> over a set that is not complete: the absence it would claim is not proven.</summary>
    public static QueryRefusalException NegationIncomplete(RealizesResult set) => new(new Refusal(
        RefusalCodes.QueryRealizesNegationIncomplete, FailureReason.Refused,
        $"not realizes({set.Morpheme}, {set.Tier}) is refused: the {set.Tier} set is not complete ({string.Join(", ", set.Reasons.Select(r => r.Code))}), " +
        "so no form of the morpheme being absent from a word would be a claim the set cannot back.",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["morpheme"] = set.Morpheme, ["tier"] = set.Tier, ["reasons"] = string.Join(",", set.Reasons.Select(r => r.Code)),
        }));

    /// <summary>The attested tier found no word that carries the morpheme; <paramref name="finding"/> says what was searched.</summary>
    public static QueryRefusalException NoParses(string morpheme, string finding) => new(new Refusal(
        RefusalCodes.QueryRealizesNoParses, FailureReason.Refused,
        $"realizes({morpheme}, attested) found no analysis that uses {morpheme}: {finding}. Name a project with stored analyses (--project), or ask for tier possible or generated.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["morpheme"] = morpheme, ["tier"] = RealizesTiers.Attested }));

    /// <summary>The parse index of the corpus could not be built; <paramref name="detail"/> carries the stderr tail or the line number.</summary>
    public static QueryRefusalException IndexFailed(string detail) => new(new Refusal(
        RefusalCodes.QueryRealizesIndexFailed, FailureReason.Refused,
        $"realizes could not build the parse index of the corpus with the grammar: {detail}",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["detail"] = detail }));

    /// <summary><c>realizes</c> needs a grammar to parse the corpus with, a project to read stored analyses from, or both.</summary>
    public static QueryRefusalException NeedsProject() => new(new Refusal(
        RefusalCodes.QueryRealizesNeedsProject, FailureReason.InvalidArgument,
        "realizes finds a morpheme in a corpus by parsing it with a grammar or by reading a project's stored analyses, and this query names neither. " +
        "Pass --grammar <path> to parse the corpus, --project <fwdata> to read stored analyses, or both.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["flag"] = "--grammar" }));

    /// <summary>A morpheme named by its form can only be found in a project's lexicon.</summary>
    public static QueryRefusalException NeedsLexicon(string morpheme) => new(new Refusal(
        RefusalCodes.QueryRealizesNeedsProject, FailureReason.InvalidArgument,
        $"'{morpheme}' names a morpheme by its form, and only a project's lexicon can say which entry that is. Pass --project <fwdata>, or name it as entry:msa.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["flag"] = "--project", ["morpheme"] = morpheme }));

    /// <summary>PanGloss answers about one morpheme named by its entry and analysis ids.</summary>
    public static QueryRefusalException NeedsIdentity(string morpheme) => new(new Refusal(
        RefusalCodes.QueryRealizesNeedsIdentity, FailureReason.InvalidArgument,
        $"realizes tier possible and generated ask PanGloss about {morpheme} by its entry and analysis ids. Name it as entry:msa, or give a form and --project so the lexicon can name it.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["morpheme"] = morpheme }));

    /// <summary>The tier is named in the schema but its variant source is not built yet.</summary>
    public static QueryRefusalException TierNotYetAvailable(string tier) => QueryRefusalException.NotYetAvailable("realizes tier " + tier);
}

/// <summary>
/// Replaces each <c>realizes</c> predicate by the field predicates that search for its variant set, refusing a negation over a set
/// that is not complete. The expression it returns runs on any source that has a <c>form</c> column.
/// </summary>
public static class RealizesLowering
{
    /// <summary>The longest alternation one lowered predicate carries; the field engine's own limit on a regular expression.</summary>
    public const int ChunkLimit = FormQuery.MaximumRegexLength - 16;

    /// <summary>Every <c>realizes</c> predicate of the expression, with whether a <c>not</c> encloses it an odd number of times.</summary>
    public static List<(QueryRealizes Node, bool Negated)> Find(QueryExpr? expr)
    {
        var found = new List<(QueryRealizes, bool)>();
        Walk(expr, false);
        return found;

        void Walk(QueryExpr? node, bool negated)
        {
            switch (node)
            {
                case QueryRealizes realizes: found.Add((realizes, negated)); break;
                case QueryAnd and: foreach (var item in and.Items) Walk(item, negated); break;
                case QueryOr or: foreach (var item in or.Items) Walk(item, negated); break;
                case QueryNot not: Walk(not.Item, !negated); break;
            }
        }
    }

    /// <summary>Whether the expression holds a <c>realizes</c> predicate.</summary>
    public static bool Has(QueryExpr? expr) => Find(expr).Count > 0;

    /// <summary>
    /// Lowers the expression. <paramref name="resolve"/> gives the normalised forms and anchor to search for per predicate; a
    /// predicate under <c>not</c> whose set is incomplete is refused.
    /// </summary>
    public static QueryExpr? Lower(QueryExpr? expr, Func<QueryRealizes, (RealizesResult Set, IReadOnlyList<string> Forms)> resolve) =>
        expr is null ? null : Visit(expr, false, resolve);

    private static QueryExpr Visit(QueryExpr node, bool negated, Func<QueryRealizes, (RealizesResult, IReadOnlyList<string>)> resolve)
    {
        switch (node)
        {
            case QueryRealizes realizes:
                var (set, forms) = resolve(realizes);
                if (forms.Count == 0 && (set.Phon?.Count ?? 0) == 0) throw RealizesRefusals.NoParses(set.Morpheme, "no form was found");
                if (negated && !set.Complete) throw RealizesRefusals.NegationIncomplete(set);
                IReadOnlyList<string> chunks = forms.Count == 0 ? [] : RealizesPatternCompiler.Chunks(forms, set.Pattern.Anchor, ChunkLimit);
                var fields = chunks.Select(chunk => (QueryExpr)new QueryField("form", QueryOps.Contains, chunk)).ToList();
                foreach (var clause in set.Phon ?? [])
                {
                    QueryExpr search = clause.Pattern.Contains(" / ", StringComparison.Ordinal) ? new QueryEnv(clause.Pattern) : new QueryPhon(clause.Pattern);
                    fields.Add(clause.Context is null ? search : new QueryAnd([search, new QueryField("form", QueryOps.Contains, clause.Context)]));
                }
                return fields.Count == 1 ? fields[0] : new QueryOr(fields);
            case QueryAnd and: return new QueryAnd([.. and.Items.Select(item => Visit(item, negated, resolve))]);
            case QueryOr or: return new QueryOr([.. or.Items.Select(item => Visit(item, negated, resolve))]);
            case QueryNot not: return new QueryNot(Visit(not.Item, !negated, resolve));
            default: return node;
        }
    }
}
