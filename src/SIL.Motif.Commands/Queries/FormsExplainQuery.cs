using System.Globalization;
using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Commands.Queries;

/// <summary>Which forms to explain: a root morpheme, or a word whose analyses are explained one by one.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Morpheme">The root: an entry guid or Motif id, <c>entry:msa</c>, or a form in the lexicon. Null when <paramref name="Word"/> is given.</param>
/// <param name="With">Morphemes generated with the root, in request order, named like the root; empty for the root alone.</param>
/// <param name="Word">A word to parse; each of its analyses is generated from its own morphemes. Null when <paramref name="Morpheme"/> is given.</param>
/// <param name="At"><c>baseline</c> (the default) for the current published Baseline, or a Proposal id for its latest Trial copy.</param>
/// <param name="TimeoutMs">The wall-clock deadline for each generate request, 1 through 240000; omitted is 30000. Past it PanGloss stops with <c>deadline</c> and returns what it found.</param>
/// <param name="MaxWork">Lowers PanGloss's generation work cap for each request; omitted sends none, so PanGloss's own cap applies.</param>
public sealed record FormsExplainRequest(
    string ProjectPath, string? Morpheme, IReadOnlyList<string> With, string? Word = null, string? At = null,
    int? TimeoutMs = null, long? MaxWork = null);

/// <summary>One set of morphemes to generate together.</summary>
/// <param name="Morphemes">The root first, then the morphemes added to it, in request order.</param>
internal sealed record FormsContextPlan(IReadOnlyList<ResolvedMorpheme> Morphemes)
{
    internal string Key => string.Join(" ", Morphemes.Select(morpheme => morpheme.Id));
}

/// <summary>
/// Chooses the sets of morphemes a root is generated in. The only planner today is the caller's own explicit list.
/// Rule-chosen test roots, which trigger each rule environment at the morpheme edges, plug in here as another planner
/// once PanGloss's <c>effects</c> op can name the rules.
/// </summary>
internal interface IFormsContextPlanner
{
    /// <summary>The contexts to generate <paramref name="root"/> in, in the order they are reported.</summary>
    IReadOnlyList<FormsContextPlan> Plan(ResolvedMorpheme root);
}

/// <summary>The one context the caller named: the root with exactly the morphemes listed.</summary>
internal sealed class ExplicitContextPlanner(IReadOnlyList<ResolvedMorpheme> with) : IFormsContextPlanner
{
    public IReadOnlyList<FormsContextPlan> Plan(ResolvedMorpheme root) => [new FormsContextPlan([root, .. with])];
}

/// <summary>Where a <c>generate</c> op is sent; the seam the tests drive without a resident parser.</summary>
internal interface IFormsGenerator
{
    /// <summary>The version of the PanGloss that answers, or <c>unknown</c>.</summary>
    string ParserVersion { get; }

    Task<WarmGenerateResult> GenerateAsync(ServeGenerateRequest request, CancellationToken cancellationToken);
}

/// <summary>Sends each request to one resident <c>pangloss serve</c> that holds the grammar.</summary>
internal sealed class ResidentFormsGenerator(
    WarmPanGlossPool pool, string grammarPath, string stateToken, string parserVersion) : IFormsGenerator
{
    public string ParserVersion { get; } = parserVersion;

    public Task<WarmGenerateResult> GenerateAsync(ServeGenerateRequest request, CancellationToken cancellationToken) =>
        pool.TryGenerateAsync(grammarPath, stateToken, grammarPath, request, cancellationToken);
}

/// <summary>
/// Explains the forms PanGloss generates for a morpheme: every surface it reaches in each context, the allomorphs it
/// chose, and the rules that fired in order. It asks the resident PanGloss that parses to generate with a rule trace
/// and reports what it answered; it never reasons about the grammar itself, and a stop is reported, never hidden.
/// </summary>
public static class FormsExplainQuery
{
    private const int MaxWordScalars = 256;

    /// <summary>The deadline sent with each generate request when the caller names none.</summary>
    internal const int DefaultTimeoutMs = 30000;

    private const int MaxTimeoutMs = 240000;

    /// <summary>The stop reason of a result whose planned contexts were not all generated because PanGloss refused one.</summary>
    internal const string ContextRejected = "context_rejected";

    /// <summary>Explains with the located parser.</summary>
    public static CommandOutcome<FormsExplainResponse> Explain(
        FormsExplainRequest request, CancellationToken cancellationToken = default) =>
        Explain(request, PanGlossExecutable.TryLocate(), cancellationToken);

