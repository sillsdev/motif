using System;
using System.Collections.Generic;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// The one LibLCM write every <c>rel/col</c>/<c>rel/seq</c> <c>addRef</c>/<c>removeRef</c> kind
/// performs — shared by every generated <c>*Lowering</c> class for a reference collection field
/// (<c>LexEntry.DialectLabels</c>, <c>.DoNotPublishIn</c>, <c>.DoNotShowMainEntryIn</c>
/// today). LibLCM's own <c>ILcmReferenceCollection&lt;T&gt;</c> and <c>ILcmReferenceSequence&lt;T&gt;</c>
/// both implement the ordinary <see cref="ICollection{T}"/> (<c>Add</c>/<c>Remove</c>/<c>Contains</c>),
/// verified by reflection against the pinned <c>SIL.LCModel</c> package, so one generic helper covers
/// both cardinalities — only the accessor expression a generated field's lowering class passes in
/// differs (<c>entry.DialectLabelsRS</c> vs <c>entry.DoNotPublishInRC</c>).
/// </summary>
/// <remarks>
/// Both operations are idempotent by design: <c>addRef</c> of an already-present member and
/// <c>removeRef</c> of an absent one are no-ops rather than errors, matching "ensure this member is/
/// isn't present" authored intent rather than raw collection-mutation semantics.
/// </remarks>
internal static class ReferenceCollectionFieldLowering
{
    public static void ApplyAddRef<TRef>(LcmCache cache, ICollection<TRef> collection, CanonicalId memberId, string kind)
        where TRef : ICmObject
    {
        var member = ReferenceFieldLowering.Resolve<TRef>(cache, memberId, kind);
        if (!collection.Contains(member))
            collection.Add(member);
    }

    /// <summary>Adds a member of the one possibility list FieldWorks offers for the field; any other object is refused by name.</summary>
    public static void ApplyAddRefInList<TRef>(LcmCache cache, ICollection<TRef> collection, CanonicalId memberId,
        string kind, string field, Func<LcmCache, ICmPossibilityList?> list)
        where TRef : ICmPossibility
    {
        var member = ReferenceFieldLowering.Resolve<TRef>(cache, memberId, kind);
        if (member.OwningList.Guid != list(cache)?.Guid)
            throw new ReferenceFieldRefusal(RefusalCodes.ReferenceWrongList, kind, field,
                $"Referenced object '{memberId.Value}' is not in the list {field} takes.");
        if (!collection.Contains(member))
            collection.Add(member);
    }

    public static void ApplyRemoveRef<TRef>(LcmCache cache, ICollection<TRef> collection, CanonicalId memberId, string kind)
        where TRef : ICmObject
    {
        var member = ReferenceFieldLowering.Resolve<TRef>(cache, memberId, kind);
        collection.Remove(member);
    }
}
