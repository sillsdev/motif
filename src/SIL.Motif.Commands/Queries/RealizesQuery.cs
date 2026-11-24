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
            if (carried.Count > 0) hits.Add(new RealizesHit(set.Morpheme, set.Tier, carried));
        }
        return hits;
    }
}

/// <summary>What a <c>realizes</c> query reads: the corpus it parses, the grammar to parse with, and the project holding stored analyses.</summary>
/// <param name="Corpus">The corpus the search runs over, whose word types a parse samples.</param>
/// <param name="GrammarPath">The grammar PanGloss parses with; null to read stored analyses only.</param>
/// <param name="ProjectPath">A FieldWorks project whose stored analyses and lexicon are read; null to use the corpus parse only.</param>
/// <param name="FoldCase">Whether the corpus is case-folded, and so the forms searched for.</param>
/// <param name="Sample">How many word types to parse; zero for every type.</param>
/// <param name="Parser">Makes the parser for the grammar; a test replays recordings here.</param>
internal sealed record RealizesContext(CorpusWordList Corpus, string? GrammarPath, string? ProjectPath, bool FoldCase, int Sample,
    Func<IRealizesParser> Parser);

/// <summary>
/// Resolves each <c>realizes</c> predicate of a query and compiles its variant set to a corpus search. The tier sources are the
/// seam: <c>attested</c> reads the corpus parse and the stored analyses, and the <c>possible</c> and <c>generated</c> sources will
/// register the same shape of answer, a <see cref="RealizesVariants"/>, so nothing downstream changes when they arrive.
/// </summary>
internal static class RealizesQuery
{
    /// <summary>The tiers that have a source today.</summary>
    internal static IReadOnlyCollection<string> Tiers { get; } = [RealizesTiers.Attested];

    /// <summary>The parser the context uses for the grammar at <paramref name="grammarPath"/>, or a refusal when PanGloss is missing.</summary>
    internal static IRealizesParser ResidentParser(string grammarPath, string? parserPath)
    {
        var executable = parserPath ?? PanGlossExecutable.TryLocate();
        if (executable is null)
            throw new QueryRefusalException(new Refusal(RefusalCodes.WordTraceParserUnavailable, FailureReason.Refused,
                PanGlossExecutable.NotFoundMessage, ParserNotFoundFact.Mark(null, true)));
        if (!File.Exists(grammarPath))
            throw new QueryRefusalException(new Refusal(RefusalCodes.QueryCorpusUnreadable, FailureReason.InvalidArgument,
                $"The grammar '{grammarPath}' is not a file."));
        var full = Path.GetFullPath(grammarPath);
        return new ResidentRealizesParser(TryWordSessions.PoolFor(executable), full, BatchInvocationEvidence.DigestFile(full), full);
    }

    /// <summary>Lowers the expression; a refusal comes back as a <see cref="QueryRefusalException"/>.</summary>
    public static RealizesRun Prepare(QueryExpr where, RealizesContext context, CancellationToken cancellationToken)
    {
        var found = RealizesLowering.Find(where);
        foreach (var tier in found.Select(item => item.Node.Tier).Distinct())
            if (!Tiers.Contains(tier)) throw RealizesRefusals.TierNotYetAvailable(tier);
        BaselineReadCache? read = null;
        try
        {
            if (context.ProjectPath is { } project) read = OpenProject(project);
            IRealizesParser? parser = null;
            var sets = new List<RealizesResult>();
            var seen = new Dictionary<QueryRealizes, (RealizesResult, IReadOnlyList<string>)>();
            var lowered = RealizesLowering.Lower(where, node =>
            {
                if (seen.TryGetValue(node, out var done)) return done;
                var (text, asRoot) = StripHint(node.Morpheme);
                ResolvedMorpheme? resolved = null;
                RealizesTarget target;
                if (FormsMorphemeResolver.TryPair(text, out var entry, out var msa)) target = new RealizesTarget(entry, msa, null);
                else if (FormsMorphemeResolver.TryGuid(text, out var guid)) target = new RealizesTarget(null, null, guid);
                else
                {
                    if (read is null) throw RealizesRefusals.NeedsLexicon(node.Morpheme);
                    resolved = Resolve(read.Cache, text, asRoot);
                    target = new RealizesTarget(resolved.EntryGuid, resolved.MsaGuid, null);
                }
                if (resolved is null && read is not null && TryResolveIds(read.Cache, text, asRoot) is { } byId) resolved = byId;
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
                    parser ??= context.Parser();
                    var (variants, parse) = RealizesCorpusSource.Collect(parser, target,
                        [.. context.Corpus.Words.Select(word => (word.Form, word.Tokens))], context.Sample, cancellationToken);
                    parts.Add(variants);
                    report = parse;
                    finding.Add($"none of the {parse.Parsed} parsed words has an analysis that contains it");
                }
                var merged = Merge(parts, resolved is not null);
                if (merged.Forms.Count == 0) throw RealizesRefusals.NoParses(node.Morpheme, string.Join("; ", finding));
                var forms = RealizesPatternCompiler.Normalize(merged.Forms.Select(form => form.Form), context.FoldCase);
                var result = new RealizesResult(RealizesResult.SchemaName, Describe(resolved, target, text), node.Tier, merged.Complete,
                    merged.Reasons, merged.StopReason, merged.Forms, RealizesPatternCompiler.Pattern(forms, merged.Anchor), report);
                sets.Add(result);
                return seen[node] = (result, forms);
            });
            var positive = RealizesLowering.Find(where).Where(item => !item.Negated).Select(item => seen[item.Node]).Distinct().ToList();
            return new RealizesRun(lowered, sets, positive);
        }
        finally
        {
            read?.Dispose();
        }
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
        return new RealizesVariants(forms, anchor, reasons, parts.Select(part => part.StopReason).FirstOrDefault(reason => reason is not null));
    }

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
        foreach (var (prefix, root) in new[] { ("root:", true), ("stem:", true), ("pfx:", false), ("sfx:", false), ("afx:", false) })
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