    /// <summary>Explains with an explicitly selected parser.</summary>
    public static CommandOutcome<FormsExplainResponse> Explain(
        FormsExplainRequest request, string? parserPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Check(request) is { } invalid) return invalid;
        if (parserPath is null)
            return CommandOutcome<FormsExplainResponse>.Refused(new Refusal(RefusalCodes.WordTraceParserUnavailable,
                FailureReason.Refused, PanGlossExecutable.NotFoundMessage, ParserNotFoundFact.Mark(null, true)));
        var proposalId = ProposalOf(request.At);

        IReadOnlyList<WordAnalysis>? analyses = null;
        if (request.Word is { } word)
        {
            var traced = WordTraceQuery.Query(new WordTraceRequest(request.ProjectPath, word, proposalId), parserPath,
                cancellationToken);
            if (!traced.Succeeded) return CommandOutcome<FormsExplainResponse>.Refused(traced.Refusal!);
            analyses = WordAnalyses(traced.Value!);
        }

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            using var parseLease = ProjectParseLease.TryAcquire(database);
            if (parseLease is null) return CommandOutcome<FormsExplainResponse>.Refused(ProjectParseLease.BusyRefusal());
            var resolved = TracedGrammar.Resolve(database, project, proposalId);
            if (!resolved.Succeeded) return CommandOutcome<FormsExplainResponse>.Refused(resolved.Refusal!);
            var grammarPath = resolved.Value!.FwDataPath;

            ResolvedPlan plan;
            try
            {
                using var reader = BaselineReadCache.Open(grammarPath);
                var planned = Plan(reader.Cache, request, analyses);
                if (planned.Refusal is { } refusal) return CommandOutcome<FormsExplainResponse>.Refused(refusal);
                plan = planned.Plan!;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Refuse(RefusalCodes.FormsExplainUnfinished, FailureReason.Refused,
                    "Motif could not read its saved copy of the project: " + exception.Message);
            }

