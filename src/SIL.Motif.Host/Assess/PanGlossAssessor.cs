using System.Security.Cryptography;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.Assess;

/// <summary>Resolves the file path an Assessor's own cache should live at.</summary>
/// <remarks>
/// Declared here, in the Host layer that only knows Assessors, and implemented by
/// <c>SIL.Motif.Worker.Assess.StatsCacheStore</c>, which is the layer that knows the worker root and its
/// ownership rules. <see cref="PanGlossAssessor"/> must not know either of those things itself.
/// </remarks>
public interface IAssessorCachePathResolver
{
    /// <summary>
    /// The stable path for one Assessor's cache over one grammar and engine. Stable for the same three
    /// inputs; different when any of them differs, so two engines can never collide on one file — the rule
    /// PanGloss's own cache already enforces, respected here in the key rather than discovered at run time.
    /// </summary>
    string PathFor(string grammarSourceSha256, string assessor, string engine);
}

/// <summary>
/// PanGloss as an <see cref="IAssessor"/>: the first Assessor, and proof the seam needs no PanGloss-specific
/// caller.
/// </summary>
/// <remarks>
/// <para>
/// Composes two seams: <see cref="IPanGlossAssessor"/> for <see cref="AssessmentKind.Correctness"/> (GUID-keyed
/// analyses against manual analysis) and the <see cref="IPanGlossInvoker"/> for both
/// <see cref="AssessmentKind.ParseTime"/> (a plain batch, read back through <see cref="PanGlossParser"/>) and
/// <see cref="AssessmentKind.ObjectTiming"/> (the same batch with <c>--stats --cache</c>, whose cache PanGloss
/// owns the format of and Motif only ever digests).
/// </para>
/// <para>
/// <see cref="AssessmentKind.EngineSize"/> is never declared: PanGloss emits build time and engine size on
/// stderr, and scraping stderr for them was rejected rather than adopted. <see cref="AssessmentKind.Difference"/>
/// and <see cref="AssessmentKind.Completion"/> are never declared either: both compare two Assessments, which
/// is the comparison mechanism's job.
/// </para>
/// <para>
/// <see cref="ProduceAsync"/> always runs the GUID-keyed assess pass first, whatever was asked for: it is the
/// only route that carries the grammar's own hash and the rest of the report header, which every produced
/// kind must cite and which this type never derives on its own.
/// </para>
/// </remarks>
public sealed class PanGlossAssessor : IAssessor
{
    /// <summary>The name this Assessor is registered and cited under.</summary>
    public const string AssessorName = "pangloss";

    private static readonly IReadOnlyList<AssessmentKind> Supported =
        [AssessmentKind.ParseTime, AssessmentKind.Correctness, AssessmentKind.ObjectTiming];
    private static readonly IReadOnlyList<AssessmentKind> DefaultCollected =
        [AssessmentKind.ParseTime, AssessmentKind.Correctness];

    private readonly IAssessorCachePathResolver _cachePaths;
    private readonly IPanGlossInvoker _invoker;
    private readonly PanGlossParser _parser;
    private readonly IPanGlossAssessor _reportRunner;

    /// <param name="cachePaths">Resolves where this Assessor's stats cache lives for a grammar and engine.</param>
    /// <param name="invoker">Runs every parser process this Assessor needs.</param>
    /// <param name="reportRunner">Runs the GUID-keyed assess pass; defaults to a real one.</param>
    public PanGlossAssessor(
        IAssessorCachePathResolver cachePaths, IPanGlossInvoker invoker, IPanGlossAssessor? reportRunner = null)
    {
        _cachePaths = cachePaths ?? throw new ArgumentNullException(nameof(cachePaths));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _parser = new PanGlossParser(invoker);
        _reportRunner = reportRunner ?? new PanGlossAssessmentProcess();
    }

    /// <inheritdoc />
    public string Name => AssessorName;

