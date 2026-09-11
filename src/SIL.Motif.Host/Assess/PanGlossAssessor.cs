using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Host.Assess;

/// <summary>Resolves the owned directory for one invocation's immutable artifacts.</summary>
/// <remarks>
/// Declared here, in the Host layer that only knows Assessors, and implemented by
/// <c>SIL.Motif.Worker.Assess.StatsCacheStore</c>, which is the layer that knows the worker root and its
/// ownership rules. <see cref="PanGlossAssessor"/> must not know either of those things itself.
/// </remarks>
public interface IAssessorCachePathResolver
{
    /// <summary>
    /// Returns the same directory for the same invocation id and distinct directories for distinct ids.
    /// May create its parent, but never creates the final directory: publication owns that creation.
    /// </summary>
    string DirectoryFor(string invocationId);
}

/// <summary>Produces timing, statistics, and approved morphology comparisons from one batch invocation.</summary>
/// <remarks>
/// TSV parsing and statistics collection are separate parser passes within that invocation. Their
/// evidence is shared, but their elapsed times are different measurements.
/// </remarks>
public sealed class PanGlossAssessor : IAssessor
{
    public const string AssessorName = "pangloss";
    private static readonly IReadOnlyList<AssessmentKind> Supported =
        [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming, AssessmentKind.Correctness];
    private readonly IAssessorCachePathResolver _paths;
    private readonly IPanGlossInvoker _invoker;

