using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.HumanJudgments;
using System.Threading;

namespace SIL.Motif.Commands;

/// <summary>Prepares a FieldWorks project for Motif human judgments after explicit confirmation.</summary>
public static class ProjectInitializationCommand
{
    /// <summary>The exact confirmation sentence accepted by both Motif front ends.</summary>
    public const string ConfirmationPhrase = "Initialize this project to work with Motif Proposals?";

    /// <summary>Initializes a project using the installation's configured worker root.</summary>
    public static CommandOutcome<ProjectInitializationResponse> Initialize(ProjectInitializationRequest request) =>
        Initialize(request, SIL.Motif.Worker.RunnerOptions.ResolveRoot(), new FwDataProjectLoader());

    /// <summary>Initializes a project using an explicit root and optional loader for controlled callers.</summary>
    public static CommandOutcome<ProjectInitializationResponse> Initialize(
        ProjectInitializationRequest request,
        string workerRoot,
        FwDataProjectLoader? loader = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Confirmation, ConfirmationPhrase, StringComparison.Ordinal))
            return Refused(
                "initialization.confirmation-required", FailureReason.InvalidArgument,
                $"Confirm this action with: {ConfirmationPhrase}",
                ("confirmation", request.Confirmation));
        if (cancellationToken.IsCancellationRequested)
            return CancellationRefusal(request.ProjectPath);
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || string.IsNullOrWhiteSpace(workerRoot))
            return Refused("project.invalid", FailureReason.InvalidArgument,
                "A project path and Motif worker root are required.");

        string projectPath;
        string recoveryCopyPath;
        try
        {
            projectPath = Path.GetFullPath(request.ProjectPath);
            if (!File.Exists(projectPath))
                return Refused("project.not-found", FailureReason.NotFound,
                    $"Project file not found: '{projectPath}'.", ("fwDataPath", projectPath));
            recoveryCopyPath = ProjectInitializationRecovery.GetRecoveryCopyPath(projectPath, workerRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Refused("project.invalid", FailureReason.InvalidArgument, exception.Message,
                ("fwDataPath", request.ProjectPath));
        }

        loader ??= new FwDataProjectLoader();

        LcmCache? cache;
        try
        {
            cache = loader.LoadCache(projectPath);
        }
        catch (LcmFileLockedException)
        {
            return ProjectLockedRefusal(projectPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or LcmInitializationException)
        {
            if (File.Exists(recoveryCopyPath))
                return UnknownOutcome(projectPath, recoveryCopyPath,
                    "Motif could not reopen the saved project after an initialization attempt. " + exception.Message);
            return Refused("project.unloadable", FailureReason.Refused,
                "Motif could not establish ownership of and read the saved project: " + exception.Message,
                ("fwDataPath", projectPath));
        }

        HumanJudgmentFieldInspection initialInspection;
        try
        {
            initialInspection = HumanJudgmentFieldDefinition.Inspect(cache);
        }
        catch (Exception exception)
        {
            cache.Dispose();
            if (File.Exists(recoveryCopyPath))
                return UnknownOutcome(projectPath, recoveryCopyPath,
                    "Motif could not inspect the saved judgment field after an initialization attempt. " + exception.Message);
            return Refused("project.unloadable", FailureReason.Refused,
                "Motif could not inspect the saved project's custom-field definitions: " + exception.Message,
                ("fwDataPath", projectPath));
        }

        if (initialInspection.State == HumanJudgmentFieldState.Compatible)
        {
            cache.Dispose();
            var retainedCopy = DeleteVerifiedRecoveryCopy(recoveryCopyPath) ? null : ExistingRecoveryPath(recoveryCopyPath);
            return CommandOutcome<ProjectInitializationResponse>.Success(
                new ProjectInitializationResponse("already-initialized", HumanJudgmentFieldDefinition.InternalName,
                    retainedCopy));
        }
        if (initialInspection.State is HumanJudgmentFieldState.Incompatible or HumanJudgmentFieldState.Ambiguous)
        {
            cache.Dispose();
            var retainedRecoveryCopy = ExistingRecoveryPath(recoveryCopyPath);
            if (retainedRecoveryCopy is not null)
            {
                try
                {
                    if (!ProjectInitializationRecovery.MatchesCurrentProject(projectPath, retainedRecoveryCopy))
                        return UnknownOutcome(projectPath, retainedRecoveryCopy,
                            "The saved project changed after an initialization attempt and now has an incompatible or ambiguous reserved field.");
                }
                catch (Exception exception)
                {
                    return UnknownOutcome(projectPath, retainedRecoveryCopy,
                        "Motif could not compare the saved project with its initialization recovery copy. " + exception.Message);
                }
            }
            return DefinitionRefusal(initialInspection, projectPath, retainedRecoveryCopy);
        }
        if (cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Count == 0)
        {
            cache.Dispose();
            return Refused("initialization.analysis-writing-system-required", FailureReason.Refused,
                "Configure an analysis writing system in FieldWorks before initializing this project.",
                ("fwDataPath", projectPath));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cache.Dispose();
            return CancellationRefusal(projectPath);
        }

        try
        {
            if (File.Exists(recoveryCopyPath))
            {
                if (!ProjectInitializationRecovery.MatchesCurrentProject(projectPath, recoveryCopyPath))
                {
                    cache.Dispose();
                    return UnknownOutcome(projectPath, recoveryCopyPath,
                        "A previous initialization attempt left a recovery copy, and the saved project has changed since it was made.");
                }
            }
            else
            {
                ProjectInitializationRecovery.CreateOriginalCopy(projectPath, recoveryCopyPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            cache.Dispose();
            return Refused("initialization.recovery-copy-failed", FailureReason.Refused,
                "Motif could not make and verify a private recovery copy, so it did not change the project. " +
                exception.Message,
                ("fwDataPath", projectPath));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cache.Dispose();
            var retainedCopy = DeleteVerifiedRecoveryCopy(recoveryCopyPath)
                ? null
                : ExistingRecoveryPath(recoveryCopyPath);
            return CancellationRefusal(projectPath, retainedCopy);
        }

        Exception? writeFailure = null;
        try
        {
            HumanJudgmentFieldDefinition.CreateDefinition(cache);
            loader.Save(cache);
        }
        catch (Exception exception)
        {
            writeFailure = exception;
        }

        try
        {
            cache.Dispose();
        }
        catch (Exception exception)
        {
            writeFailure ??= exception;
        }

        return VerifyAfterAttempt(projectPath, recoveryCopyPath, loader, writeFailure);
    }

    private static CommandOutcome<ProjectInitializationResponse> ProjectLockedRefusal(string projectPath) =>
        Refused(RefusalCodes.ProjectInUse, FailureReason.Busy,
            ProjectStoreCommand.ProjectInUseMessage(projectPath, "initialize the project"),
            ("fwDataPath", projectPath));

    private static CommandOutcome<ProjectInitializationResponse> CancellationRefusal(
        string projectPath, string? recoveryCopyPath = null) =>
        Refused("initialization.cancelled", FailureReason.Cancelled,
            "Project initialization was cancelled before the field definition changed." +
            RestoreStepIfRetained(projectPath, recoveryCopyPath),
            ("fwDataPath", projectPath), ("recoveryCopyPath", recoveryCopyPath));

    private static string RestoreStepIfRetained(string projectPath, string? recoveryCopyPath) =>
        recoveryCopyPath is null
            ? string.Empty
            : " " + ProjectInitializationRecovery.RestoreStep(projectPath, recoveryCopyPath);

    private static CommandOutcome<ProjectInitializationResponse> VerifyAfterAttempt(
        string projectPath,
        string recoveryCopyPath,
        FwDataProjectLoader loader,
        Exception? writeFailure)
    {
        LcmCache verifiedCache;
        try
        {
            verifiedCache = loader.LoadCache(projectPath);
        }
        catch (Exception exception)
        {
            return UnknownOutcome(projectPath, recoveryCopyPath,
                "Motif could not reopen the saved project to establish the initialization result. " +
                (writeFailure?.Message ?? exception.Message));
        }

        HumanJudgmentFieldInspection inspection;
        try
        {
            inspection = HumanJudgmentFieldDefinition.Inspect(verifiedCache);
        }
        catch (Exception exception)
        {
            verifiedCache.Dispose();
            return UnknownOutcome(projectPath, recoveryCopyPath,
                "Motif reopened the project but could not verify its reserved field: " + exception.Message);
        }

        try
        {
            verifiedCache.Dispose();
        }
        catch (Exception exception)
        {
            return UnknownOutcome(projectPath, recoveryCopyPath,
                "Motif could not close the verification cache cleanly: " + exception.Message);
        }

        if (inspection.State == HumanJudgmentFieldState.Compatible)
        {
            var retainedCopy = DeleteVerifiedRecoveryCopy(recoveryCopyPath) ? null : ExistingRecoveryPath(recoveryCopyPath);
            return CommandOutcome<ProjectInitializationResponse>.Success(
                new ProjectInitializationResponse("initialized", HumanJudgmentFieldDefinition.InternalName,
                    retainedCopy));
        }

        if (inspection.State == HumanJudgmentFieldState.Missing)
        {
            try
            {
                if (ProjectInitializationRecovery.MatchesCurrentProject(projectPath, recoveryCopyPath))
                {
                    var retainedCopy = DeleteVerifiedRecoveryCopy(recoveryCopyPath)
                        ? null
                        : ExistingRecoveryPath(recoveryCopyPath);
                    return Refused("initialization.not-saved", FailureReason.Refused,
                        "The project remains unchanged and Motif did not initialize it. Correct the reported save problem and retry." +
                        RestoreStepIfRetained(projectPath, retainedCopy),
                        ("fwDataPath", projectPath), ("saveFailure", writeFailure?.Message),
                        ("recoveryCopyPath", retainedCopy));
                }
            }
            catch (Exception exception)
            {
                return UnknownOutcome(projectPath, recoveryCopyPath,
                    "Motif could not compare the saved project with its recovery copy after the save attempt. " + exception.Message);
            }
        }

        var reason = inspection.State switch
        {
            HumanJudgmentFieldState.Ambiguous => "The saved project contains multiple reserved judgment definitions.",
            HumanJudgmentFieldState.Incompatible => "The saved project contains an incompatible reserved judgment definition.",
            _ => "The saved project differs from its recovery copy, but has no compatible reserved judgment definition.",
        };
        return UnknownOutcome(projectPath, recoveryCopyPath, reason + " " + (writeFailure?.Message ?? string.Empty));
    }

    private static CommandOutcome<ProjectInitializationResponse> DefinitionRefusal(
        HumanJudgmentFieldInspection inspection,
        string projectPath,
        string? recoveryCopyPath = null)
    {
        var definitions = string.Join("; ", inspection.Definitions.Select(definition => definition.Description));
        var recoveryMessage = recoveryCopyPath is null
            ? string.Empty
            : " Keep the retained recovery copy at '" + recoveryCopyPath + "' while resolving the schema. " +
                ProjectInitializationRecovery.RestoreStep(projectPath, recoveryCopyPath);
        if (inspection.State == HumanJudgmentFieldState.Ambiguous)
            return Refused("judgment.field-ambiguous", FailureReason.Refused,
                "The project has multiple definitions named MotifHumanJudgment: " + definitions +
                ". Resolve this schema conflict in FieldWorks before rerunning project initialization." + recoveryMessage,
                ("fwDataPath", projectPath), ("definitions", definitions), ("recoveryCopyPath", recoveryCopyPath));

        return Refused("judgment.field-incompatible", FailureReason.Refused,
            "The reserved field has an incompatible schema: " + definitions +
            ". The required schema is a String on RnGenericRec with the analysis writing-system selector and no destination or list. " +
            "Resolve it deliberately in FieldWorks before rerunning project initialization." + recoveryMessage,
            ("fwDataPath", projectPath), ("definitions", definitions), ("recoveryCopyPath", recoveryCopyPath));
    }

    private static CommandOutcome<ProjectInitializationResponse> UnknownOutcome(
        string projectPath, string recoveryCopyPath, string explanation) =>
        Refused("initialization.outcome-unknown", FailureReason.StoreInconsistent,
            explanation.Trim() + " Do not retry or restore automatically. Inspect the saved project and recovery copy at '" +
            recoveryCopyPath + "' first. " + ProjectInitializationRecovery.RestoreStep(projectPath, recoveryCopyPath),
            ("fwDataPath", projectPath), ("recoveryCopyPath", recoveryCopyPath));

    private static bool DeleteVerifiedRecoveryCopy(string recoveryCopyPath)
    {
        try
        {
            ProjectInitializationRecovery.DeleteVerifiedCopy(recoveryCopyPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ExistingRecoveryPath(string recoveryCopyPath) =>
        File.Exists(recoveryCopyPath) ? recoveryCopyPath : null;

    private static CommandOutcome<ProjectInitializationResponse> Refused(
        string code,
        FailureReason reason,
        string message,
        params (string Key, string? Value)[] facts)
    {
        var factMap = facts.Where(fact => fact.Value is not null)
            .ToDictionary(fact => fact.Key, fact => fact.Value!, StringComparer.Ordinal);
        return CommandOutcome<ProjectInitializationResponse>.Refused(new Refusal(code, reason, message, factMap));
    }
}
