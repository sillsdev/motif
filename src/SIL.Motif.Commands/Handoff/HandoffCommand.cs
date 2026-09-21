using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.LCModel;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.WritingSystems;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Handoff;

/// <summary>Which project to hand off, where to publish it, and whether to export a retained invocation.</summary>
public sealed record HandoffRequest(
    string ProjectPath,
    string OutputDirectory,
    SelectionRequest Selection,
    bool Assess,
    string? InvocationId = null);

/// <summary>
/// Writes the five-file AI Handoff (ADR 0045) by composing the commands that already exist:
/// <see cref="BaselineCaptureCommand"/> for a Baseline-only export, <see cref="AssessCommand"/> for a fresh
/// Assessment, or a selected retained invocation, which supplies its own Baseline, Selection, evidence, and
/// Assessment identities.
/// </summary>
/// <remarks>
/// <para>
/// The folder is never visible at its destination until <see cref="HandoffWriter.Publish"/> has
/// validated the complete listing: everything above is written into a sibling incoming directory first,
/// so a mid-run cancellation or PanGloss failure leaves no destination directory at all.
/// </para>
/// <para>
/// Progress is reported through the command-owned stages of <see cref="AssessmentStage"/>, which was
/// written for this run as much as for a bare Assessment. The nested <see cref="AssessCommand"/>'s own
/// stages are forwarded, minus its <see cref="AssessmentStage.Complete"/>: that is true of the Assessment
/// and false of the Handoff, which still has the grammar and the texts to write.
/// Reporting it would tell a caller the run had finished part way through, so this command swallows it and
/// reports its own once the folder is actually in place.
/// </para>
/// </remarks>
public static class HandoffCommand
{
    /// <summary>Writes a Handoff folder, resolving the managed root the real installation uses.</summary>
    public static CommandOutcome<HandoffCommandResponse> Handoff(
        HandoffRequest request, Action<AssessmentProgress>? onProgress = null,
        CancellationToken cancellationToken = default) =>
        Handoff(request, RunnerOptions.ResolveRoot(), onProgress, cancellationToken);

    /// <summary>
    /// Writes a Handoff folder under an explicitly supplied managed root. The single-argument overload is
    /// what production code and the CLI call; this one exists so a test can supply its own disposable root.
    /// </summary>
    public static CommandOutcome<HandoffCommandResponse> Handoff(
        HandoffRequest request, string managedRoot, Action<AssessmentProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var ownership = WorkspaceOwnership.Bootstrap(managedRoot);
        using var invoker = new PanGlossInvoker();
        var assessor = new LazyPanGlossAssessor(() => new PanGlossAssessor(new StatsCacheStore(ownership), invoker));
        return Run(request, managedRoot, assessor, invoker, onProgress, cancellationToken);
    }

    /// <summary>
    /// Writes a Handoff folder against explicitly supplied collaborators — a fake Assessor and a fake
    /// invoker stand in for a real PanGloss in tests. Admission and containment are the invoker's.
    /// </summary>
    internal static CommandOutcome<HandoffCommandResponse> Run(
        HandoffRequest request, string managedRoot, IAssessor assessor, IPanGlossInvoker invoker,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessor);
        ArgumentNullException.ThrowIfNull(invoker);

