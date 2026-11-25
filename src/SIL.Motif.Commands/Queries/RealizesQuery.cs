using System.Globalization;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;

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
            if (carried.Count > 0) hits.Add(new RealizesHit(set.Morpheme, set.Tier, [.. carried.Select(RealizesPatternCompiler.Show)]));
        }
        return hits;
    }
}

/// <summary>What a <c>realizes</c> query reads: the corpus it parses, the grammar to parse with, and the project holding stored analyses.</summary>
/// <param name="Corpus">The corpus the search runs over, whose word types the batch parse indexes.</param>
/// <param name="GrammarPath">The grammar PanGloss parses with; null to read stored analyses only.</param>
/// <param name="ProjectPath">A FieldWorks project whose stored analyses and lexicon are read; null to use the corpus parse only.</param>
/// <param name="FoldCase">Whether the corpus is case-folded, and so the forms searched for.</param>
/// <param name="IndexBuilder">Makes the builder of the batch parse index; a test replays a recording here.</param>
/// <param name="CacheRoot">The folder the parse index is kept in.</param>
/// <param name="Notice">Hears that the one-off index build is starting; the command line writes it to standard error.</param>
/// <param name="Grammar">Makes the resident grammar the <c>possible</c> and <c>generated</c> tiers ask; a test replays recordings here.</param>
/// <param name="Thorough">Whether the planner takes three test roots per environment instead of two.</param>
/// <param name="EntryMsas">The analyses of an entry id, when the caller already holds the project catalog; null reads them from the project.</param>
internal sealed record RealizesContext(CorpusWordList Corpus, string? GrammarPath, string? ProjectPath, bool FoldCase,
    Func<IRealizesIndexBuilder> IndexBuilder, string CacheRoot, Action<string> Notice,
    Func<IRealizesGrammar>? Grammar = null, bool Thorough = false, Func<string, IReadOnlyList<string>?>? EntryMsas = null);

/// <summary>
/// Resolves each <c>realizes</c> predicate of a query and compiles its variant set to a corpus search. The tier sources are the
/// seam: <c>attested</c> reads the corpus parse and the stored analyses, and the <c>possible</c> and <c>generated</c> sources will
/// register the same shape of answer, a <see cref="RealizesVariants"/>, so nothing downstream changes when they arrive.
/// </summary>
internal static class RealizesQuery
{
    /// <summary>The tiers that have a source today.</summary>
    internal static IReadOnlyCollection<string> Tiers { get; } = [RealizesTiers.Attested, RealizesTiers.Possible, RealizesTiers.Generated];

    /// <summary>The builder of the batch parse index for the grammar, or a refusal when PanGloss is missing.</summary>
    internal static IRealizesIndexBuilder BatchBuilder(string? parserPath)
    {
        var executable = parserPath ?? PanGlossExecutable.TryLocate();
        if (executable is null)
            throw new QueryRefusalException(new Refusal(RefusalCodes.WordTraceParserUnavailable, FailureReason.Refused,
                PanGlossExecutable.NotFoundMessage, ParserNotFoundFact.Mark(null, true)));
        return new PanGlossIndexBuilder(executable);
    }

