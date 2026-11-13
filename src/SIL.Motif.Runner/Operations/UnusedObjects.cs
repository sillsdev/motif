using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// What still refers to a grammar or text object, for the delete composers and their handlers, so neither cascades.
/// Every refusal names each referrer with its class, id and the entry or rule that holds it.
/// </summary>
public static class UnusedObjects
{
    /// <summary>The objects that refer to <paramref name="value"/>, never itself nor anything it owns.</summary>
    public static IReadOnlyList<ICmObject> Referrers(ICmObject value) =>
        value.ReferringObjects.Where(user => !ReferenceEquals(user, value) && !IsOwnedBy(user, value))
            .OrderBy(user => user.ClassName, StringComparer.Ordinal).ThenBy(user => user.Guid).ToArray();

    /// <summary>Names an object as a class, an id and the entry or rule it belongs to.</summary>
    public static string Describe(ICmObject value)
    {
        var where = string.Empty;
        for (var current = value; current is not null; current = current.Owner)
        {
            if (current is ILexEntry entry) { where = $" of entry '{entry.HeadWord.Text}'"; break; }
            if (!ReferenceEquals(current, value) && current is IPhRegularRule or IPhMetathesisRule or IMoCompoundRule or IPhEnvironment)
            {
                where = $" of {current.ClassName} {current.Guid:D}";
                break;
            }
        }
        return $"a {value.ClassName} ({value.Guid:D}){where}";
    }

    private static void RequireNone(IReadOnlyList<ICmObject> users, string subject, string kind, string remedy)
    {
        if (users.Count == 0) return;
        throw new InvalidOperationException(
            $"'{kind}': {subject} is still used by {users.Count} object{(users.Count == 1 ? string.Empty : "s")}: " +
            string.Join("; ", users.Take(10).Select(Describe)) + (users.Count > 10 ? $"; and {users.Count - 10} more" : string.Empty) +
            $". {remedy}");
    }

    /// <summary>Requires an Exception Features item with no sub-items and no referrer.</summary>
    public static void RequireUnusedExceptionFeature(LcmCache cache, ICmPossibility feature, CanonicalId id, string kind)
    {
        var list = cache.LangProject.MorphologicalDataOA?.ProdRestrictOA;
        if (list is null || feature.OwningList?.Guid != list.Guid)
            throw new InvalidOperationException($"'{kind}': '{id.Value}' is not an item of the Exception Features list.");
        if (feature.SubPossibilitiesOS.Count > 0)
            throw new InvalidOperationException(
                $"'{kind}': exception feature '{id.Value}' has {feature.SubPossibilitiesOS.Count} sub-item(s) " +
                $"({string.Join(", ", feature.SubPossibilitiesOS.Take(5).Select(sub => sub.Guid.ToString("D")))}); delete those first.");
        RequireNone(Referrers(feature), $"exception feature '{id.Value}'", kind,
            "Remove the feature from each with compose-edit-exception-features first.");
    }

    /// <summary>Requires an environment of the phonological data that nothing refers to.</summary>
    public static void RequireUnusedEnvironment(IPhEnvironment environment, CanonicalId id, string kind)
    {
        if (environment.Owner is not IPhPhonData)
            throw new InvalidOperationException($"'{kind}': '{id.Value}' is not an environment of the phonological data.");
        RequireNone(Referrers(environment), $"environment '{id.Value}'", kind,
            "Take the environment off each of them first.");
    }

    /// <summary>The natural-class names an environment string writes between square brackets.</summary>
    public static IEnumerable<string> BracketedNames(string? representation) =>
        string.IsNullOrEmpty(representation) ? [] :
            Regex.Matches(representation, @"\[([^\[\]]+)\]").Select(match => match.Groups[1].Value.Trim());

    private static IEnumerable<string> Labels(IPhNaturalClass naturalClass)
    {
        foreach (var field in new IMultiAccessorBase[] { naturalClass.Name, naturalClass.Abbreviation })
            foreach (var ws in field.AvailableWritingSystemIds)
                if (field.get_String(ws).Text is { Length: > 0 } text) yield return text.Trim();
    }

    /// <summary>Requires a natural class no rule, class or environment (object or string) uses.</summary>
    public static void RequireUnusedNaturalClass(LcmCache cache, IPhNaturalClass naturalClass, CanonicalId id, string kind)
    {
        if (naturalClass.Owner is not IPhPhonData data)
            throw new InvalidOperationException($"'{kind}': '{id.Value}' is not a natural class of the phonological data.");
        var users = Referrers(naturalClass).ToList();
        var labels = Labels(naturalClass).ToHashSet(StringComparer.Ordinal);
        foreach (var environment in data.EnvironmentsOS)
            if (!users.Contains(environment) &&
                BracketedNames(environment.StringRepresentation?.Text).Any(labels.Contains))
                users.Add(environment);
        RequireNone(users, $"natural class '{id.Value}'", kind,
            "Remove the class from each (environments by editing their string) or relink with compose-relink-natural-class first.");
    }

    /// <summary>Requires a Note that a Segment owns.</summary>
    public static void RequireSegmentNote(INote note, CanonicalId id, string kind)
    {
        if (note.Owner is not ISegment)
            throw new InvalidOperationException($"'{kind}': '{id.Value}' is not a Note of a Text Segment.");
    }

    private static bool IsOwnedBy(ICmObject value, ICmObject owner)
    {
        for (var current = value.Owner; current is not null; current = current.Owner)
            if (current.Guid == owner.Guid) return true;
        return false;
    }
}
