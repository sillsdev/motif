using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.Commands;

/// <summary>The one field deleting a refused store needs: which project's store to delete.</summary>
public sealed record ProjectStoreResetRequest(string ProjectPath);

/// <summary>
/// The project's Motif store file, and whether it was deleted. <see cref="Deleted"/> is <c>false</c> when this
/// version of Motif can use the store, so there was nothing to delete.
/// </summary>
public sealed record ProjectStoreResetResponse(string StorePath, bool Deleted);

/// <summary>
/// Deletes a project's Motif store when it was made by another version of Motif, so the next open recreates it.
/// Before 1.0 a stored shape is never migrated, so this is how a person gets past a store this version cannot use,
/// pinned by `ARefusedStoreIsDeletedAloneAndTheNextOpenRecreatesIt`.
/// </summary>
/// <remarks>
/// <para>
/// It opens the store exactly as every verb does, and deletes only when that open is refused under
/// <see cref="RefusalCodes.StoreOtherVersion"/>. The file it deletes is the one that refusal names, and nothing
/// else: never the FieldWorks project, never a neighbouring file. A store this version can use is kept, and any
/// other refusal is returned unchanged, so the operation cannot throw away a store that was not in the way, pinned by
/// `AStoreThisVersionCanUseIsKept`.
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
    public static CommandOutcome<ProjectStoreResetResponse> DeleteRefused(
        ProjectStoreResetRequest request, string productVersion)
    {
        ArgumentNullException.ThrowIfNull(request);
        var opened = ProjectStoreCommand.Run(request.ProjectPath, productVersion, (database, _) =>
            CommandOutcome<ProjectStoreResetResponse>.Success(new ProjectStoreResetResponse(database.FullPath, false)));
        if (opened.Refusal is not { Code: RefusalCodes.StoreOtherVersion } refusal) return opened;

        var storePath = refusal.Facts[RefusalFactNames.StorePath];
        try
        {
            File.Delete(storePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandOutcome<ProjectStoreResetResponse>.Refused(new Refusal("project.store-io",
                FailureReason.Refused,
                $"Motif could not delete its file for this project, '{storePath}': {exception.Message}",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["fwDataPath"] = request.ProjectPath,
                    [RefusalFactNames.StorePath] = storePath,
                }));
        }
        return CommandOutcome<ProjectStoreResetResponse>.Success(new ProjectStoreResetResponse(storePath, true));
    }
}
