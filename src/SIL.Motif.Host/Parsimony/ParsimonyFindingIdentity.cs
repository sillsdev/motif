using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// The one home of a Parsimony Finding's identity: the strings the measure runner mints, the rules that read them
/// back, the table from authored-object and group kinds to LibLCM class names, and the rule that decides whether
/// a saved judgment's subject names a Finding. Stored Reports and Human judgments persist these strings, so every
/// minted form is byte-stable.
/// </summary>
public static class ParsimonyFindingIdentity
{
    private const string AdjacencySegment = "/adjacency-question/";

    private static readonly string[] AdhocClasses = ["MoAlloAdhocProhib", "MoMorphAdhocProhib", "MoAdhocProhibGr"];
    private static readonly string[] AllomorphClasses = ["MoStemAllomorph", "MoAffixAllomorph", "MoAffixProcess"];
    private static readonly string[] NaturalClassClasses = ["PhNCSegments", "PhNCFeatures"];
    private static readonly string[] StatementClasses = ["PhEnvironment", "PhNCSegments", "PhNCFeatures"];

    /// <summary>The group key shared by every duplicate-form group of one entry.</summary>
    public static string EntryGroupKey(string entryGuid) => "entry/" + entryGuid;

    /// <summary>The attachment identity of one duplicate-form group, named by its member GUIDs so form edits keep it.</summary>
    public static string DuplicateFormsIdentity(string entryGuid, string morphType, IEnumerable<string> memberIds) =>
        $"entry/{entryGuid}/duplicate-forms/{morphType}/" + Digest(string.Join("\n", memberIds));

    /// <summary>The Finding ID of an item-attached Finding, one per item and digested site key.</summary>
    public static string SiteFindingId(string measureId, string itemGuid, string siteKey) =>
        $"{measureId}:{itemGuid}:{Digest(siteKey)}";

    /// <summary>The group key of one natural-class usage site.</summary>
    public static string ClassSiteGroupKey(string classGuid, string usageSite) =>
        $"class/{classGuid}/site/{Digest(usageSite)}";

    /// <summary>The group key of every case on one wordform.</summary>
    public static string WordformGroupKey(string wordformGuid) => "wordform/" + wordformGuid;

    /// <summary>The group key of one reviewed negative case.</summary>
    public static string ReviewedNegativeGroupKey(string caseId) => "reviewed-negative/" + caseId;

    /// <summary>The Finding ID of a group-attached Finding, which is the digest of the attachment identity.</summary>
    public static string GroupFindingId(string measureId, string identity) => $"{measureId}:{Digest(identity)}";

    /// <summary>The group key of ad hoc prohibitions that share one normalised signature.</summary>
    public static string AdhocSignatureGroupKey(string signature) => "adhoc-signature/" + Digest(signature);

    /// <summary>The group key of one adjacency-question rule inside a slot-order theme.</summary>
    public static string AdjacencyThemeKey(string themeKey, string ruleGuid) =>
        themeKey + AdjacencySegment + ruleGuid;

    /// <summary>The attachment identity of a slot-order group, built from its group (theme) key.</summary>
    public static string AdhocSlotOrderIdentity(string themeKey) => "adhoc-slot-order/" + themeKey;

    /// <summary>The Finding ID of a slot-order Finding.</summary>
    public static string AdhocSlotOrderFindingId(string measureId, bool adjacencyQuestion, string themeKey) =>
        $"{measureId}:{(adjacencyQuestion ? "adjacency:" : "theme:")}{Digest(themeKey)}";

    /// <summary>The attachment identity of one unused statement.</summary>
    public static string StatementIdentity(string statementKind, string statementGuid) =>
        $"statement/{statementKind}/{statementGuid}";

    /// <summary>The group key shared by every unused statement of one kind.</summary>
    public static string StatementGroupKey(string statementKind) => $"statement/{statementKind}";

    /// <summary>The identity of an alternation family, derived from its exact family key.</summary>
    public static string AlternationFamilyIdentity(string familyKey) => "alternation-family/" + Digest(familyKey);

    /// <summary>Whether a Finding is an adjacency question, which names exactly one prohibition.</summary>
    public static bool IsAdjacencyQuestion(ParsimonyFinding finding) =>
        finding.FindingId.StartsWith(finding.MeasureId + ":adjacency:", StringComparison.Ordinal);