    public PanGlossAssessor(IAssessorCachePathResolver paths, IPanGlossInvoker invoker)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
    }

    public string Name => AssessorName;
    public IReadOnlyList<AssessmentKind> SupportedKinds => Supported;

    public async Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var wanted = scope.Collect.Count == 0 ? Supported : scope.Collect;
        foreach (var kind in wanted)
        {
            if (!Supported.Contains(kind))
                throw new AssessorRefusalException(AssessorName, kind,
                    "the supported batch route does not produce this measurement.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var requestedWords = scope.Words.ToArray();
        var sources = Directory.GetFiles(exportedCandidate, "*.fwdata", SearchOption.AllDirectories);
        if (sources.Length != 1)
            throw new AssessorUnavailableException(AssessorName,
                $"The candidate must contain exactly one .fwdata source; found {sources.Length}.");
        var invocationId = Guid.NewGuid().ToString("N");
        var directory = _paths.DirectoryFor(invocationId);
        var cachePath = wanted.Contains(AssessmentKind.ObjectTiming) ? Path.Combine(directory, "stats.sqlite") : null;
        AssessmentArtifactLease? artifactLease = null;
        try
        {
            var outcome = await _invoker.RunAsync(new PanGlossRequest.Batch(
                sources[0], requestedWords, scope.PerWordLimit, cachePath, scope.PerWordStepLimit, directory)
                { CollectAnalyses = true },
                "assess:batch", cancellationToken).ConfigureAwait(false);
            artifactLease = (outcome as PanGlossOutcome.Completed)?.ArtifactLease;
            if (outcome is PanGlossOutcome.Cancelled)
                throw new OperationCanceledException(cancellationToken);
            if (outcome is not PanGlossOutcome.Completed { BatchEvidence: { } evidence } completed)
                throw new AssessorUnavailableException(AssessorName,
                    outcome is PanGlossOutcome.Completed ? "The invocation returned no retained evidence." : outcome.Message);
            var matchingArtifacts = evidence.InvocationId == invocationId &&
                Path.GetFullPath(evidence.SourcePath) == Path.Combine(Path.GetFullPath(directory), "source.fwdata");
            if (!matchingArtifacts || evidence.PerWordStepLimit != scope.PerWordStepLimit ||
                evidence.PerWordTimeoutMs != (int)scope.PerWordLimit.TotalMilliseconds ||
                evidence.Threads != 1 || evidence.CollectStatistics != (cachePath is not null))
                throw new AssessorUnavailableException(AssessorName, "The invocation evidence does not match the requested scope.");
            IReadOnlyList<WordAnalysis> rows;
            try { rows = BatchTsvParser.Parse(completed.Output); }
            catch (InvalidOperationException exception)
            {
                throw new AssessorUnavailableException(AssessorName, exception.Message);
            }
            if (rows.Count != requestedWords.Length || rows.Where((row, index) =>
                row.Index != index || !string.Equals(row.Word, requestedWords[index], StringComparison.Ordinal)).Any())
                throw new AssessorUnavailableException(AssessorName,
                    "The batch did not return exactly one ordered result for every requested word.");
            if (completed.MorphologyOutput is null || evidence.AnalysesPath is null || evidence.AnalysesSha256 is null)
                throw new AssessorUnavailableException(AssessorName, "The invocation returned no retained morphology evidence.");
            try
            {
                if (BatchInvocationEvidence.DigestFile(evidence.AnalysesPath) != evidence.AnalysesSha256 ||
                    File.ReadAllText(evidence.AnalysesPath) != completed.MorphologyOutput)
                    throw new InvalidDataException("The retained morphology artifact changed.");
                var morphology = ParseMorphEvidence.Read(completed.MorphologyOutput, requestedWords);
                rows = rows.Select((row, index) =>
                {
                    var item = morphology[index];
                    var expectedOutcome = item.InvalidShape ? WordOutcome.Skipped : item.TimedOut ? WordOutcome.TimedOut
                        : item.Capped ? WordOutcome.Capped : item.Analyses.Count + item.Unavailable.Count > 0
                            ? WordOutcome.Analysed : WordOutcome.NoAnalysis;
                    if (row.ElapsedMs != item.ElapsedMs || row.Outcome != expectedOutcome)
                        throw new InvalidDataException("TSV and morphology evidence describe different search outcomes.");
                    return row with { Morphology = item };
                }).ToArray();
                if (wanted.Contains(AssessmentKind.Correctness))
                {
                    using var cache = new FwDataProjectLoader().LoadScratchCache(evidence.SourcePath);
                    var expected = ApprovedMorphologyReader.Read(cache);
                    rows = rows.Select(row => row with
                    {
                        Correctness = MorphologyCorrectness.Compare(row.Morphology!,
                            expected.TryGetValue(row.Word, out var approved) ? approved : []),
                    }).ToArray();
                    if (BatchInvocationEvidence.DigestFile(evidence.SourcePath) != evidence.SourceBytesSha256)
                        throw new InvalidDataException("The source changed while reading approved expectations.");
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                throw new AssessorUnavailableException(AssessorName, exception.Message);
            }
            var warnings = completed.StandardError.Split('\n').Select(line => line.Trim())
                .Where(line => line.StartsWith("warning:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("capability:", StringComparison.OrdinalIgnoreCase)).ToArray();
            var analysis = new BatchAnalysis(rows, evidence.PerWordTimeoutMs, evidence.SourcePath, warnings)
            {
                PerWordStepLimit = evidence.PerWordStepLimit,
            };
            var results = new List<ProducedAssessment>();
            if (wanted.Contains(AssessmentKind.ParseTime))
                results.Add(Produced(AssessmentKind.ParseTime, new AssessmentRaw.Batch(analysis), evidence));
            if (cachePath is not null)
                results.Add(Produced(AssessmentKind.ObjectTiming,
                    new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath)), evidence));
            if (wanted.Contains(AssessmentKind.Correctness))
                results.Add(Produced(AssessmentKind.Correctness, new AssessmentRaw.Batch(analysis), evidence));
            cancellationToken.ThrowIfCancellationRequested();
            return results.Select(result => result with { ArtifactLease = artifactLease }).ToArray();
        }
        catch
        {
            artifactLease?.Dispose();
            throw;
        }
    }

    private static ProducedAssessment Produced(
        AssessmentKind kind, AssessmentRaw raw, BatchInvocationEvidence evidence) =>
        new(kind, evidence.SourceBytesSha256, null, null, null, null, null, raw) { Invocation = evidence };
}