        if (request.Assess && string.IsNullOrWhiteSpace(request.InvocationId))
            return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                "handoff.invocation-required", FailureReason.InvalidArgument,
                "Run assess first or pass the completed Assessment invocation id."));

        if (File.Exists(request.OutputDirectory) ||
            (Directory.Exists(request.OutputDirectory) &&
                Directory.EnumerateFileSystemEntries(request.OutputDirectory).Any()))
        {
            return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                "handoff.destination-exists", FailureReason.InvalidArgument,
                $"The destination '{request.OutputDirectory}' already exists and is not empty.",
                Fact(("outputDirectory", request.OutputDirectory))));
        }

        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            try
            {
                BaselineCaptureResponse baseline;
                SelectionProjection selectionProjection;
                IReadOnlyList<HandoffWriter.AssessedWordStatistics>? assessedWords = null;
                string? statisticsAssessmentId = null;
                var assessmentIds = new List<string>();
                string? exportedInvocationId = null;
                RetainedInvocationRecord? retained = null;
                StatsEvidenceReplay? replay = null;
                LcmCache? retainedSource = null;
                string? languageName = null;
                string? projectName = null;

                if (request.Assess && request.InvocationId is not null)
                {
                    try
                    {
                        retained = new RetainedInvocationRepository(database).Get(request.InvocationId);
                    }
                    catch (KeyNotFoundException)
                    {
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.invocation-not-found", FailureReason.NotFound,
                            $"Retained invocation '{request.InvocationId}' was not found.",
                            Fact(("invocationId", request.InvocationId))));
                    }

                    var workspaceKey = ProjectWorkspaceKey.Compute(project);
                    if (!StringComparer.Ordinal.Equals(retained.ProjectKey, workspaceKey))
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.invocation-mismatch", FailureReason.Refused,
                            $"Retained invocation '{request.InvocationId}' belongs to another project.",
                            Fact(("invocationId", request.InvocationId))));

                    var sourceAssessment = retained.Assessments.FirstOrDefault(item =>
                        item.Kind.IsStoredKind(AssessmentKind.ParseTime));
                    var statisticsAssessment = retained.Assessments.FirstOrDefault(item =>
                        item.Kind.IsStoredKind(AssessmentKind.ObjectTiming));
                    if (sourceAssessment?.Invocation is null ||
                        string.IsNullOrWhiteSpace(sourceAssessment.Invocation.SourcePath) ||
                        string.IsNullOrWhiteSpace(sourceAssessment.Invocation.SourceBytesSha256) ||
                        !File.Exists(sourceAssessment.Invocation.SourcePath))
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.source-unavailable", FailureReason.Refused,
                            $"Retained invocation '{request.InvocationId}' has no available source evidence.",
                            Fact(("invocationId", request.InvocationId))));

                    var sourceRefusal = ValidateRetainedSources(retained, request.InvocationId);
                    if (sourceRefusal is not null)
                        return CommandOutcome<HandoffCommandResponse>.Refused(sourceRefusal);

                    if (statisticsAssessment?.Invocation is null ||
                        string.IsNullOrWhiteSpace(statisticsAssessment.CachePath) ||
                        string.IsNullOrWhiteSpace(statisticsAssessment.CacheDigest))
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.statistics-unavailable", FailureReason.Refused,
                            $"Retained invocation '{request.InvocationId}' has no available statistics evidence.",
                            Fact(("invocationId", request.InvocationId))));

                    try
                    {
                        replay = StatsEvidenceReplay.Create(
                            statisticsAssessment.Invocation, statisticsAssessment.CachePath,
                            statisticsAssessment.CacheDigest);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or
                        UnauthorizedAccessException or ArgumentException or KeyNotFoundException)
                    {
                        replay?.Dispose();
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.statistics-unavailable", FailureReason.Refused, exception.Message,
                            Fact(("invocationId", request.InvocationId))));
                    }

                    try
                    {
                        if (!File.Exists(retained.BaselineFwDataPath))
                            throw new FileNotFoundException(
                                "The retained Baseline source is not available.", retained.BaselineFwDataPath);
                        retainedSource = new FwDataProjectLoader().LoadScratchCache(retained.BaselineFwDataPath);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or
                        UnauthorizedAccessException or ArgumentException)
                    {
                        replay?.Dispose();
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.source-unavailable", FailureReason.Refused, exception.Message,
                            Fact(("invocationId", request.InvocationId))));
                    }

                    Guid? missingTextId = null;
                    var retainedTextRepository = retainedSource.ServiceLocator.GetInstance<ITextRepository>();
                    foreach (var textId in retained.Selection.TextIds)
                    {
                        if (!retainedTextRepository.TryGetObject(textId, out _))
                        {
                            missingTextId = textId;
                            break;
                        }
                    }
                    if (missingTextId is not null)
                    {
                        retainedSource.Dispose();
                        replay.Dispose();
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.text-not-found", FailureReason.InvalidArgument,
                            $"Text '{missingTextId:D}' is not present in the retained Baseline source.",
                            Fact(("textId", missingTextId.Value.ToString("D")))));
                    }

                    baseline = new BaselineCaptureResponse(
                        retained.BaselineToken, retained.BaselineFwDataPath,
                        retained.BaselineSourceLastWriteUtc, false, true);
                    selectionProjection = new SelectionProjection(
                        retained.Selection.ResolvedWords, retained.Selection.SourceCounts);
                    assessmentIds.AddRange(retained.Members.Select(member => member.AssessmentId));
                    exportedInvocationId = retained.InvocationId;
                    statisticsAssessmentId = statisticsAssessment.AssessmentId;
                    languageName = LanguageNameOf(retainedSource);
                    projectName = retainedSource.ProjectId.Name;

                    var parseAssessment = retained.Assessments.FirstOrDefault(item =>
                        item.Kind.IsStoredKind(AssessmentKind.ParseTime));
                    if (parseAssessment is null)
                    {
                        retainedSource.Dispose();
                        replay.Dispose();
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.statistics-unavailable", FailureReason.Refused,
                            $"Retained invocation '{request.InvocationId}' has no ParseTime Assessment.",
                            Fact(("invocationId", request.InvocationId))));
                    }
                    assessedWords = (parseAssessment.Words ?? []).Select(word =>
                        new HandoffWriter.AssessedWordStatistics(word.Word, word.Outcome, word.ElapsedMs, word.RawSignature))
                        .ToList();
                }
                else if (request.Assess)
                {
                    var assessOutcome = AssessCommand.Run(
                        new AssessRequest(request.ProjectPath, request.Selection), managedRoot,
                        assessor, invoker, ForwardExceptComplete(onProgress),
                        cancellationToken);
                    if (!assessOutcome.Succeeded)
                    {
                        // The person cancelled a Handoff, not an Assessment; the nested code must not leak out.
                        var cancelled = cancellationToken.IsCancellationRequested
                            || assessOutcome.Refusal!.Reason == FailureReason.Cancelled;
                        return CommandOutcome<HandoffCommandResponse>.Refused(
                            cancelled ? Cancelled(request.ProjectPath) : assessOutcome.Refusal!);
                    }

                    baseline = assessOutcome.Value!.Baseline;
                    selectionProjection = assessOutcome.Value!.Selection;
                    assessmentIds.AddRange(assessOutcome.Value!.AssessmentIds);
                    statisticsAssessmentId = assessOutcome.Value.Measurements
                        .SingleOrDefault(item => item.Kind == AssessmentKind.ObjectTiming.ToStoredKind())?.AssessmentId;
                    if (statisticsAssessmentId is null)
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.statistics-unavailable", FailureReason.Refused,
                            "The Assessment returned no identified ObjectTiming measurement for this Handoff."));
                    assessedWords = assessOutcome.Value.Words
                        .Select(word => new HandoffWriter.AssessedWordStatistics(
                            word.Word, word.Outcome, word.ElapsedMs, word.RawSignature))
                        .ToList();
                }
                else
                {
                    var baselineOutcome = BaselineCaptureCommand.Capture(
                        new BaselineCaptureRequest(request.ProjectPath), managedRoot);
                    if (!baselineOutcome.Succeeded)
                        return CommandOutcome<HandoffCommandResponse>.Refused(baselineOutcome.Refusal!);
                    baseline = baselineOutcome.Value!;

                    using var selectionCache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
                    var composed = SelectionComposer.Compose(
                        selectionCache, request.Selection, new AssessmentRepository(database));
                    if (!composed.Succeeded)
                        return CommandOutcome<HandoffCommandResponse>.Refused(composed.Refusal!);
                    selectionProjection = composed.Value!.Projection;
                    languageName = LanguageNameOf(selectionCache);
                    projectName = selectionCache.ProjectId.Name;
                }

                if (statisticsAssessmentId is not null && replay is null)
                {
                    try
                    {
                        var recorded = new AssessmentRepository(database).Get(statisticsAssessmentId);
                        if (!recorded.Kind.IsStoredKind(AssessmentKind.ObjectTiming) || recorded.Invocation is null ||
                            recorded.CachePath is null || recorded.CacheDigest is null)
                            throw new InvalidDataException("The Handoff's ObjectTiming Assessment has no complete retained evidence.");
                        replay = StatsEvidenceReplay.Create(recorded.Invocation, recorded.CachePath, recorded.CacheDigest);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or
                        UnauthorizedAccessException or ArgumentException or KeyNotFoundException)
                    {
                        return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                            "handoff.statistics-unavailable", FailureReason.Refused, exception.Message,
                            Fact(("assessmentId", statisticsAssessmentId))));
                    }
                }
                using var replayLease = replay;
                using var retainedSourceLease = retainedSource;
                var grammarPath = request.Assess ? replay!.GrammarPath : baseline.FwDataPath;

                var writeRefusal = HandoffWriter.Publish(request.OutputDirectory, request.Assess, incoming =>
                {
                    Refusal? textsRefusal;
                    string? sampleTextKey;
                    if (retainedSource is not null)
                    {
                        textsRefusal = HandoffWriter.WriteTextsJson(
                            retainedSource, retained?.Selection.TextIds ?? request.Selection.TextIds, incoming,
                            out sampleTextKey);
                    }
                    else
                    {
                        using var cache = new FwDataProjectLoader().LoadScratchCache(grammarPath);
                        languageName ??= LanguageNameOf(cache);
                        projectName ??= cache.ProjectId.Name;
                        textsRefusal = HandoffWriter.WriteTextsJson(
                            cache, request.Selection.TextIds, incoming, out sampleTextKey);
                    }
                    if (textsRefusal is not null) return textsRefusal;

                    HandoffWriter.WritePythonHelper(incoming);

                    Report(onProgress, AssessmentStage.ImportingGrammar, "Importing the grammar...");
                    var import = invoker.RunAsync(
                            new PanGlossRequest.Import(grammarPath, Path.Combine(incoming, HandoffWriter.GrammarFileName)),
                            "handoff:import:" + request.ProjectPath, cancellationToken)
                        .GetAwaiter().GetResult();
                    if (import is PanGlossOutcome.Cancelled) return Cancelled(request.ProjectPath);
                    if (import is not PanGlossOutcome.Completed)
                    {
                        return new Refusal("handoff.parser-unavailable", FailureReason.Refused, import.Message,
                            Fact(("projectPath", request.ProjectPath)));
                    }

                    if (request.Assess)
                        HandoffWriter.WriteAssessmentJson(incoming, assessedWords ?? []);

                    var sampleWord = selectionProjection.Words.Count > 0 ? selectionProjection.Words[0] : "word";
                    var handoffMarkdown = HandoffWriter.BuildHandoffMarkdown(
                        request.Assess, sampleTextKey ?? "text", sampleWord);
                    File.WriteAllText(Path.Combine(incoming, HandoffWriter.HandoffMarkdownFileName), handoffMarkdown);

                    return null;
                });

                if (writeRefusal is not null)
                    return CommandOutcome<HandoffCommandResponse>.Refused(writeRefusal);

                Report(onProgress, AssessmentStage.Complete, "Handoff complete.");
                var files = HandoffWriter.ListFiles(request.OutputDirectory);
                var pastedHeader = HandoffWriter.BuildPastedHeader(
                    languageName ?? "the language", projectName ?? Path.GetFileNameWithoutExtension(request.ProjectPath));
                var handoffMarkdownContent = File.ReadAllText(
                    Path.Combine(request.OutputDirectory, HandoffWriter.HandoffMarkdownFileName));
                return CommandOutcome<HandoffCommandResponse>.Success(new HandoffCommandResponse(
                    request.OutputDirectory, baseline, selectionProjection, files, assessmentIds)
                {
                    InvocationId = exportedInvocationId,
                    PastedHeader = pastedHeader,
                    HandoffMarkdown = handoffMarkdownContent,
                });
            }
            catch (OperationCanceledException)
            {
                return CommandOutcome<HandoffCommandResponse>.Refused(Cancelled(request.ProjectPath));
            }
        });
    }

    private static string LanguageNameOf(LcmCache cache) =>
        WritingSystemInventoryReader.Read(cache).DefaultVernacular?.Name is { Length: > 0 } name
            ? name
            : "the language";

    private static void Report(Action<AssessmentProgress>? onProgress, AssessmentStage stage, string message) =>
        onProgress?.Invoke(new AssessmentProgress(stage, 0, null, message));

    /// Passes the nested Assessment's own stages on, except the one that says it finished.
    private static Action<AssessmentProgress>? ForwardExceptComplete(Action<AssessmentProgress>? onProgress) =>
        onProgress is null
            ? null
            : progress =>
            {
                if (progress.Stage != AssessmentStage.Complete) onProgress(progress);
            };

    private static Refusal Cancelled(string projectPath) => new(
        "handoff.cancelled", FailureReason.Cancelled,
        "The Handoff run was cancelled; no destination directory was created.",
        Fact(("projectPath", projectPath)));

    private static Refusal? ValidateRetainedSources(
        RetainedInvocationRecord retained, string invocationId)
    {
        foreach (var assessment in retained.Assessments)
        {
            var evidence = assessment.Invocation;
            if (evidence is null || string.IsNullOrWhiteSpace(evidence.SourcePath) ||
                string.IsNullOrWhiteSpace(evidence.SourceBytesSha256) || !File.Exists(evidence.SourcePath))
                return new Refusal(
                    "handoff.source-unavailable", FailureReason.Refused,
                    $"Retained invocation '{invocationId}' has no available source evidence.",
                    Fact(("invocationId", invocationId)));

            try
            {
                if (BatchInvocationEvidence.DigestFile(evidence.SourcePath) != evidence.SourceBytesSha256)
                    return new Refusal(
                        "handoff.source-unavailable", FailureReason.Refused,
                        $"Retained invocation '{invocationId}' has changed source evidence.",
                        Fact(("invocationId", invocationId)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new Refusal(
                    "handoff.source-unavailable", FailureReason.Refused, exception.Message,
                    Fact(("invocationId", invocationId)));
            }
        }

        return null;
    }

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;

    private static Dictionary<string, string> Fact(params (string Key, string Value)[] facts)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in facts) dictionary[key] = value;
        return dictionary;
    }
}
