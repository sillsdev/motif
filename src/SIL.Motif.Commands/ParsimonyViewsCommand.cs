using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Projection.HumanJudgments;

namespace SIL.Motif.Commands;

/// <summary>Lists the fixed Parsimony catalog and reads bounded named views from published bundles.</summary>
public static class ParsimonyViewsCommand
{
    /// <summary>Lists registered measures and fixed named views without reading project artifacts.</summary>
    public static CommandOutcome<ParsimonyMeasureCatalogResponse> Measures(ListParsimonyMeasuresRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FwDataPath) || string.IsNullOrWhiteSpace(request.ProductVersion))
            return Refused<ParsimonyMeasureCatalogResponse>("parsimony.invalid-catalog-request",
                FailureReason.InvalidArgument, "A project path and product version are required.");
        return CommandOutcome<ParsimonyMeasureCatalogResponse>.Success(new ParsimonyMeasureCatalogResponse(
            MeasureCatalog.MeasureSetVersion, MeasureCatalog.All, ParsimonyViewCatalog.All));
    }

    /// <summary>Reads one fixed view from the exact bundle resolved in the current project store.</summary>
    public static CommandOutcome<ParsimonyNamedViewResponse> View(ReadParsimonyViewRequest request) =>
        ViewWithLeaseSignal(request, CancellationToken.None);

    /// <summary>Reads a view while also stopping it when the caller signals that its reader lease is lost.</summary>
    internal static CommandOutcome<ParsimonyNamedViewResponse> ViewWithLeaseSignal(ReadParsimonyViewRequest request,
        CancellationToken leaseLoss)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Query is null)
            return Refused<ParsimonyNamedViewResponse>("parsimony.invalid-view-request",
                FailureReason.InvalidArgument, "A named-view query is required.");
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var artifacts = new EvidenceArtifactRepository(database);
            var bundle = artifacts.Get(workspaceKey, request.Query.BundleId);
            if (bundle is null)
            {
                if (CanListSavedDecisions(request.Query))
                    return ReadSavedDecisions(project, request.Query);
                return Refused<ParsimonyNamedViewResponse>("parsimony.bundle-not-found", FailureReason.NotFound,
                    $"Parsimony bundle '{request.Query.BundleId}' was not found in this project.");
            }
            try
            {
                ParsimonyReportResponse? storedReport = null;
                if (request.Query.Filters.ReportId is { } reportId)
                {
                    var bundleToken = ReadBundleToken(bundle);
                    try
                    {
                        storedReport = ReadBoundReport(database, reportId, bundle, bundleToken);
                    }
                    // A wrong or damaged Report says nothing about the bundle, so it must not reach the bundle classifier.
                    catch (Exception exception) when (
                        exception is KeyNotFoundException or InvalidDataException or JsonException
                            or ReportNotBoundException)
                    {
                        if (CanListSavedDecisions(request.Query))
                            return ReadSavedDecisions(project, request.Query);
                        return RefuseReport(reportId, exception);
                    }
                }
                if (CanListSavedDecisions(request.Query) &&
                    (!Directory.Exists(bundle.EvidenceDirectory) || !File.Exists(bundle.GrammarFactsPath) ||
                     !File.Exists(bundle.EvidencePath)))
                    return ReadSavedDecisions(project, request.Query);
                using var lease = artifacts.AcquireReaderLease(bundle.BundleId,
                    "view-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                lease.StartHeartbeat();
                using var leaseSignal = CancellationTokenSource.CreateLinkedTokenSource(lease.CancellationToken,
                    leaseLoss);
                ValidateBundlePaths(bundle, WorkspaceOwnership.Bootstrap(RunnerOptions.ResolveRoot()), workspaceKey);
                var token = ReadBundleToken(bundle);
                var inputs = storedReport?.Inputs ?? new ParsimonyReportInputs(bundle.BundleId, token, "baseline", null,
                    bundle.ModelFingerprint,
                    new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, bundle.GrammarFactsSha256),
                    new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
                    null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
                using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs,
                    leaseSignal.Token);
                var result = ParsimonyViewsQuery.Execute(session, request.Query, storedReport);
                session.ThrowIfLeaseLost();
                return CommandOutcome<ParsimonyNamedViewResponse>.Success(result);
            }
            catch (ParsimonyLeaseLostException)
            {
                return Refused<ParsimonyNamedViewResponse>("parsimony.lease-lost", FailureReason.Busy,
                    "The Parsimony bundle's reader lease was lost before the view finished; retry the view.");
            }
            catch (KeyNotFoundException exception)
            {
                return Refused<ParsimonyNamedViewResponse>("parsimony.view-item-not-found", FailureReason.NotFound,
                    exception.Message);
            }
            // Cancellation and out-of-memory propagate; every other failure is classified, and unknown ones mark nothing.
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
            {
                if (exception is ArgumentException)
                    return Refused<ParsimonyNamedViewResponse>("parsimony.invalid-view-request",
                        FailureReason.InvalidArgument, exception.Message);
                var fault = ParsimonyBundleFaults.Classify(exception,
                    [bundle.EvidenceDirectory, bundle.GrammarFactsPath, bundle.EvidencePath]);
                if (fault == ParsimonyBundleFault.Busy)
                    return Refused<ParsimonyNamedViewResponse>("parsimony.bundle-busy", FailureReason.Busy,
                        exception.Message);
                if (fault is { } known)
                    artifacts.MarkUnavailable(bundle.BundleId, known, exception.Message);
                return Refused<ParsimonyNamedViewResponse>("parsimony.bundle-invalid",
                    FailureReason.StoreInconsistent, exception.Message);
            }
        });
    }

    private static bool CanListSavedDecisions(ParsimonyNamedViewRequest query) =>
        query.View is "parsimony-suppressed" or "parsimony-suppression-history";

    private static CommandOutcome<ParsimonyNamedViewResponse> ReadSavedDecisions(ProjectLocator project,
        ParsimonyNamedViewRequest query)
    {
        try
        {
            var judgments = ProjectReadCache.ReadSaved(project, (_, cache) =>
                JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(cache)));
            return CommandOutcome<ParsimonyNamedViewResponse>.Success(
                ParsimonyViewsQuery.ExecuteUnavailableEvidence(query, judgments));
        }
        catch (ArgumentException exception)
        {
            return Refused<ParsimonyNamedViewResponse>("parsimony.invalid-view-request",
                FailureReason.InvalidArgument, exception.Message);
        }
    }

    private static ParsimonyReportResponse ReadBoundReport(MotifDatabase database, string reportId,
        ParsimonyBundleRecord bundle, SIL.Motif.Contract.Baselines.BaselineToken bundleToken)
    {
        var stored = new ReportRepository(database).Get(reportId);
        if (stored is null || stored.Kind != "parsimony")
            throw new KeyNotFoundException($"Parsimony Report '{reportId}' was not found.");
        var response = JsonSerializer.Deserialize<ParsimonyReportResponse>(stored.ReportJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (response is null || !ParsimonyCommands.IsComplete(response))
            throw new InvalidDataException($"The stored Parsimony Report '{reportId}' is damaged.");
        if (response.ReportId != reportId || response.Inputs.BundleId != bundle.BundleId ||
            response.Inputs.Evidence.Sha256 != bundle.EvidenceSha256 ||
            response.Inputs.GrammarFacts.Sha256 != bundle.GrammarFactsSha256 ||
            response.Inputs.BaselineToken != bundleToken)
            throw new ReportNotBoundException(
                $"Parsimony Report '{reportId}' was not produced from bundle '{bundle.BundleId}'.");
        return response;
    }

    private static SIL.Motif.Contract.Baselines.BaselineToken ReadBundleToken(ParsimonyBundleRecord bundle) =>
        JsonSerializer.Deserialize<SIL.Motif.Contract.Baselines.BaselineToken>(bundle.BaselineTokenJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidDataException("The bundle has no Baseline token.");

    private static CommandOutcome<ParsimonyNamedViewResponse> RefuseReport(string reportId, Exception exception) =>
        exception switch
        {
            KeyNotFoundException => Refused<ParsimonyNamedViewResponse>("parsimony.report-not-found",
                FailureReason.NotFound, exception.Message),
            ReportNotBoundException => Refused<ParsimonyNamedViewResponse>("parsimony.report-bundle-mismatch",
                FailureReason.Refused, exception.Message),
            InvalidDataException => Refused<ParsimonyNamedViewResponse>("parsimony.report-damaged",
                FailureReason.StoreInconsistent, exception.Message),
            _ => Refused<ParsimonyNamedViewResponse>("parsimony.report-damaged", FailureReason.StoreInconsistent,
                $"Parsimony Report '{reportId}' is damaged: {exception.Message}"),
        };

    private sealed class ReportNotBoundException(string message) : Exception(message);

    private static void ValidateBundlePaths(ParsimonyBundleRecord bundle, WorkspaceOwnership ownership,
        string projectKey)
    {
        var directory = Path.GetFullPath(bundle.EvidenceDirectory);
        var facts = Path.GetFullPath(bundle.GrammarFactsPath);
        var evidence = Path.GetFullPath(bundle.EvidencePath);
        var expectedRoot = ParsimonyArtifactPath.ProjectRoot(ownership.WorkerRoot, projectKey);
        var expectedDirectory = Path.GetFullPath(ParsimonyArtifactPath.BundleDirectory(ownership.WorkerRoot,
            projectKey, bundle.BaselineDigest, bundle.ModelFingerprint, bundle.MaterialKey, bundle.InputKind,
            bundle.CandidateIdentity, bundle.BundleId));
        var relative = Path.GetRelativePath(expectedRoot, directory);
        if (!StringComparer.Ordinal.Equals(facts, Path.Combine(directory, "grammar-facts.sqlite")) ||
            !StringComparer.Ordinal.Equals(evidence, Path.Combine(directory, "evidence.sqlite")) ||
            !Directory.Exists(directory) || !File.Exists(facts) || !File.Exists(evidence) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(facts) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(evidence) & FileAttributes.ReparsePoint) != 0 ||
            !ownership.IsOwned(directory) || relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative) ||
            !StringComparer.Ordinal.Equals(directory, expectedDirectory) ||
            !StringComparer.Ordinal.Equals(Path.GetFileName(directory),
                ParsimonyArtifactPath.BundleDirectoryName(bundle.BundleId)))
            throw new InvalidDataException("The registered Parsimony bundle does not contain its two closed artifact files.");
    }

    private static CommandOutcome<T> Refused<T>(string code, FailureReason reason, string message) where T : class =>
        CommandOutcome<T>.Refused(new Refusal(code, reason, message));
}