    /// <inheritdoc />
    public IReadOnlyList<AssessmentKind> SupportedKinds => Supported;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(exportedCandidate))
            throw new ArgumentException("Required.", nameof(exportedCandidate));

        var wanted = scope.Collect.Count == 0 ? DefaultCollected : scope.Collect;
        foreach (var kind in wanted)
        {
            if (!Supported.Contains(kind))
                throw new AssessorRefusalException(AssessorName, kind, ReasonNotProduced(kind));
        }
        if (!PanGlossEngineNames.TryParse(scope.Engine, out var engine))
        {
            throw new ArgumentException(
                $"'{scope.Engine}' does not name an engine {AssessorName} recognizes.", nameof(scope));
        }

        var grammarSourcePath = LocateGrammarSource(exportedCandidate);

        AssessReport report;
        try
        {
            // Every produced kind cites this hash, and only the assess pass carries it — never derived here.
            report = await _reportRunner.RunAsync(exportedCandidate, cancellationToken).ConfigureAwait(false);
        }
        catch (ParserUnavailableException exception)
        {
            throw new AssessorUnavailableException(AssessorName, exception.Message);
        }

        var results = new List<ProducedAssessment>();
        if (wanted.Contains(AssessmentKind.Correctness))
        {
            results.Add(Produced(report, AssessmentKind.Correctness,
                new AssessmentRaw.WordMeasurements(report.Words)));
        }
        if (wanted.Contains(AssessmentKind.ParseTime))
        {
            var runResult = await _parser.AnalyseBatchAsync(
                grammarSourcePath, scope.Words, engine, scope.PerWordLimit, "assess:parse-time", cancellationToken)
                .ConfigureAwait(false);
            if (!runResult.Succeeded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (runResult.Refusal is { } refusal)
                    throw new InvalidOperationException($"{AssessorName} could not measure parse time: {refusal.Detail}");
                throw new AssessorUnavailableException(AssessorName, runResult.Outcome.Message);
            }
            results.Add(Produced(report, AssessmentKind.ParseTime, new AssessmentRaw.Batch(runResult.Analysis!)));
        }
        if (wanted.Contains(AssessmentKind.ObjectTiming))
        {
            var cachePath = _cachePaths.PathFor(report.GrammarSourceSha256, AssessorName, scope.Engine);
            var outcome = await _invoker.RunAsync(
                new PanGlossRequest.Batch(grammarSourcePath, scope.Words, scope.PerWordLimit, cachePath),
                "assess:object-timing", cancellationToken).ConfigureAwait(false);
            if (outcome is not PanGlossOutcome.Completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new AssessorUnavailableException(AssessorName, outcome.Message);
            }
            results.Add(Produced(report, AssessmentKind.ObjectTiming,
                new AssessmentRaw.FileCache(cachePath, DigestOfFile(cachePath))));
        }
        return results;
    }

    // Every produced kind shares one report's header; this is the one place that pairs them.
    private static ProducedAssessment Produced(AssessReport report, AssessmentKind kind, AssessmentRaw raw) =>
        new(kind, report.GrammarSourceSha256, report.OutcomeDigest, report.SemanticDigest,
            report.ModelFingerprint, report.Pipeline, report.DiagnosticCount, raw);

    private static string ReasonNotProduced(AssessmentKind kind) => kind switch
    {
        AssessmentKind.EngineSize =>
            "the compiled engine's size is emitted on stderr at build time, not through this route.",
        AssessmentKind.Difference =>
            "a difference compares two Assessments; it is not one Assessor call's job to produce alone.",
        AssessmentKind.Completion =>
            "which words newly complete compares two Assessments; it is not one Assessor call's job to produce alone.",
        _ => $"'{AssessorName}' does not produce {kind} from this scope's collection.",
    };

    // Mirrors PanGlossAssessmentProcess's own dispatch: exactly one .fwdata by extension, no assumed layout.
    private static string LocateGrammarSource(string exportedCandidate)
    {
        var matches = Directory.GetFiles(exportedCandidate, "*.fwdata", SearchOption.AllDirectories);
        if (matches.Length == 0)
        {
            throw new FileNotFoundException(
                "The exported candidate contains no .fwdata grammar source.", exportedCandidate);
        }
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"The exported candidate contains {matches.Length} .fwdata files; exactly one is required.");
        }
        return matches[0];
    }

    // Hashes what actually landed on disk rather than trusting the stats runner's exit code alone.
    private static string DigestOfFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return "sha256:" + Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
