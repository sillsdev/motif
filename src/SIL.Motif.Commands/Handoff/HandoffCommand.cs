using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Handoff;

/// <summary>Which project to hand off, where to publish it, what to include, and how to select its Texts and words.</summary>
public sealed record HandoffRequest(
    string ProjectPath,
    string OutputDirectory,
    SelectionRequest Selection,
    bool WriteFlexTextXml,
    bool Assess);

/// <summary>
/// Writes the self-explaining AI Handoff folder (design decision 6) by composing the commands that
/// already exist: <see cref="BaselineCaptureCommand"/> for the project copy, <see cref="AssessCommand"/>
/// for the optional measurement and its statistics summary, <see cref="StatsCommand"/> for the six
/// per-group JSONL files, and <see cref="IPanGlossGrammarImporter"/> for the grammar snapshot. None of
/// their internals are reimplemented here.
/// </summary>
/// <remarks>
/// <para>
/// The folder is never visible at its destination until <see cref="HandoffWriter.Publish"/> has
/// validated the complete listing: everything above is written into a sibling incoming directory first,
/// so a mid-run cancellation or PanGloss failure leaves no destination directory at all.
/// </para>
/// <para>
/// The grammar importer arrives as a factory, the same seam <see cref="AssessCommand"/> and
/// <see cref="StatsCommand"/> already use for their own PanGloss collaborators: it is built only after
/// the project has resolved, so a mistyped path is refused as <c>project.not-found</c> rather than a
/// missing parser.
/// </para>
/// <para>
/// Progress is reported through the command-owned stages of <see cref="AssessmentStage"/>, which was
/// written for this run as much as for a bare Assessment. The nested <see cref="AssessCommand"/>'s own
/// stages are forwarded, minus its <see cref="AssessmentStage.Complete"/>: that is true of the Assessment
/// and false of the Handoff, which still has the grammar, the texts, and six statistics files to write.
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
        using var queue = new MachinePanGlossQueue();
        return Run(request, managedRoot,
            () => new PanGlossAssessor(new StatsCacheStore(ownership)),
            () => new PanGlossStatsQueryProcess(),
            () => new PanGlossGrammarImportProcess(),
            queue, onProgress, cancellationToken);
    }

    /// <summary>
    /// Writes a Handoff folder against explicitly supplied collaborators — fakes stand in for the real
    /// PanGloss subprocesses in tests, and a custom-slotted <see cref="MachinePanGlossQueue"/> keeps a
    /// test's admission race off the machine's real, well-known slots.
    /// </summary>
    internal static CommandOutcome<HandoffCommandResponse> Run(
        HandoffRequest request, string managedRoot,
        Func<IAssessor> assessorFactory, Func<IPanGlossStatsQuery> statsQueryFactory,
        Func<IPanGlossGrammarImporter> grammarImporterFactory, MachinePanGlossQueue queue,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessorFactory);
        ArgumentNullException.ThrowIfNull(statsQueryFactory);
        ArgumentNullException.ThrowIfNull(grammarImporterFactory);
        ArgumentNullException.ThrowIfNull(queue);

        if (File.Exists(request.OutputDirectory) ||
            (Directory.Exists(request.OutputDirectory) &&
                Directory.EnumerateFileSystemEntries(request.OutputDirectory).Any()))
        {
            return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                "handoff.destination-exists", FailureReason.InvalidArgument,
                $"The destination '{request.OutputDirectory}' already exists and is not empty.",
                Fact(("outputDirectory", request.OutputDirectory))));
        }

        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, _) =>
        {
            try
            {
                var grammarImporter = grammarImporterFactory();

                BaselineCaptureResponse baseline;
                SelectionProjection selectionProjection;
                string? statisticsMarkdown = null;
                var assessmentIds = new List<string>();

                if (request.Assess)
                {
                    var assessOutcome = AssessCommand.Run(
                        new AssessRequest(request.ProjectPath, request.Selection), managedRoot,
                        assessorFactory, statsQueryFactory, queue, ForwardExceptComplete(onProgress),
                        cancellationToken);
                    if (!assessOutcome.Succeeded)
                        return CommandOutcome<HandoffCommandResponse>.Refused(assessOutcome.Refusal!);

                    baseline = assessOutcome.Value!.Baseline;
                    selectionProjection = assessOutcome.Value!.Selection;
                    statisticsMarkdown = assessOutcome.Value!.SummaryMarkdown;
                    assessmentIds.AddRange(assessOutcome.Value!.AssessmentIds);
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
                }

                var writeRefusal = HandoffWriter.Publish(request.OutputDirectory, request.Assess, incoming =>
                {
                    using (var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath))
                        HandoffWriter.WriteTexts(cache, request.Selection.TextIds, incoming, request.WriteFlexTextXml);

                    HandoffWriter.WriteSelectionTxt(incoming, selectionProjection);

                    Report(onProgress, AssessmentStage.ImportingGrammar, "Importing the grammar...");
                    queue.RunAsync<object?>(
                        "handoff:import:" + request.ProjectPath,
                        async (cpuJob, jobToken) =>
                        {
                            await grammarImporter.ImportAsync(
                                    baseline.FwDataPath, Path.Combine(incoming, HandoffWriter.GrammarFileName),
                                    jobToken, new WindowsCpuJobGovernor(cpuJob))
                                .ConfigureAwait(false);
                            return null;
                        },
                        cancellationToken).GetAwaiter().GetResult();

                    if (request.Assess)
                    {
                        File.WriteAllText(
                            Path.Combine(incoming, HandoffWriter.StatisticsSummaryFileName), statisticsMarkdown);
                        Report(onProgress, AssessmentStage.ReadingStatistics, "Reading PanGloss's statistics...");
                        var statisticsDir = Directory.CreateDirectory(
                            Path.Combine(incoming, HandoffWriter.StatisticsDirectoryName)).FullName;

                        var groupRefusal = queue.RunAsync<Refusal?>(
                            "handoff:stats:" + request.ProjectPath,
                            (cpuJob, jobToken) =>
                            {
                                var governor = new WindowsCpuJobGovernor(cpuJob);
                                foreach (var group in HandoffWriter.StatisticsGroups)
                                {
                                    var groupOutcome = StatsCommand.Run(
                                        new StatsRequest(
                                            request.ProjectPath, null, StatsOutputKind.Text,
                                            new[] { "--group", group, "--format", "jsonl" }),
                                        statsQueryFactory, jobToken, governor);
                                    if (!groupOutcome.Succeeded) return Task.FromResult(groupOutcome.Refusal);

                                    File.WriteAllText(
                                        Path.Combine(statisticsDir, group + ".jsonl"), groupOutcome.Value!.Text);
                                }
                                return Task.FromResult<Refusal?>(null);
                            },
                            cancellationToken).GetAwaiter().GetResult();
                        if (groupRefusal is not null) return groupRefusal;
                    }

                    HandoffWriter.WriteEmbeddedAssets(incoming);
                    return null;
                });

                if (writeRefusal is not null)
                    return CommandOutcome<HandoffCommandResponse>.Refused(writeRefusal);

                Report(onProgress, AssessmentStage.Complete, "Handoff complete.");
                var files = HandoffWriter.ListFiles(request.OutputDirectory);
                return CommandOutcome<HandoffCommandResponse>.Success(new HandoffCommandResponse(
                    request.OutputDirectory, baseline, selectionProjection, files, assessmentIds));
            }
            catch (OperationCanceledException)
            {
                return CommandOutcome<HandoffCommandResponse>.Refused(Cancelled(request.ProjectPath));
            }
            catch (ParserUnavailableException ex)
            {
                return CommandOutcome<HandoffCommandResponse>.Refused(new Refusal(
                    "handoff.parser-unavailable", FailureReason.Refused, ex.Message,
                    Fact(("projectPath", request.ProjectPath))));
            }
        });
    }

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
        "handoff.cancelled", FailureReason.Refused,
        "The Handoff run was cancelled; no destination directory was created.",
        Fact(("projectPath", projectPath)));

    private static string ResolveProductVersion() =>
        typeof(HandoffCommand).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private static Dictionary<string, string> Fact(params (string Key, string Value)[] facts)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in facts) dictionary[key] = value;
        return dictionary;
    }
}
