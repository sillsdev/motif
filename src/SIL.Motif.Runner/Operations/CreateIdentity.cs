using System;
using System.Collections.Generic;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Operations;

/// <summary>Identity checks shared by the hand-written owning creates.</summary>
internal static class CreateIdentity
{
    /// <summary>
    /// Throws for the zero identity and for any identity already in use; the message separates reuse by the
    /// expected class from a conflict with another class.
    /// </summary>
    public static void RequireFresh(LcmCache cache, CanonicalId entityId, string kind, string expectedClass)
    {
        var guid = entityId.ToGuid();
        if (guid == Guid.Empty)
            throw new InvalidOperationException($"'{kind}': the zero identity cannot be authored.");
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (!repository.IsValidObjectId(guid)) return;
        var existingClass = repository.GetObject(guid).ClassName;
        if (existingClass != expectedClass)
            throw new InvalidOperationException($"'{kind}': identity '{entityId.Value}' already exists as {existingClass}, " +
                $"expected {expectedClass}; this is a semantic conflict. Choose a fresh identity.");
        throw new InvalidOperationException($"'{kind}': identity '{entityId.Value}' already exists; " +
            "creation would overwrite/reuse storage. Choose a fresh identity.");
    }

    /// <summary>Records an identity that already names an object outside the slot, so drift sees the collision.</summary>
    public static void AddCollision(LcmCache cache, CanonicalId entityId, Guid? occupant, Dictionary<string, string> current)
    {
        var guid = entityId.ToGuid();
        if (guid == occupant) return;
        if (cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(guid))
            current["collision"] = entityId.Value;
    }
}
