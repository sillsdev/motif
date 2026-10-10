using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Generated construction paths supply typed ownership and factory delegates, never arbitrary field names.
/// Ordered sequences require identity-relative placement when nonempty; unordered pools ignore storage order.
/// All writes belong to the caller's unit of work, including removal of factory defaults.
/// </summary>
internal sealed class OwningCreateHandler<TOwner>(
    string kind, string field, Func<TOwner, ICmObject[]> members,
    Action<LcmCache, TOwner, Guid, int, string?> create,
    bool sequence, bool atomic, string[] concreteClasses, string expectedClass, bool positionMatters = true) : IOperationHandler where TOwner : class, ICmObject
{
    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{kind}' requires entityId.");
        var after = operation.After ?? throw new InvalidOperationException($"'{kind}' requires after.");
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, concreteClasses.Length == 0 ? Array.Empty<string>() :
            new[] { "class" }, kind);
        string? concreteClass = null;
        if (concreteClasses.Length != 0)
        {
            concreteClass = ClosedPayloadParsing.GetRequiredString(after, "class", kind);
            if (!concreteClasses.Contains(concreteClass))
                throw new InvalidOperationException($"'{kind}': class must be {string.Join(" or ", concreteClasses)}.");
        }
        var (id, owner) = TargetResolution.Resolve<TOwner>(cache, operation, kind);
        var existing = members(owner);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (repository.IsValidObjectId(entityId.ToGuid()))
        {
            var existingClass = repository.GetObject(entityId.ToGuid()).ClassName;
            var desiredClass = concreteClass ?? expectedClass;
            if (existingClass != desiredClass)
                throw new InvalidOperationException($"'{kind}': identity '{entityId.Value}' already exists as {existingClass}, " +
                    $"expected {desiredClass}; this is a semantic conflict. Choose a fresh identity.");
            throw new InvalidOperationException($"'{kind}': identity '{entityId.Value}' already exists; " +
                "creation would overwrite/reuse storage. Choose a fresh identity.");
        }
        if (atomic && existing.Length != 0)
            throw new InvalidOperationException($"'{kind}': the owning slot is occupied; creation cannot replace it.");
        if (!sequence && operation.Placement is not null)
            throw new InvalidOperationException($"'{kind}': placement applies only to a sequence.");
        if (sequence && !positionMatters && operation.Placement is not null)
            throw new InvalidOperationException($"'{kind}': placement is not defined for this unordered sequence.");
        var index = sequence
            ? positionMatters ? InsertionIndex(existing, operation.Placement) : existing.Length
            : 0;
        var before = Read(cache, owner, entityId);
        create(cache, owner, entityId.ToGuid(), index, concreteClass);
        touchedTargets.Add(id);
        return new ExpectedEffect(id, field, before, Read(cache, owner, entityId));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{kind}' requires entityId.");
        var (id, owner) = TargetResolution.Resolve<TOwner>(cache, operation, kind);
        var current = Read(cache, owner, entityId);
        return new ExpectedEffect(id, field, current, current);
    }

    private Dictionary<string, string> Read(LcmCache cache, TOwner owner, CanonicalId entityId)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var list = members(owner);
        if (sequence && !positionMatters)
        {
            var unordered = list
                .Select(member => CanonicalId.FromGuid(member.Guid).Value)
                .Order(StringComparer.Ordinal)
                .ToArray();
            for (var i = 0; i < unordered.Length; i++)
                result[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = unordered[i];
        }
        else if (sequence || atomic)
        {
            for (var i = 0; i < list.Length; i++)
                result[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = CanonicalId.FromGuid(list[i].Guid).Value;
        }
        else if (list.Any(m => m.Guid == entityId.ToGuid())) result["ref"] = entityId.Value;
        if (cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(entityId.ToGuid()) &&
            !list.Any(m => m.Guid == entityId.ToGuid())) result["collision"] = entityId.Value;
        return result;
    }

    private int InsertionIndex(ICmObject[] existing, Placement? placement)
    {
        if (placement is null)
        {
            if (existing.Length != 0)
                throw new InvalidOperationException($"'{kind}': a nonempty sequence requires an identity-relative placement.");
            return 0;
        }
        var left = placement.After is { } after ? Array.FindIndex(existing, m => m.Guid == after.ToGuid()) : -1;
        var right = placement.Before is { } before ? Array.FindIndex(existing, m => m.Guid == before.ToGuid()) : existing.Length;
        if ((placement.After is not null && left < 0) || (placement.Before is not null && right < 0) || right != left + 1)
            throw new InvalidOperationException($"'{kind}': placement neighbours are missing or no longer adjacent; recompute a Dry Run.");
        return right;
    }
}