            var grammarHash = BatchInvocationEvidence.DigestFile(grammarPath);
            using var pool = new WarmPanGlossPool(parserPath);
            var generator = new ResidentFormsGenerator(pool, grammarPath, grammarHash,
                ParserExecutableIdentity.ReadVersion(parserPath));
            return Run(generator, plan, grammarHash, resolved.Value.TracedAgainst, cancellationToken,
                request.TimeoutMs ?? DefaultTimeoutMs, request.MaxWork);
        });
    }

    /// <summary>The analyses of a parsed word that name every morph by FieldWorks identity.</summary>
    internal sealed record WordAnalysis(string Reading, IReadOnlyList<(string? EntryId, string? MsaId, string? Form)> Morphs);

    internal static IReadOnlyList<WordAnalysis> WordAnalyses(WordTraceResponse response) =>
        response.Reading.Analyses.Select(analysis => new WordAnalysis(
            string.Join(' ', analysis.Morphs.Select(morph => morph.Headword ?? morph.Form ?? "?")),
            analysis.Morphs.Select(morph => (morph.EntryId, morph.MsaId, morph.Headword ?? morph.Form)).ToArray())).ToArray();

    /// <summary>What to generate: the contexts, with the words that gave rise to them and any analysis that could not be named.</summary>
    internal sealed record ResolvedPlan(
        string? Morpheme, string? Word, IReadOnlyList<FormsContextPlan> Contexts, IReadOnlyList<FormsExplainSkipped> Skipped);

    private readonly record struct Planned(ResolvedPlan? Plan, Refusal? Refusal);

    private static Planned Plan(SIL.LCModel.LcmCache cache, FormsExplainRequest request, IReadOnlyList<WordAnalysis>? analyses)
    {
        if (request.Word is { } word)
            return PlanWord(cache, word, analyses ?? []);
        var root = FormsMorphemeResolver.Resolve(cache, request.Morpheme!, asRoot: true);
        if (root.Refusal is { } rootRefusal) return new Planned(null, rootRefusal);
        var with = new List<ResolvedMorpheme>();
        foreach (var spec in request.With)
        {
            var found = FormsMorphemeResolver.Resolve(cache, spec, asRoot: false);
            if (found.Refusal is { } refusal) return new Planned(null, refusal);
            with.Add(found.Morpheme!);
        }
        IFormsContextPlanner planner = new ExplicitContextPlanner(with);
        return new Planned(new ResolvedPlan(request.Morpheme, null, planner.Plan(root.Morpheme!), []), null);
    }

    private static Planned PlanWord(SIL.LCModel.LcmCache cache, string word, IReadOnlyList<WordAnalysis> analyses)
    {
        var contexts = new Dictionary<string, FormsContextPlan>(StringComparer.Ordinal);
        var skipped = new List<FormsExplainSkipped>();
        foreach (var analysis in analyses)
        {
            var named = new List<ResolvedMorpheme>();
            string? why = null;
            foreach (var (entryId, msaId, form) in analysis.Morphs)
            {
                if (entryId is null || msaId is null ||
                    !FormsMorphemeResolver.TryGuid(entryId, out var entry) || !FormsMorphemeResolver.TryGuid(msaId, out var msa))
                {
                    why = $"'{form ?? "?"}' has no lexicon identity (the parser guessed it).";
                    break;
                }
                var found = FormsMorphemeResolver.Resolve(cache, entry + ":" + msa, asRoot: false);
                if (found.Refusal is { } refusal)
                {
                    why = refusal.Message;
                    break;
                }
                named.Add(found.Morpheme!);
            }
            if (why is null && named.FindIndex(morpheme => morpheme.MsaClass == ResolvedMorpheme.StemClass) is var at and >= 0)
            {
                var plan = new FormsContextPlan([named[at], .. named.Where((_, index) => index != at)]);
                contexts.TryAdd(plan.Key, plan);
                continue;
            }
            skipped.Add(new FormsExplainSkipped(analysis.Reading, why ?? "The analysis has no stem."));
        }
        if (contexts.Count == 0)
            return new Planned(null, new Refusal(RefusalCodes.FormsExplainNoReading, FailureReason.NotFound,
                analyses.Count == 0
                    ? $"PanGloss gave '{word}' no analysis to generate from."
                    : $"None of the {analyses.Count.ToString(CultureInfo.InvariantCulture)} analyses of '{word}' could be named in the lexicon: " +
                      string.Join(" ", skipped.Select(item => $"[{item.Reading}] {item.Reason}")),
                new Dictionary<string, string> { ["word"] = word }));
        return new Planned(new ResolvedPlan(null, word, contexts.Values.OrderBy(plan => plan.Key, StringComparer.Ordinal).ToArray(),
            skipped.OrderBy(item => item.Reading, StringComparer.Ordinal).ToArray()), null);
    }

    /// <summary>Generates every planned context and projects the answers; the seam the tests drive without a project.</summary>
    internal static CommandOutcome<FormsExplainResponse> Run(IFormsGenerator generator, ResolvedPlan plan,
        string grammarHash, string tracedAgainst, CancellationToken cancellationToken,
        int wordTimeoutMs = DefaultTimeoutMs, long? maxWork = null)
    {
        var contexts = new List<FormsExplainContext>();
        var skipped = new List<FormsExplainSkipped>(plan.Skipped);
        Refusal? firstRejection = null;
        foreach (var context in plan.Contexts)
        {
            var request = new ServeGenerateRequest(context.Morphemes[0].ToRequest(),
                context.Morphemes.Skip(1).Select(morpheme => morpheme.ToRequest()).ToArray(),
                MaxWork: maxWork, WordTimeoutMs: wordTimeoutMs);
            var answer = generator.GenerateAsync(request, cancellationToken).GetAwaiter().GetResult();
            switch (answer)
            {
                case WarmGenerateResult.Completed completed:
                    if (!FormsExplainProjection.TryProject(completed.Json, context.Morphemes, out var projected, out var error))
                        return Refuse(RefusalCodes.FormsExplainMalformed, FailureReason.Refused,
                            "PanGloss's generate reply is malformed (" + error + "). This is a PanGloss reply-shape problem; report it to PanGloss with this field name.");
                    contexts.Add(projected!);
                    break;
                case WarmGenerateResult.Rejected rejected:
                    firstRejection ??= new Refusal(RefusalCodes.FormsExplainParserRefused, FailureReason.Refused,
                        $"PanGloss refused the generate request ({rejected.Code}): {rejected.Message}",
                        new Dictionary<string, string> { ["parserCode"] = rejected.Code });
                    skipped.Add(new FormsExplainSkipped(ReadingOf(context),
                        $"PanGloss refused it ({rejected.Code}): {rejected.Message}"));
                    break;
                case WarmGenerateResult.Unsupported unsupported:
                    return Refuse(RefusalCodes.FormsExplainParserUnsupported, FailureReason.Refused,
                        $"The installed PanGloss ({generator.ParserVersion}) cannot generate forms with a rule trace: " +
                        $"{unsupported.Detail}. Motif needs {PanGlossInterfaceVersions.ServeGenerateNeeded}.",
                        new Dictionary<string, string>
                        {
                            ["parserVersion"] = generator.ParserVersion,
                            ["needed"] = PanGlossInterfaceVersions.ServeGenerateNeeded,
                        });
                case WarmGenerateResult.Cancelled:
                    return Refuse(RefusalCodes.WordTraceCancelled, FailureReason.Cancelled, "The explanation was cancelled.");
                case WarmGenerateResult.Failed failed:
                    return failed.Code switch
                    {
                        "serve-unsupported" => Refuse(RefusalCodes.FormsExplainParserUnsupported, FailureReason.Refused,
                            $"The installed PanGloss ({generator.ParserVersion}) has no resident serve mode Motif can generate through. " +
                            $"Motif needs {PanGlossInterfaceVersions.ServeGenerateNeeded}.",
                            new Dictionary<string, string>
                            {
                                ["parserVersion"] = generator.ParserVersion,
                                ["needed"] = PanGlossInterfaceVersions.ServeGenerateNeeded,
                            }),
                        "parser-unavailable" => CommandOutcome<FormsExplainResponse>.Refused(new Refusal(
                            RefusalCodes.WordTraceParserUnavailable, FailureReason.Refused, failed.Message,
                            ParserNotFoundFact.Mark(null, true))),
                        _ => Refuse(RefusalCodes.FormsExplainUnfinished, FailureReason.Refused,
                            $"The resident PanGloss did not answer ({failed.Code}): {failed.Message}",
                            new Dictionary<string, string> { ["parserCode"] = failed.Code }),
                    };
            }
        }
        if (contexts.Count == 0 && firstRejection is not null)
            return CommandOutcome<FormsExplainResponse>.Refused(firstRejection);
        // A context PanGloss rejected has no forms in the result, so the result cannot claim to be everything.
        var anyRejected = firstRejection is not null;
        var complete = contexts.All(context => context.Complete) && !anyRejected;
        return CommandOutcome<FormsExplainResponse>.Success(new FormsExplainResponse(
            FormsExplainSchema.Id, plan.Morpheme, plan.Word, contexts, complete,
            anyRejected ? ContextRejected
                : contexts.Select(context => context.StopReason).FirstOrDefault(reason => reason is not null),
            grammarHash, generator.ParserVersion, tracedAgainst, skipped.Count == 0 ? null : skipped));
    }

    private static string ReadingOf(FormsContextPlan context) =>
        string.Join(' ', context.Morphemes.Select(morpheme => morpheme.Form ?? morpheme.Id));

    private static CommandOutcome<FormsExplainResponse>? Check(FormsExplainRequest request)
    {
        var hasMorpheme = !string.IsNullOrWhiteSpace(request.Morpheme);
        var hasWord = !string.IsNullOrWhiteSpace(request.Word);
        if (hasMorpheme == hasWord)
            return Invalid("Name one root morpheme, or one word with --word, not both and not neither.");
        if (hasWord)
        {
            if (request.With is { Count: > 0 })
                return Invalid("--with names morphemes to add to a root; it cannot be combined with --word, which explains the word's own analyses.");
            var word = request.Word!;
            if (word.EnumerateRunes().Count() > MaxWordScalars || word.StartsWith('-') || word.Any(char.IsWhiteSpace))
                return Invalid($"Give one word of 1 to {MaxWordScalars} characters, with no spaces and not starting with '-'.");
        }
        if (request.With is { } with && with.Count + 1 > FormsMorphemeResolver.MaxMorphemes)
            return Invalid($"Name at most {FormsMorphemeResolver.MaxMorphemes - 1} morphemes with --with.");
        if (request.With?.Any(string.IsNullOrWhiteSpace) == true)
            return Invalid("A --with morpheme is empty.");
        if (request.TimeoutMs is { } timeout && timeout is < 1 or > MaxTimeoutMs)
            return Invalid($"--timeout-ms is 1 through {MaxTimeoutMs} milliseconds.");
        if (request.MaxWork is < 1)
            return Invalid("--max-work must be at least 1.");
        if (ProposalOf(request.At) is { } proposal && !CanonicalId.TryParse(proposal, out _))
            return Invalid($"'{request.At}' is neither 'baseline' nor a Proposal id.");
        return null;
    }

    private static string? ProposalOf(string? at) =>
        string.IsNullOrWhiteSpace(at) || at == "baseline" ? null : at;

    private static CommandOutcome<FormsExplainResponse> Invalid(string message) =>
        Refuse(RefusalCodes.FormsExplainRequestInvalid, FailureReason.InvalidArgument, message);

    private static CommandOutcome<FormsExplainResponse> Refuse(
        string code, FailureReason reason, string message, IReadOnlyDictionary<string, string>? facts = null) =>
        CommandOutcome<FormsExplainResponse>.Refused(new Refusal(code, reason, message, facts));
}
