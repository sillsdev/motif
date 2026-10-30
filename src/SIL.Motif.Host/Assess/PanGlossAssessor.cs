using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;

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
    private static readonly IReadOnlyList<AssessmentKind> WordDefaults =
        [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming, AssessmentKind.Correctness];
    private static readonly IReadOnlyList<AssessmentKind> Supported =
        [.. WordDefaults, AssessmentKind.PhonologyExamples];
    private readonly IAssessorCachePathResolver _paths;
    private readonly IPanGlossInvoker _invoker;
    private readonly Func<string, string> _sourceDigest;
    private readonly Func<string, IReadOnlyList<string>, LowercasePass> _lowercasePass;

    public PanGlossAssessor(IAssessorCachePathResolver paths, IPanGlossInvoker invoker)
        : this(paths, invoker, BatchInvocationEvidence.DigestFile)
    {
    }

    internal PanGlossAssessor(IAssessorCachePathResolver paths, IPanGlossInvoker invoker,
        Func<string, string> sourceDigest,
        Func<string, IReadOnlyList<string>, LowercasePass>? lowercasePass = null)
    {
        _lowercasePass = lowercasePass ?? ((source, words) => LowercasePass.ForProjectIfNeeded(source, words));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _sourceDigest = sourceDigest ?? throw new ArgumentNullException(nameof(sourceDigest));
    }

    public string Name => AssessorName;
    public IReadOnlyList<AssessmentKind> SupportedKinds => Supported;

    public async Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken)
        => await ProduceAsync(scope, exportedCandidate, null, cancellationToken).ConfigureAwait(false);

    /// <summary>Runs the batch and reports each flushed word row while it is in progress.</summary>
    public async Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, Action<TrialWordProgress>? onProgress,
        CancellationToken cancellationToken)
        => await ProduceAsync(scope, exportedCandidate, onProgress, null, cancellationToken).ConfigureAwait(false);

    /// <summary>Runs the batch with word progress and machine-capacity wait callbacks.</summary>
    /// <param name="scope">The words and measurements requested.</param>
    /// <param name="exportedCandidate">A directory holding exactly the grammar to measure.</param>
    /// <param name="onProgress">Receives word results as the batch flushes them.</param>
    /// <param name="onAdmissionWait">Receives true while waiting for capacity and false when the wait ends.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The produced Assessments.</returns>
    public async Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, Action<TrialWordProgress>? onProgress,
        Action<bool>? onAdmissionWait, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope is AssessmentScope.PhonologyExamples examples)
        {
            var phonologySources = Directory.GetFiles(exportedCandidate, "*.fwdata", SearchOption.AllDirectories);
            if (phonologySources.Length != 1)
                throw new InvalidDataException("The phonology source must contain exactly one .fwdata file.");
            var outcome = await _invoker.RunAsync(new PanGlossRequest.PhonologyExamples(
                phonologySources[0], examples.Examples, examples.Limits), "assess:phonology-examples", cancellationToken)
                .ConfigureAwait(false);
            return [new ProducedAssessment(AssessmentKind.PhonologyExamples,
                _sourceDigest(phonologySources[0]), null, null, null, null, null,
                new AssessmentRaw.PhonologyExamples(PhonologyAssessmentOutcome.From(outcome)))];
        }
        var wordsScope = scope as AssessmentScope.Words ?? throw new ArgumentOutOfRangeException(nameof(scope));
        var wanted = wordsScope.Collect.Count == 0 ? WordDefaults : wordsScope.Collect;
        foreach (var kind in wanted)
        {
            if (!WordDefaults.Contains(kind))
                throw new AssessorRefusalException(AssessorName, kind,
                    "the supported batch route does not produce this measurement.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var requestedWords = wordsScope.WordForms.ToArray();
        var sources = Directory.GetFiles(exportedCandidate, "*.fwdata", SearchOption.AllDirectories);
        if (sources.Length != 1)
            throw new AssessorUnavailableException(AssessorName,
                $"The candidate must contain exactly one .fwdata source; found {sources.Length}.");
        // The one seam for Assess and Trial: appended lowercase wordforms parse in the same batch (ADR 0078).
        var (parsedWords, lowercaseOrigin) = (wordsScope.Casing ?? _lowercasePass(sources[0], requestedWords))
            .Expand(requestedWords);
        requestedWords = parsedWords.ToArray();
        var invocationId = Guid.NewGuid().ToString("N");
        var directory = _paths.DirectoryFor(invocationId);
        using var cacheLock = StatsCacheLock.Create(directory);
        while (!cacheLock.TryAcquire())
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        var cachePath = wanted.Contains(AssessmentKind.ObjectTiming) ? Path.Combine(directory, "stats.sqlite") : null;
        AssessmentArtifactLease? artifactLease = null;
        try
        {
            var batch = new PanGlossRequest.Batch(
                sources[0], requestedWords, wordsScope.PerWordLimit, cachePath, wordsScope.PerWordStepLimit, directory)
                { CollectAnalyses = true, OnProgress = onProgress, OnAdmissionWait = onAdmissionWait };
            var outcome = await _invoker.RunAsync(batch,
                "assess:batch", cancellationToken).ConfigureAwait(false);
            artifactLease = (outcome as PanGlossOutcome.Completed)?.ArtifactLease;
            if (outcome is PanGlossOutcome.Cancelled)
                throw new OperationCanceledException(cancellationToken);
            if (outcome is not PanGlossOutcome.Completed { BatchEvidence: { } evidence } completed)
                throw new AssessorUnavailableException(AssessorName,
                    outcome is PanGlossOutcome.Completed ? "The invocation returned no retained evidence." : outcome.Message)
                {
                    ExecutableMissing = outcome is PanGlossOutcome.Unavailable { ExecutableMissing: true },
                    ParserRefusal = outcome as PanGlossOutcome.Refused,
                    MemoryKill = (outcome as PanGlossOutcome.MemoryLimit)?.Kill,
                };
            var matchingArtifacts = evidence.InvocationId == invocationId &&
                Path.GetFullPath(evidence.SourcePath) == Path.Combine(Path.GetFullPath(directory), "source.fwdata");
            if (!matchingArtifacts || evidence.PerWordStepLimit != wordsScope.PerWordStepLimit ||
                evidence.PerWordTimeoutMs != (wordsScope.PerWordLimit is { } timeLimit
                    ? (int)timeLimit.TotalMilliseconds : null) ||
                evidence.Threads != batch.ThreadCount || evidence.CollectStatistics != (cachePath is not null))
                throw new AssessorUnavailableException(AssessorName, "The invocation evidence does not match the requested scope.");
            try
            {
                if (BatchInvocationEvidence.DigestFile(evidence.TsvPath) != evidence.TsvSha256)
                    throw new InvalidDataException("The retained TSV artifact changed.");
                if (!BatchInvocationEvidence.TextMatchesDigest(completed.Output, evidence.TsvSha256))
                    throw new InvalidDataException("The completed TSV payload does not match retained evidence.");
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                throw new AssessorUnavailableException(AssessorName, exception.Message);
            }
            StoppedWordRetryRunner.Outcome? retried = null;
            // Words stopped at the wall-clock limit run again serially under a longer one; the record names each.
            if (completed.MorphologyOutput is { } firstAnalyses)
            {
                try
                {
                    retried = await StoppedWordRetryRunner.RunAsync(_invoker, _paths, batch, evidence,
                        completed.Output, firstAnalyses, onAdmissionWait, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new AssessorUnavailableException(AssessorName, exception.Message);
                }
                if (retried is not null)
                {
                    evidence = retried.Evidence;
                    completed = completed with { Output = retried.Tsv, MorphologyOutput = retried.Analyses };
                }
            }
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
            var warnings = completed.StandardError.Split('\n').Select(line => line.Trim())
                .Where(line => line.StartsWith("warning:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("capability:", StringComparison.OrdinalIgnoreCase)).ToArray();
            var unavailableWarnings = warnings.Where(IsUnsupportedGrammarWarning).ToArray();
            if (completed.MorphologyOutput is null || evidence.AnalysesPath is null || evidence.AnalysesSha256 is null)
                throw new AssessorUnavailableException(AssessorName, "The invocation returned no retained morphology evidence.");
            try
            {
                if (BatchInvocationEvidence.DigestFile(evidence.AnalysesPath) != evidence.AnalysesSha256)
                    throw new InvalidDataException("The retained morphology artifact changed.");
                if (!BatchInvocationEvidence.TextMatchesDigest(completed.MorphologyOutput, evidence.AnalysesSha256))
                    throw new InvalidDataException("The completed morphology payload does not match retained evidence.");
                var morphology = ParseMorphEvidence.Read(completed.MorphologyOutput, requestedWords);
                rows = rows.Select((row, index) =>
                {
                    var item = morphology[index];
                    var expectedOutcome = item.InvalidShape ? WordOutcome.Skipped : item.TimedOut ? WordOutcome.TimedOut
                        : item.Capped ? WordOutcome.Capped : item.Analyses.Count + item.Unavailable.Count > 0
                            ? WordOutcome.Analysed : WordOutcome.NoAnalysis;
                    if (row.ElapsedMs != item.ElapsedMs || row.Outcome != expectedOutcome)
                        throw new InvalidDataException("TSV and morphology evidence describe different search outcomes.");
                    var origin = lowercaseOrigin.GetValueOrDefault(row.Word);
                    if (retried is null || !retried.RetriedIndices.Contains(row.Index))
                        return row with { Morphology = item, LowercaseOf = origin };
                    var statistics = retried.RetryStatistics?.GetValueOrDefault(row.Word);
                    var attempts = statistics?.Attempts ??
                        (row.Steps is <= int.MaxValue and { } steps ? (int)steps : item.Attempts);
                    return row with
                    {
                        Morphology = item with { Attempts = attempts },
                        LowercaseOf = origin,
                        WasRetried = true,
                        Steps = statistics?.Attempts ?? row.Steps ?? item.Attempts,
                        Passes = statistics?.Passes,
                        WorkSteps = statistics?.WorkSteps ?? row.WorkSteps,
                        RetryStatistics = statistics,
                    };
                }).ToArray();
                if (unavailableWarnings.Length > 0)
                {
                    rows = rows.Select(row => row.Outcome == WordOutcome.NoAnalysis &&
                        row.Morphology is { Analyses.Count: 0 } item
                            ? row with
                            {
                                Morphology = item with
                                {
                                    Unavailable = item.Unavailable.Concat(unavailableWarnings).ToArray(),
                                },
                            }
                            : row).ToArray();
                }
                if (wanted.Contains(AssessmentKind.Correctness))
                {
                    IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> expected;
                    using (var cache = new FwDataProjectLoader().LoadScratchCache(evidence.SourcePath))
                        expected = ApprovedMorphologyReader.Read(cache, ApprovedMorphologyReader.FormsOf(rows.Select(row => row.Word)));
                    rows = rows.Select(row => row with
                    {
                        Correctness = MorphologyCorrectness.Compare(row.Morphology!,
                            expected.TryGetValue(row.Word, out var approved) ? approved : []),
                    }).ToArray();
                    if (_sourceDigest(evidence.SourcePath) != evidence.SourceBytesSha256)
                        throw new InvalidDataException("The source changed while reading approved expectations.");
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                throw new AssessorUnavailableException(AssessorName, exception.Message);
            }
            var analysis = new BatchAnalysis(rows, evidence.PerWordTimeoutMs, evidence.SourcePath, warnings)
            {
                PerWordStepLimit = evidence.PerWordStepLimit,
            };
            var recorded = evidence with
            {
                GrammarWarnings = warnings.Length == 0 ? null : string.Join("\n", warnings),
            };
            var results = new List<ProducedAssessment>();
            if (wanted.Contains(AssessmentKind.ParseTime))
                results.Add(Produced(AssessmentKind.ParseTime, new AssessmentRaw.Batch(analysis), recorded));
            if (cachePath is not null)
                results.Add(Produced(AssessmentKind.ObjectTiming,
                    new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath)), recorded));
            if (wanted.Contains(AssessmentKind.Correctness))
                results.Add(Produced(AssessmentKind.Correctness, new AssessmentRaw.Batch(analysis), recorded));
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

    private static bool IsUnsupportedGrammarWarning(string warning) =>
        warning.StartsWith("capability:", StringComparison.OrdinalIgnoreCase) ||
        warning.Contains("cannot be loaded as an affix rule", StringComparison.OrdinalIgnoreCase) ||
        warning.Contains("cannot be checked against the phoneme inventory", StringComparison.OrdinalIgnoreCase);
}
