using SIL.LCModel;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Baselines;

namespace SIL.Motif.Commands.Queries;

/// <summary>A <c>realizes</c> query made runnable: the lowered expression, each variant set it searches for, and the labels of a hit.</summary>
/// <param name="lowered">The expression with every <c>realizes</c> replaced by the field predicates that search for its forms.</param>
/// <param name="sets">One result per distinct predicate, in the order written.</param>
/// <param name="positive">The sets whose forms label a hit: those not under a <c>not</c>, with their normalised forms.</param>
internal sealed class RealizesRun(QueryExpr? lowered, IReadOnlyList<RealizesResult> sets,
    IReadOnlyList<(RealizesResult Set, IReadOnlyList<string> Forms)> positive)
{
    public QueryExpr? Lowered { get; } = lowered;

    public IReadOnlyList<RealizesResult> Sets { get; } = sets;

    /// <summary>The sets a word is labelled with the forms it carries; null when no set counts toward a hit (every predicate is under <c>not</c>).</summary>
    public IReadOnlyList<RealizesHit>? HitsFor(string word)
    {
        if (positive.Count == 0) return null;
        var hits = new List<RealizesHit>();
        foreach (var (set, forms) in positive)
        {
            var carried = forms.Where(form => RealizesPatternCompiler.Carries(word, form, set.Pattern.Anchor)).ToList();
            if (carried.Count > 0) hits.Add(new RealizesHit(set.Morpheme, set.Tier, carried));
        }
        return hits;
    }
}

/// <summary>
/// Resolves each <c>realizes</c> predicate of a query against a project and compiles its variant set to a corpus search. The tier
/// sources are the seam: <see cref="Sources"/> holds <c>attested</c>, and the <c>possible</c> and <c>generated</c> sources
/// register the same shape of answer, a <see cref="RealizesVariants"/>, so nothing downstream changes when they arrive.
/// </summary>
internal static class RealizesQuery
{
    /// <summary>The tier sources that exist, by tier.</summary>
    internal static IReadOnlyDictionary<string, Func<LcmCache, ResolvedMorpheme, RealizesVariants>> Sources { get; } =
        new Dictionary<string, Func<LcmCache, ResolvedMorpheme, RealizesVariants>>(StringComparer.Ordinal)
        {
            [RealizesTiers.Attested] = RealizesAttested.Read,
        };

    /// <summary>Lowers the expression against the project at <paramref name="projectPath"/>; a refusal comes back as a <see cref="QueryRefusalException"/>.</summary>
    public static RealizesRun Prepare(QueryExpr where, string projectPath, bool foldCase)
    {
        var found = RealizesLowering.Find(where);
        foreach (var tier in found.Select(item => item.Node.Tier).Distinct())
            if (!Sources.ContainsKey(tier)) throw RealizesRefusals.TierNotYetAvailable(tier);
        BaselineReadCache read;
        try { read = BaselineReadCache.Open(projectPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new QueryRefusalException(new Refusal(RefusalCodes.QueryCorpusUnreadable, FailureReason.InvalidArgument,
                $"The project '{projectPath}' could not be opened to read its stored analyses: {exception.Message}"));
        }
        using (read)
        {
            var sets = new List<RealizesResult>();
            var seen = new Dictionary<QueryRealizes, (RealizesResult, IReadOnlyList<string>)>();
            var lowered = RealizesLowering.Lower(where, node =>
            {
                if (seen.TryGetValue(node, out var done)) return done;
                var morpheme = Resolve(read.Cache, node.Morpheme);
                var variants = Sources[node.Tier](read.Cache, morpheme);
                if (variants.Forms.Count == 0) throw RealizesRefusals.NoParses(node.Morpheme, variants.ProjectHasAnalyses);
                var forms = RealizesPatternCompiler.Normalize(variants.Forms.Select(form => form.Form), foldCase);
                var result = new RealizesResult(RealizesResult.SchemaName, Describe(morpheme), node.Tier, variants.Complete,
                    variants.Reasons, variants.StopReason, variants.Forms, RealizesPatternCompiler.Pattern(forms, variants.Anchor));
                sets.Add(result);
                return seen[node] = (result, forms);
            });
            var positive = RealizesLowering.Find(where).Where(item => !item.Negated).Select(item => seen[item.Node]).Distinct().ToList();
            return new RealizesRun(lowered, sets, positive);
        }
    }

    private static string Describe(ResolvedMorpheme morpheme) =>
        (morpheme.Form is { Length: > 0 } form ? form + " " : "") + "(" + morpheme.EntryGuid + ":" + morpheme.MsaGuid + ")";

    /// <summary>Names the morpheme: a kind hint (<c>root:</c>, <c>pfx:</c>, <c>sfx:</c>) picks a stem or an affix; with none, an affix is tried before a stem.</summary>
    internal static ResolvedMorpheme Resolve(LcmCache cache, string spec)
    {
        var text = spec.Trim();
        bool? asRoot = null;
        foreach (var (prefix, root) in new[] { ("root:", true), ("stem:", true), ("pfx:", false), ("sfx:", false), ("afx:", false) })
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !FormsMorphemeResolver.TryPair(text, out _, out _))
            {
                asRoot = root;
                text = text[prefix.Length..];
                break;
            }
        var lookup = FormsMorphemeResolver.Resolve(cache, text, asRoot ?? false);
        if (lookup.Morpheme is null && asRoot is null && lookup.Refusal?.Code == RefusalCodes.FormsExplainMorphemeUnresolved)
        {
            var asStem = FormsMorphemeResolver.Resolve(cache, text, asRoot: true);
            if (asStem.Morpheme is not null) return asStem.Morpheme;
        }
        return lookup.Morpheme ?? throw new QueryRefusalException(lookup.Refusal!);
    }
}