    /// <summary>Whether an adjacency Finding's group key names the given prohibition.</summary>
    public static bool AdjacencyNamesRule(ParsimonyFinding finding, string ruleGuid) =>
        finding.GroupKey?.EndsWith(AdjacencySegment + ruleGuid, StringComparison.Ordinal) == true;

    /// <summary>Whether a duplicate-form Finding's group key is the given entry's.</summary>
    public static bool GroupKeyIsEntry(ParsimonyFinding finding, string entryGuid) =>
        string.Equals(finding.GroupKey, EntryGroupKey(entryGuid), StringComparison.Ordinal);

    /// <summary>
    /// Whether a group Finding's attachment identity agrees with its group key under the identity form its group
    /// kind mints.
    /// </summary>
    public static bool GroupIdentityIsConsistent(ParsimonyFinding finding)
    {
        var key = finding.GroupKey;
        var identity = finding.AttachesTo.Identity;
        return finding.AttachesTo.GroupKind switch
        {
            ParsimonyGroupKind.AllomorphDuplicateForms => key is not null &&
                key.StartsWith("entry/", StringComparison.Ordinal) &&
                identity.StartsWith(key + "/duplicate-forms/", StringComparison.Ordinal),
            ParsimonyGroupKind.AlternationFamily => identity == key,
            ParsimonyGroupKind.AdhocSlotOrder => key is not null && identity == AdhocSlotOrderIdentity(key),
            _ => false,
        };
    }

    /// <summary>The final slash-separated segment of an identity, normalised when it is a GUID or portable ID.</summary>
    public static string LastIdentity(string identity)
    {
        var segment = identity.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? identity;
        return NormalizeIdentity(segment);
    }

    /// <summary>
    /// The GUID segments of a Finding's group key and then attachment identity, each with the kind segment that
    /// precedes it, so a caption can name the first one the facts can describe.
    /// </summary>
    public static IEnumerable<(string? Kind, string Guid)> GuidSegments(ParsimonyFinding finding)
    {
        foreach (var owner in new[] { finding.GroupKey, finding.AttachesTo.Identity }.OfType<string>())
        {
            var parts = owner.Split('/');
            for (var index = 0; index < parts.Length; index++)
                if (Guid.TryParse(parts[index], out _))
                    yield return (index > 0 ? parts[index - 1] : null, parts[index]);
        }
    }

    /// <summary>Whether a LibLCM class may stand for an authored object of the given kind.</summary>
    public static bool ClassMatches(ParsimonyAuthoredObjectKind? kind, string className) => kind switch
    {
        ParsimonyAuthoredObjectKind.AdhocProhibition => AdhocClasses.Contains(className),
        ParsimonyAuthoredObjectKind.InflectionalMsa => className == "MoInflAffMsa",
        ParsimonyAuthoredObjectKind.AffixTemplate => className == "MoInflAffixTemplate",
        ParsimonyAuthoredObjectKind.AffixSlot => className == "MoInflAffixSlot",
        ParsimonyAuthoredObjectKind.Allomorph => AllomorphClasses.Contains(className),
        ParsimonyAuthoredObjectKind.NaturalClass => NaturalClassClasses.Contains(className),
        ParsimonyAuthoredObjectKind.Environment => className == "PhEnvironment",
        _ => false,
    };

    /// <summary>Whether a LibLCM class may be a member of a computed group of the given kind.</summary>
    public static bool GroupMemberClassMatches(ParsimonyGroupKind? kind, string className) => kind switch
    {
        ParsimonyGroupKind.AlternationFamily or ParsimonyGroupKind.AllomorphDuplicateForms =>
            AllomorphClasses.Contains(className),
        ParsimonyGroupKind.AdhocSlotOrder => AdhocClasses.Contains(className),
        ParsimonyGroupKind.UnusedStatement => StatementClasses.Contains(className),
        _ => false,
    };

    /// <summary>Whether two evidence digests are equal, with or without the <c>sha256:</c> prefix.</summary>
    public static bool EvidenceDigestsMatch(string first, string second) =>
        StripDigestPrefix(first) == StripDigestPrefix(second);

