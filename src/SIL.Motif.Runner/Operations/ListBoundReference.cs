using System;
using System.Collections.Generic;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Resolution;
using SIL.Motif.Runner.Execution;

namespace SIL.Motif.Runner.Operations;

/// <summary>The one membership check for a reference that must come from a possibility list.</summary>
internal static class ListBoundReference
{
    /// <summary>Resolves a reference that must be a <typeparamref name="TRef"/> owned by the field's list.</summary>
    public static TRef Resolve<TRef>(LcmCache cache, CanonicalId id, string kind, string field, Guid list)
        where TRef : ICmPossibility
    {
        var resolved = CanonicalIdResolver.Resolve(cache, id);
        if (resolved is not TRef member)
            throw new ReferenceFieldRefusal(RefusalCodes.ReferenceWrongClass, kind, field,
                $"Referenced object '{id.Value}' is a {resolved.ClassName}, not a {typeof(TRef).Name.TrimStart('I')}.");
        RequireInList(member, list, RefusalCodes.ReferenceWrongList, kind, field);
        return member;
    }

    /// <summary>Throws the refusal for a <paramref name="member"/> that <paramref name="list"/> does not own.</summary>
    public static void RequireInList(ICmObject member, Guid list, string code, string kind, string field)
    {
        if (list == Guid.Empty || (member as ICmPossibility)?.OwningList.Guid != list)
            throw new ReferenceFieldRefusal(code, kind, field,
                $"Referenced object '{member.Guid:D}' is not in the list {field} takes.");
    }
}

/// <summary>A reference write the field's list or class does not take, with the field it names.</summary>
internal sealed class ReferenceFieldRefusal(string code, string kind, string field, string reason)
    : ProposalRefusalException(code, FailureReason.Refused, $"'{kind}': {reason}",
        new Dictionary<string, string> { ["kind"] = kind, ["field"] = field });
