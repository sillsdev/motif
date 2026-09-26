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
/// Deletes a project's Motif store when another version of Motif made it, so the next open recreates it. A
/// stored shape from another version is refused rather than migrated, so this is how a person gets past it,
/// pinned by `ARefusedStoreIsDeletedAloneAndTheNextOpenRecreatesIt`.
/// </summary>
/// <remarks>
/// <para>
/// The file it deletes is the project's own store and nothing else: never the FieldWorks project, never a
/// neighbouring file. It deletes only a store refused as <see cref="RefusalCodes.StoreOtherVersion"/> would be. A
/// store this version can use is kept, pinned by `AStoreThisVersionCanUseIsKept`. Any other refusal comes back
/// under its own code with the file untouched, pinned by `ADamagedStoreIsRefusedAsItsOwnFailureAndLeftByteForByte`.
/// </para>
/// <para>
/// The decision and the deletion are made under the store's creation lock, so an opener cannot create or replace
/// the store between the two. A store recreated while this waited is kept, pinned by
/// `AStoreRecreatedWhileTheDeleteWaitsIsKept`. A lock not freed in time is a busy refusal, pinned by
/// `NothingIsDeletedWhileAnotherOpenerHoldsTheStoresCreationLock`.
/// </para>
/// <para>
/// Every change not yet applied lives in that store and is lost with it; a caller confirms that with the person
/// first. A store another process holds open cannot be deleted, and is refused as a store input/output failure,
/// pinned by `AStoreSomethingElseHoldsOpenIsRefusedAndKept`.
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
                FailureReason.InvalidArgument, exception.Message, request.ProjectPath, null);
        }

        var storePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        var catalog = new ProjectDatabaseCatalog(
            MotifSchema.CurrentSchema, ProjectStoreCommand.ParseVersion(productVersion));
        try
        {
            var deleted = catalog.DeleteIfOtherVersion(project, ownershipPatience);
            return CommandOutcome<ProjectStoreResetResponse>.Success(new ProjectStoreResetResponse(storePath, deleted));
        }
        catch (MotifStoreLockException exception)
        {
            return Refused("project.busy", FailureReason.Busy, exception.Message, request.ProjectPath, storePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Refused("project.store-io", FailureReason.Refused,
                $"Motif could not delete its file for this project, '{storePath}': {exception.Message}",
                request.ProjectPath, storePath);
        }
        catch (InvalidDataException exception)
        {
            return Refused("store.inconsistent", FailureReason.StoreInconsistent, exception.Message,
                request.ProjectPath, storePath);
        }
        catch (NotSupportedException exception)
        {
            return Refused("store.unsupported", FailureReason.Refused, exception.Message, request.ProjectPath,
                storePath);
        }
    }

    private static CommandOutcome<ProjectStoreResetResponse> Refused(
        string code, FailureReason reason, string message, string fwDataPath, string? storePath)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["fwDataPath"] = fwDataPath };
        if (storePath is not null) facts[RefusalFactNames.StorePath] = storePath;
        return CommandOutcome<ProjectStoreResetResponse>.Refused(new Refusal(code, reason, message, facts));
    }
}
