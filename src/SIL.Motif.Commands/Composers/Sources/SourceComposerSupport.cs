using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Scripture;

namespace SIL.Motif.Commands;

/// <summary>A refusal the Source composers raise: a stable code with the facts it was computed from.</summary>
internal sealed class SourceRefusalException(string code, FailureReason reason, string message,
    IReadOnlyDictionary<string, string>? facts = null)
    : ProposalRefusalException(code, reason, message, facts ?? new Dictionary<string, string>())
{
    internal static SourceRefusalException Of(string code, FailureReason reason, string message,
        params (string Name, string Value)[] facts) =>
        new(code, reason, message, facts.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal));
}

/// <summary>
/// What the Source composers read and check. They read the project copy they are given, which already holds the
/// Source edits staged earlier in the Draft, so a Source added a moment ago is found and its id is taken.
/// </summary>
internal static class SourceComposerSupport
{
    /// <summary>The list as it stands in the copy; a list named twice is a refusal.</summary>
    public static IReadOnlyList<SourceListEntry> Read(LcmCache cache)
    {
        try { return SourceList.Read(cache); }
        catch (SourceListAmbiguousException ambiguous)
        {
            throw SourceRefusalException.Of(RefusalCodes.SourceListAmbiguous, FailureReason.Refused, ambiguous.Message);
        }
    }

    /// <summary>The item and entry of the Source named <paramref name="id"/>, compared ignoring case; a missing Source throws a not-found refusal.</summary>
    public static (ICmPossibility Item, SourceListEntry Entry) Find(LcmCache cache, string id)
    {
        var entries = Read(cache);
        var entry = entries.FirstOrDefault(candidate => string.Equals(candidate.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        var item = entry is null ? null : ScriptureSourceLookup.FindItem(cache, entry.Id);
        if (entry is null || item is null)
            throw SourceRefusalException.Of(RefusalCodes.SourceNotFound, FailureReason.NotFound,
                $"The project's Source list has no Source '{id}'. List the Sources to see the ids.", ("id", id));
        return (item, entry);
    }

    /// <summary>The canonical spelling of a versification name, or a refusal that lists the allowed names.</summary>
    public static (string Name, SIL.Scripture.ScrVers Versification) Versification(string name)
    {
        foreach (var known in SourceListFormat.Versifications)
            if (string.Equals(known.Key, name.Trim(), StringComparison.OrdinalIgnoreCase)) return (known.Key, known.Value);
        var allowed = string.Join(", ", SourceListFormat.Versifications.Keys);
        throw SourceRefusalException.Of(RefusalCodes.SourceVersificationUnknown, FailureReason.InvalidArgument,
            $"The versification '{name}' is not known. Use one of: {allowed}.", ("name", name), ("allowed", allowed));
    }

    /// <summary>Throws for a Context that does not parse in <paramref name="versification"/>.</summary>
    public static void RequireContext(string context, SIL.Scripture.ScrVers versification)
    {
        if (!ScriptureContext.TryParse(context, versification, out _, out var error))
            throw SourceRefusalException.Of(RefusalCodes.SourceContextInvalid, FailureReason.InvalidArgument,
                $"The Context is not valid. {error}", ("error", error));
    }

    /// <summary>Throws for an id that is empty, has spaces, or is taken; a removed Source's id is never reused.</summary>
    public static void RequireFreshId(LcmCache cache, string id)
    {
        if (id.Length == 0 || id != id.Trim() || id.Any(char.IsWhiteSpace))
            throw SourceRefusalException.Of(RefusalCodes.SourceIdInvalid, FailureReason.InvalidArgument,
                "A Source id is needed and has no spaces, for example WEB.", ("id", id));
        var same = Read(cache).FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        if (same is null) return;
        if (!string.Equals(same.Id, id, StringComparison.Ordinal))
            throw SourceRefusalException.Of(RefusalCodes.SourceIdInvalid, FailureReason.InvalidArgument,
                $"The id '{id}' differs from the Source '{same.Id}' only by case. Choose another id.", ("id", id));
        if (same.Status == SourceStatus.Removed)
            throw SourceRefusalException.Of(RefusalCodes.SourceIdRemoved, FailureReason.Refused,
                $"The Source '{id}' was removed, and an id is never reused. Restore it instead.", ("id", id));
        throw SourceRefusalException.Of(RefusalCodes.SourceIdInUse, FailureReason.Refused,
            $"The Source id '{id}' is already in use.", ("id", id));
    }

    /// <summary>One operation of the Source group against an item.</summary>
    public static OperationEnvelope Set(string kind, ICmPossibility item, ComposeContext context, string property, string value) =>
        new(context.MintId(), kind, target: CanonicalId.FromGuid(item.Guid),
            after: System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, string> { [property] = value }));
}