    /// <summary>Lowers the expression; a refusal comes back as a <see cref="QueryRefusalException"/>.</summary>
    public static RealizesRun Prepare(QueryExpr where, RealizesContext context, CancellationToken cancellationToken, RealizesListed? listed = null)
    {
        var found = RealizesLowering.Find(where);
        foreach (var tier in found.Select(item => item.Node.Tier).Distinct())
            if (!Tiers.Contains(tier)) throw RealizesRefusals.TierNotYetAvailable(tier);
        BaselineReadCache? read = null;
        try
        {
            // Ids name a morpheme with no project; opening one costs minutes of LibLCM, so possible and generated skip it.
            var needsProject = found.Any(item => item.Node.Tier == RealizesTiers.Attested || !StripHintAndIds(item.Node.Morpheme));
            if (context.ProjectPath is { } project && needsProject) read = OpenProject(project);
            (RealizesParseIndex Index, bool Built, double Seconds)? index = null;
            var sets = new List<RealizesResult>();
            var seen = new Dictionary<QueryRealizes, (RealizesResult, IReadOnlyList<string>)>();
            Func<QueryRealizes, (RealizesResult, IReadOnlyList<string>)> resolveNode = node =>
            {
                if (seen.TryGetValue(node, out var done)) return done;
                var (text, asRoot) = StripHint(node.Morpheme);
                ResolvedMorpheme? resolved = null;
                RealizesTarget target;
                if (FormsMorphemeResolver.TryPair(text, out var entry, out var msa)) target = new RealizesTarget(entry, msa, null);
                else if (FormsMorphemeResolver.TryGuid(text, out var guid))
                {
                    // An entry id means any analysis of the entry, as morph(entry:) does; the parse names analyses, never entries.
                    var msas = context.EntryMsas?.Invoke(guid) ?? (read is null ? null : EntryMsas(read.Cache, guid));
                    target = msas is { Count: > 0 }
                        ? msas.Count == 1 ? new RealizesTarget(guid, msas[0], null)
                            : new RealizesTarget(guid, null, null, MsaIds: new HashSet<string>(msas, StringComparer.OrdinalIgnoreCase))
                        : new RealizesTarget(null, null, guid);
                }
                else
                {
                    if (read is null) throw RealizesRefusals.NeedsLexicon(node.Morpheme);
                    resolved = Resolve(read.Cache, text, asRoot);
                    target = new RealizesTarget(resolved.EntryGuid, resolved.MsaGuid, null);
                }
                if (resolved is null && read is not null && TryResolveIds(read.Cache, text, asRoot) is { } byId) resolved = byId;
                if (resolved is not null) target = target with { IsRoot = resolved.MsaClass == ResolvedMorpheme.StemClass };
                if (node.Tier is RealizesTiers.Possible or RealizesTiers.Generated)
                {
                    var possible = GrammarSet(node, resolved, target, text, context, cancellationToken);
                    sets.Add(possible.Item1);
                    return seen[node] = possible;
                }
                var parts = new List<RealizesVariants>();
                var finding = new List<string>();
                RealizesParseReport? report = null;
                if (resolved is not null && read is not null)
                {
                    var stored = RealizesAttested.Read(read.Cache, resolved);
                    parts.Add(stored);
                    finding.Add(stored.ProjectHasAnalyses ? "no stored analysis uses it" : "the project has no stored analyses");
                }
                if (context.GrammarPath is not null)
                {
                    var corpus = context.Corpus.Words.Select(word => (word.Form, word.Tokens)).ToList();
                    try
                    {
                        index ??= RealizesParseIndexCache.GetOrBuild(context.CacheRoot, context.GrammarPath, [.. corpus.Select(word => word.Form)],
                            context.IndexBuilder(), context.Notice, cancellationToken);
                    }
                    catch (RealizesIndexException failed) { throw RealizesRefusals.IndexFailed(failed.Message); }
                    var (variants, parse) = RealizesCorpusSource.Collect(index.Value.Index, (index.Value.Built, index.Value.Seconds),
                        target, corpus, cancellationToken);
                    parts.Add(variants);
                    report = parse;
                    finding.Add($"none of the {parse.Parsed} parsed word types has an analysis that contains it");
                }
                var merged = Merge(parts, resolved is not null);
                if (merged.Forms.Count == 0 && target.AnyId is { } anyId && resolved is null && !IndexCarries(index?.Index, anyId))
                    throw RealizesRefusals.IdUnknown(node.Morpheme, anyId, read is not null || context.EntryMsas is not null);
                if (merged.Forms.Count == 0 && listed?.Contains(node) == true)
                {
                    var absent = new RealizesResult(RealizesResult.SchemaName, Describe(resolved, target, text), node.Tier, false,
                        [new RealizesReason(RealizesLowering.AbsentReason, string.Join("; ", finding))], null, [],
                        RealizesPatternCompiler.Pattern([], merged.Anchor), report);
                    sets.Add(absent);
                    return seen[node] = (absent, []);
                }
                if (merged.Forms.Count == 0) throw RealizesRefusals.NoParses(node.Morpheme, string.Join("; ", finding));
                var forms = RealizesPatternCompiler.Normalize(merged.Terms ?? merged.Forms.Select(form => form.Form), context.FoldCase);
                var pattern = RealizesPatternCompiler.Pattern(forms, merged.Anchor);
                if (merged.Terms is not null) pattern = Broadness(pattern, forms, merged.Anchor, context.Corpus);
                var result = new RealizesResult(RealizesResult.SchemaName, Describe(resolved, target, text), node.Tier, merged.Complete,
                    merged.Reasons, merged.StopReason, merged.Forms, pattern, report);
                sets.Add(result);
                return seen[node] = (result, forms);
            };
            var lowered = RealizesLowering.Lower(where, node =>
            {
                try { return resolveNode(node); }
                catch (QueryRefusalException refused) when (listed?.Origins.TryGetValue(node, out _) == true)
                {
                    var origin = listed.Origins[node];
                    throw new QueryRefusalException(new Refusal(refused.Refusal.Code, refused.Refusal.Reason, origin + ": " + refused.Refusal.Message,
                        refused.Refusal.Facts.Concat([new KeyValuePair<string, string>("stemsLine", origin)]).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value),
                        refused.Refusal.ParserDiagnostic));
                }
            });
            var positive = RealizesLowering.Find(where).Where(item => !item.Negated).Select(item => seen[item.Node]).Distinct().ToList();
            return new RealizesRun(lowered, sets, positive);
        }
        finally
        {
            read?.Dispose();
        }
    }

    // The possible tier: the rules that may change the morpheme, the forms they compute, and patterns for the rest.
    private static bool StripHintAndIds(string morpheme)
    {
        var (text, _) = StripHint(morpheme);
        return FormsMorphemeResolver.TryPair(text, out _, out _) || FormsMorphemeResolver.TryGuid(text, out _);
    }

    private static (RealizesResult, IReadOnlyList<string>) GrammarSet(QueryRealizes node, ResolvedMorpheme? resolved, RealizesTarget target,
        string text, RealizesContext context, CancellationToken cancellationToken)
    {
        if (context.Grammar is null) throw QueryRefusalException.PhonNeedsGrammar();
        var grammar = context.Grammar();
        RealizesVariants variants;
        if (node.Tier == RealizesTiers.Possible) variants = RealizesPossible.Collect(grammar, target, cancellationToken, context.GrammarPath is { } known ? RealizesLexicon.Processes(known) : null);
        else
        {
            var lexicon = RealizesLexicon.Weighted(context.GrammarPath is { } path ? RealizesLexicon.Read(path) : [],
                context.Corpus.Words.Select(word => (word.Form, word.Tokens)));
            variants = RealizesGenerated.Collect(grammar, target, lexicon, AttestedNeighbours(context, target, lexicon), context.Thorough, cancellationToken, context.GrammarPath is { } known ? RealizesLexicon.Processes(known) : null);
        }
        return Seal(node, Describe(resolved, target, text), variants, context);
    }

    // The other morphs of the indexed words that carry the morpheme, by frequency; none when no index is cached.
    private static IReadOnlyList<PlannerCandidate> AttestedNeighbours(RealizesContext context, RealizesTarget target, IReadOnlyList<PlannerCandidate> lexicon)
    {
        if (context.GrammarPath is null || context.IndexBuilder is null) return [];
        RealizesParseIndex? index;
        try { index = RealizesParseIndexCache.TryLoad(context.CacheRoot, context.GrammarPath, context.Corpus.Words.Select(word => word.Form).ToList(), context.IndexBuilder().Identity); }
        catch (QueryRefusalException) { return []; }
        if (index is null) return [];
        var weight = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in context.Corpus.Words)
        {
            if (!index.Words.TryGetValue(word.Form, out var entry) || entry.Status is not (IndexedStatus.Ok)) continue;
            foreach (var analysis in entry.Analyses.Where(analysis => analysis.Morphs.Any(target.Matches)))
                foreach (var morph in analysis.Morphs.Where(morph => !target.Matches(morph)))
                    weight[morph.Msa] = weight.GetValueOrDefault(morph.Msa) + word.Tokens;
        }
        var byMsa = lexicon.GroupBy(candidate => candidate.MsaGuid, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return [.. weight.OrderByDescending(pair => pair.Value).Select(pair => byMsa.GetValueOrDefault(pair.Key)).OfType<PlannerCandidate>().Take(6)];
    }

    private static (RealizesResult, IReadOnlyList<string>) Seal(QueryRealizes node, string morpheme, RealizesVariants variants, RealizesContext context)
    {
        var searchable = variants.Forms.Where(form => form.Form != RealizesPossible.Unrealised).ToList();
        var forms = RealizesPatternCompiler.Normalize(searchable.Select(form => form.Form), context.FoldCase);
        var counted = variants.Forms.Select(form =>
        {
            if (form.Form == RealizesPossible.Unrealised) return form;
            var carried = context.Corpus.Words.Where(word => RealizesPatternCompiler.Carries(word.Form, form.Form, variants.Anchor)).Select(word => word.Form).ToList();
            return form with { Words = carried.Count, Examples = [.. carried.Take(3)] };
        }).ToList();
        var pattern = RealizesPatternCompiler.Pattern(forms, variants.Anchor);
        pattern = Broadness(pattern, forms, variants.Anchor, context.Corpus);
        var result = new RealizesResult(RealizesResult.SchemaName, morpheme, node.Tier, variants.Complete, variants.Reasons, variants.StopReason,
            counted, pattern, null, variants.Rules, variants.Phon, variants.Effects);
        return (result, forms);
    }

    private static BaselineReadCache OpenProject(string projectPath)
    {
        try { return BaselineReadCache.Open(projectPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new QueryRefusalException(new Refusal(RefusalCodes.QueryCorpusUnreadable, FailureReason.InvalidArgument,
                $"The project '{projectPath}' could not be opened to read its stored analyses: {exception.Message}"));
        }
    }

    /// <summary>The share of the corpus's word types one term may match before the search is flagged as over-broad.</summary>
    internal const double OverBroadShare = 0.2;

    // A term that names a whole word matches that word alone, so only the context terms can be broad.
    private static RealizesPattern Broadness(RealizesPattern pattern, IReadOnlyList<string> terms, string anchor, CorpusWordList corpus)
    {
        var words = corpus.Words;
        string? broadest = null;
        var most = 0;
        foreach (var term in terms.Where(term => !(term.StartsWith(RealizesPatternCompiler.StartMarker) && term.EndsWith(RealizesPatternCompiler.EndMarker))))
        {
            var count = words.Count(word => RealizesPatternCompiler.Carries(word.Form, term, anchor));
            if (count > most) (most, broadest) = (count, term);
        }
        return words.Count > 0 && most > OverBroadShare * words.Count
            ? pattern with { OverBroad = true, Broadest = RealizesPatternCompiler.Show(broadest!) + $" ({most} of {words.Count} word types)" }
            : pattern;
    }

    // Sources agree on a form by its text: the words add up, and a project gives the anchor from the morph type.
    private static RealizesVariants Merge(IReadOnlyList<RealizesVariants> parts, bool anchorFromProject)
    {
        var forms = parts.SelectMany(part => part.Forms).GroupBy(form => form.Form, StringComparer.Ordinal)
            .Select(group => new RealizesForm(group.Key, RealizesTiers.Attested,
                string.Join(",", group.Select(form => form.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                group.Sum(form => form.Words), [.. group.SelectMany(form => form.Examples).Distinct(StringComparer.Ordinal).Take(3)]))
            .OrderByDescending(form => form.Words).ThenBy(form => form.Form, StringComparer.Ordinal).ToList();
        var reasons = parts.SelectMany(part => part.Reasons).GroupBy(reason => reason.Code).Select(group => group.First()).ToList();
        var anchor = anchorFromProject ? parts[0].Anchor : parts.Count > 0 ? parts[^1].Anchor : "none";
        IReadOnlyList<string>? terms = null;
        if (parts.Any(part => part.Terms is not null))
        {
            terms = [.. parts.SelectMany(part => part.Terms ?? part.Forms.Select(form => RealizesPatternCompiler.Mark(form.Form, part.Anchor))).Distinct(StringComparer.Ordinal)];
            anchor = "context";
        }
        return new RealizesVariants(forms, anchor, reasons, parts.Select(part => part.StopReason).FirstOrDefault(reason => reason is not null), Terms: terms);
    }

    // The analyses of an entry in the project; null when the guid is no entry.
    private static IReadOnlyList<string>? EntryMsas(LcmCache cache, string guid) =>
        Guid.TryParse(guid, out var id) && cache.ServiceLocator.ObjectRepository.TryGetObject(id, out var found) && found is ILexEntry entry
            ? [.. entry.MorphoSyntaxAnalysesOC.Select(msa => msa.Guid.ToString("D", CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal)]
            : null;

    // Whether any morph of the parse names the id as its form or analysis.
    private static bool IndexCarries(RealizesParseIndex? index, string id) =>
        index is not null && index.Words.Values.Any(word => word.Analyses.Any(analysis => analysis.Morphs.Any(morph =>
            string.Equals(morph.Form, id, StringComparison.OrdinalIgnoreCase) || string.Equals(morph.Msa, id, StringComparison.OrdinalIgnoreCase))));

    private static string Describe(ResolvedMorpheme? resolved, RealizesTarget target, string text)
    {
        var id = resolved is not null ? resolved.EntryGuid + ":" + resolved.MsaGuid
            : target.AnyId ?? target.EntryId + (target.MsaId is null ? "" : ":" + target.MsaId);
        var label = resolved?.Form;
        return (label is { Length: > 0 } ? label + " " : "") + "(" + id + ")";
    }

    private static (string Text, bool? AsRoot) StripHint(string spec)
    {
        var text = spec.Trim();
        foreach (var (prefix, root) in new (string, bool?)[] { ("entry:", null), ("root:", true), ("stem:", true), ("pfx:", false), ("sfx:", false), ("afx:", false) })
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !FormsMorphemeResolver.TryPair(text, out _, out _))
                return (text[prefix.Length..], root);
        return (text, null);
    }

    // With a project the lexicon can name the morpheme too, giving its morph type and stored analyses.
    private static ResolvedMorpheme? TryResolveIds(LcmCache cache, string text, bool? asRoot)
    {
        var lookup = FormsMorphemeResolver.Resolve(cache, text, asRoot ?? false);
        if (lookup.Morpheme is not null) return lookup.Morpheme;
        return asRoot is null ? FormsMorphemeResolver.Resolve(cache, text, asRoot: true).Morpheme : null;
    }

    /// <summary>Names a morpheme by form in the lexicon: with no hint an affix is tried before a stem.</summary>
    internal static ResolvedMorpheme Resolve(LcmCache cache, string text, bool? asRoot)
    {
        var lookup = FormsMorphemeResolver.Resolve(cache, text, asRoot ?? false);
        if (lookup.Morpheme is null && asRoot is null && lookup.Refusal?.Code == RefusalCodes.FormsExplainMorphemeUnresolved)
        {
            var asStem = FormsMorphemeResolver.Resolve(cache, text, asRoot: true);
            if (asStem.Morpheme is not null) return asStem.Morpheme;
        }
        return lookup.Morpheme ?? throw new QueryRefusalException(lookup.Refusal!);
    }
}