    /// <summary>Whether a saved judgment's subject names the Finding, in the project the Report was read from.</summary>
    public static bool SubjectMatches(HumanJudgmentSubject subject, ParsimonyFinding finding, string projectId)
    {
        var attachment = finding.AttachesTo;
        switch (subject)
        {
            case ProjectJudgmentSubject project:
                return attachment.Kind == ParsimonyAttachmentKind.Project &&
                    SameIdentity(project.Id, attachment.Identity) && SameIdentity(project.Id, projectId);
            case ParsimonyFindingJudgmentSubject reportedFinding:
                return attachment.Kind == ParsimonyAttachmentKind.WordCase &&
                    reportedFinding.MeasureId == finding.MeasureId &&
                    reportedFinding.FindingId == finding.FindingId;
            case ObjectJudgmentSubject item:
                if (attachment.Kind == ParsimonyAttachmentKind.AuthoredObject &&
                    ClassMatches(attachment.AuthoredObjectKind, item.Object.Class) &&
                    SameIdentity(item.Object.Id, attachment.Identity)) return true;
                return attachment.Kind == ParsimonyAttachmentKind.Group &&
                    attachment.GroupKind == ParsimonyGroupKind.UnusedStatement &&
                    GroupMemberClassMatches(ParsimonyGroupKind.UnusedStatement, item.Object.Class) &&
                    SameIdentity(item.Object.Id, LastIdentity(attachment.Identity));
            case GroupJudgmentSubject group:
                return GroupSubjectMatches(group, finding);
            default:
                return false;
        }
    }

    private static bool GroupSubjectMatches(GroupJudgmentSubject group, ParsimonyFinding finding)
    {
        var attachment = finding.AttachesTo;
        if (group.MeasureId != finding.MeasureId || attachment.Kind != ParsimonyAttachmentKind.Group ||
            !GroupRoleMatches(group.Role, attachment.GroupKind)) return false;
        var referenced = ReferencedObjectIds(finding, attachment.GroupKind);
        if (referenced.Count == 0) referenced.Add(LastIdentity(attachment.Identity));
        var members = group.Members.Select(member => NormalizeIdentity(member.Id)).Order(StringComparer.Ordinal)
            .ToArray();
        return members.Length == referenced.Count &&
            members.SequenceEqual(referenced.Order(StringComparer.Ordinal)) &&
            group.Members.All(member => GroupMemberClassMatches(attachment.GroupKind, member.Class));
    }

    private static HashSet<string> ReferencedObjectIds(ParsimonyFinding finding, ParsimonyGroupKind? groupKind)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var evidenceView = groupKind switch
        {
            ParsimonyGroupKind.AlternationFamily or ParsimonyGroupKind.AllomorphDuplicateForms =>
                "allomorph-context",
            ParsimonyGroupKind.AdhocSlotOrder => "adhoc-context",
            _ => null,
        };
        foreach (var reference in finding.EvidenceRefs.Where(item => evidenceView is null || item.View == evidenceView))
        {
            if (reference.Arguments.ValueKind != JsonValueKind.Object ||
                !reference.Arguments.TryGetProperty("objectGuid", out var value) ||
                value.ValueKind != JsonValueKind.String || value.GetString() is not { } identity) continue;
            result.Add(NormalizeIdentity(identity));
        }
        return result;
    }

    private static bool GroupRoleMatches(JudgmentGroupRole role, ParsimonyGroupKind? kind) => (role, kind) switch
    {
        (JudgmentGroupRole.AlternationFamily, ParsimonyGroupKind.AlternationFamily) => true,
        (JudgmentGroupRole.DuplicatePair, ParsimonyGroupKind.AllomorphDuplicateForms) => true,
        (JudgmentGroupRole.AdhocSlotOrder, ParsimonyGroupKind.AdhocSlotOrder) => true,
        (JudgmentGroupRole.AdhocCluster, ParsimonyGroupKind.UnusedStatement) => true,
        _ => false,
    };

    private static bool SameIdentity(string first, string second) => NormalizeIdentity(first) == NormalizeIdentity(second);

    private static string NormalizeIdentity(string value)
    {
        if (Guid.TryParse(value, out var guid)) return CanonicalId.FromGuid(guid).Value;
        return CanonicalId.TryParse(value, out var id) ? CanonicalId.FromGuid(id.ToGuid()).Value : value;
    }

    private static string StripDigestPrefix(string value) => value.StartsWith("sha256:", StringComparison.Ordinal)
        ? value[7..] : value;

    private static string Digest(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
