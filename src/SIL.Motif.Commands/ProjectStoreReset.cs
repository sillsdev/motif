using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>The one field deleting a refused store needs: which project's store to delete.</summary>
public sealed record ProjectStoreResetRequest(string ProjectPath);

/// <summary>
/// The project's Motif store file, and whether it was deleted. <see cref="Deleted"/> is <c>false</c> when the
/// file is absent or this version of Motif can use it, so there was nothing to delete.
/// </summary>
public sealed record ProjectStoreResetResponse(string StorePath, bool Deleted);

/// <summary>
/// Deletes a project's Motif store that this version cannot upgrade, so the next open recreates it. An older
/// store is upgraded on open, not reset, and a newer store is never reset: it is refused with the
/// <c>storeNewer</c> fact, pointing to installing the newer Motif or restoring a pre-upgrade backup, pinned by
/// `ANewerStoreIsNeverResetAndIsLeftByteForByte`. The backup-first reset arrives in a later slice of #466.
/// A store no upgrade step reaches is how a person gets past it, pinned by
/// `ARefusedStoreIsDeletedAloneAndTheNextOpenRecreatesIt`.
/// </summary>
/// <remarks>
/// <para>
/// The file it deletes is the project's own store and nothing else: never the FieldWorks project, never a
/// neighbouring file. It deletes only a store refused as <see cref="RefusalCodes.StoreOtherVersion"/> would be. A
/// store this version can use is kept, pinned by `AStoreThisVersionCanUseIsKept`. Any other refusal comes back
/// under its own code with the file untouched, pinned by `ADamagedStoreIsRefusedAsItsOwnFailureAndLeftByteForByte`.
/// </para>
/// <para>
/// The creation lock orders reset against first creation, and an exclusive use lease protects an incompatible
/// store from deletion while Motif has it open; <see cref="MotifDatabase.DeleteIfOtherVersion"/> details both.
/// A store created while this waited is kept, pinned by `ADatabaseCreatedWhileTheDeleteWaitsIsKept`. A creation
/// lock not freed in time is a busy refusal, pinned by
/// `NothingIsDeletedWhileAnotherOpenerHoldsTheStoresCreationLock`.
/// </para>
/// <para>
/// Every change not yet applied lives in that store and is lost with it; a caller confirms that with the person
/// first. A store another Motif process holds open cannot be deleted and is refused as busy, pinned by
/// `AStoreSomethingElseHoldsOpenIsRefusedAndKept`.
/// </para>
/// </remarks>
public static class ProjectStoreReset
{
    /// <summary>Deletes the project's store if another version of Motif made it.</summary>
    public static CommandOutcome<ProjectStoreResetResponse> DeleteRefused(ProjectStoreResetRequest request) =>
        DeleteRefused(request, MotifProductVersion.CurrentText);

    /// <summary>Deletes the project's store if a Motif at <paramref name="productVersion"/> cannot use it.</summary>
    /// <param name="request">The project whose store to delete.</param>
    /// <param name="productVersion">The Motif version whose compatibility check decides the refusal.</param>
    /// <param name="ownershipPatience">Maximum wait for the store's creation lock; defaults to 30 seconds.</param>
    public static CommandOutcome<ProjectStoreResetResponse> DeleteRefused(
        ProjectStoreResetRequest request, string productVersion, TimeSpan? ownershipPatience = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProjectLocator project;
        try
        {
            project = ProjectStoreCommand.Locate(request.ProjectPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return Refused(exception is FileNotFoundException ? "project.not-found" : "project.invalid",
                FailureReason.InvalidArgument, "Motif could not find or use this project.", request.ProjectPath, null, exception.Message);
        }

        var storePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        var catalog = new ProjectDatabaseCatalog(
            MotifSchema.CurrentSchema, ProjectStoreCommand.ParseVersion(productVersion));
        try
        {
            var deleted = catalog.DeleteIfOtherVersion(project, ownershipPatience);
            return CommandOutcome<ProjectStoreResetResponse>.Success(new ProjectStoreResetResponse(storePath, deleted));
        }
        catch (MotifStoreVersionException exception) when (exception.Newer)
        {
            // A newer store is never reset: install the newer Motif or restore a pre-upgrade backup.
            return CommandOutcome<ProjectStoreResetResponse>.Refused(
                ProjectStoreCommand.VersionRefusal(exception, request.ProjectPath));
        }
        catch (MotifStoreLockException exception)
        {
            var busy = Refused("project.busy", FailureReason.Busy, "The project store is in use.", request.ProjectPath, storePath, exception.Message);
            return CommandOutcome<ProjectStoreResetResponse>.Refused(ProjectStoreCommand.NamingHolders(busy.Refusal!, storePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Refused("project.store-io", FailureReason.Refused,
                $"Motif could not delete its file for this project, '{Path.GetFileName(storePath)}'.",
                request.ProjectPath, storePath, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return Refused("store.inconsistent", FailureReason.StoreInconsistent, "Motif could not read this project store.",
                request.ProjectPath, storePath, exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return Refused("store.unsupported", FailureReason.Refused, "Motif cannot use this project store.", request.ProjectPath,
                storePath, exception.Message);
        }
    }

    private static CommandOutcome<ProjectStoreResetResponse> Refused(
        string code, FailureReason reason, string message, string fwDataPath, string? storePath, string? exception = null)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["fwDataPath"] = fwDataPath };
        if (storePath is not null) facts[RefusalFactNames.StorePath] = storePath;
        if (exception is not null) facts["exceptionDetail"] = exception;
        return CommandOutcome<ProjectStoreResetResponse>.Refused(new Refusal(code, reason, message, facts));
    }
}
