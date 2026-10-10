using System;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Help;
using SIL.Motif.Projection;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Derives the closed judgment subject and captions from one stored Parsimony finding.</summary>
internal static class ParsimonyFindingDispositionComposer
{
    private static readonly Lazy<ParsimonyRecipeCatalog> Recipes = new(ParsimonyRecipeCatalog.Load);

    internal static RecordParsimonyDispositionIntent Create(
        LcmCache cache,
        ParsimonyReportResponse report,
        ParsimonyFinding finding,
        CanonicalId recordTypeId,
        string dispositionText,
        string? reason,
        string? question)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(finding);

        if (!Guid.TryParse(report.Inputs.BaselineToken.ProjectIdentity, out var projectGuid) ||
            projectGuid != cache.LangProject.Guid)
            throw new InvalidOperationException("The Parsimony Report belongs to a different FieldWorks project.");

        var disposition = dispositionText switch
        {
            "fix" => ParsimonyDispositionKind.Fix,
            "keep" => ParsimonyDispositionKind.Keep,
            "ask" => ParsimonyDispositionKind.Ask,
            "defer" => ParsimonyDispositionKind.Defer,
            _ => throw new ArgumentException("Disposition must be fix, keep, ask, or defer.", nameof(dispositionText)),
        };
        if ((disposition == ParsimonyDispositionKind.Ask) != (question is not null))
            throw new ArgumentException("Supply --question only with --disposition ask.", nameof(question));
        if (question is not null && string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("An ask disposition needs a nonblank question.", nameof(question));

        var measure = MeasureCatalog.Find(finding.MeasureId)
            ?? throw new InvalidOperationException($"Unknown Parsimony measure '{finding.MeasureId}'.");
        if (measure.QueryId is null || measure.AttachesTo != finding.AttachesTo.Kind ||
            measure.GroupKind != finding.AttachesTo.GroupKind ||
            measure.AuthoredObjectKind != finding.AttachesTo.AuthoredObjectKind)
            throw new InvalidOperationException("The finding does not match its registered Parsimony measure contract.");
        var recipe = Recipes.Value.Find(finding.MeasureId)
            ?? throw new InvalidOperationException($"Parsimony measure '{finding.MeasureId}' has no Help caption.");

        var (subject, subjectCaption) = ResolveSubject(cache, report, finding, measure);
        return new RecordParsimonyDispositionIntent(
            recordTypeId,
            finding.MeasureId,
            subject,
            disposition,
            CanonicalEvidenceDigest(finding.EvidenceDigest),
            measure.QueryId,
            subjectCaption,
            recipe.Title,
            reason,
            question,
            CanonicalId.Parse(report.ReportId));
    }

    private static (HumanJudgmentSubject Subject, string Caption) ResolveSubject(
        LcmCache cache,
        ParsimonyReportResponse report,
        ParsimonyFinding finding,
        MeasureDefinition measure)
    {
        switch (finding.AttachesTo.Kind)
        {
            case ParsimonyAttachmentKind.Project:
            {
                var id = CanonicalId.FromGuid(cache.LangProject.Guid);
                if (!MatchesId(finding.AttachesTo.Identity, cache.LangProject.Guid))
                    throw new InvalidOperationException("The project finding does not name this FieldWorks project.");
                return (new ProjectJudgmentSubject(id.Value), "the FieldWorks project");
            }
            case ParsimonyAttachmentKind.AuthoredObject:
            {
                var item = ResolveObject(cache, finding.AttachesTo.Identity);
                if (!MatchesAuthoredKind(item, measure.AuthoredObjectKind))
                    throw new InvalidOperationException("The finding's object does not match its registered type.");
                var subject = new ObjectJudgmentSubject(ToJudgmentObject(item));
                return (subject, Caption(cache, item));
            }
            case ParsimonyAttachmentKind.WordCase:
                return (new ParsimonyFindingJudgmentSubject(finding.MeasureId, finding.FindingId),
                    WordCaseCaption(finding));
            case ParsimonyAttachmentKind.Group:
                return ResolveGroup(cache, report, finding);
            default:
                throw new InvalidOperationException(
                    $"Parsimony findings attached to '{finding.AttachesTo.Kind}' cannot be recorded as a Notebook disposition.");
        }
    }

    private static (HumanJudgmentSubject Subject, string Caption) ResolveGroup(
        LcmCache cache,
        ParsimonyReportResponse report,
        ParsimonyFinding finding)
    {
        if (finding.AttachesTo.GroupKind is not { } groupKind)
            throw new InvalidOperationException("The finding does not name a supported computed group.");

        var (expectedMeasure, evidenceView, role) = (finding.MeasureId, groupKind) switch
        {
            ("P-allo-duplicate-form", ParsimonyGroupKind.AllomorphDuplicateForms) =>
                ("P-allo-duplicate-form", "allomorph-context", JudgmentGroupRole.DuplicatePair),
            ("P-allo-alternation-family", ParsimonyGroupKind.AlternationFamily) =>
                ("P-allo-alternation-family", "allomorph-context", JudgmentGroupRole.AlternationFamily),
            ("B-adhoc-is-slot-order", ParsimonyGroupKind.AdhocSlotOrder) =>
                ("B-adhoc-is-slot-order", "adhoc-context", JudgmentGroupRole.AdhocSlotOrder),
            _ => throw new InvalidOperationException("This computed group has no closed Notebook subject mapping."),
        };
        if (finding.MeasureId != expectedMeasure || string.IsNullOrWhiteSpace(finding.GroupKey))
            throw new InvalidOperationException("The computed group has an incomplete measure or member identity.");
        var identityMatches = groupKind switch
        {
            ParsimonyGroupKind.AllomorphDuplicateForms =>
                finding.GroupKey.StartsWith("entry/", StringComparison.Ordinal) &&
                finding.AttachesTo.Identity.StartsWith(finding.GroupKey + "/duplicate-forms/", StringComparison.Ordinal),
            ParsimonyGroupKind.AlternationFamily => finding.AttachesTo.Identity == finding.GroupKey,
            ParsimonyGroupKind.AdhocSlotOrder =>
                finding.AttachesTo.Identity == "adhoc-slot-order/" + finding.GroupKey,
            _ => false,
        };
        if (!identityMatches)
            throw new InvalidOperationException("The computed group identity does not match its exact member key.");

        var references = finding.EvidenceRefs.Where(reference => reference.View == evidenceView).ToArray();
        if (groupKind == ParsimonyGroupKind.AdhocSlotOrder &&
            finding.FindingId.StartsWith(expectedMeasure + ":adjacency:", StringComparison.Ordinal))
        {
            var reference = references.Length == 1 ? references[0] :
                throw new InvalidOperationException("The adjacency finding must name its one exact prohibition.");
            if (reference.BundleId != report.Inputs.BundleId ||
                reference.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !reference.Arguments.TryGetProperty("objectGuid", out var identity) ||
                identity.ValueKind != System.Text.Json.JsonValueKind.String)
                throw new InvalidOperationException("The adjacency finding does not carry its exact prohibition identity.");
            var prohibition = ResolveObject(cache, identity.GetString()!);
            if (!MatchesAuthoredKind(prohibition, ParsimonyAuthoredObjectKind.AdhocProhibition) ||
                !finding.GroupKey.EndsWith("/adjacency-question/" + prohibition.Guid, StringComparison.Ordinal))
                throw new InvalidOperationException("The adjacency finding does not match its exact prohibition.");
            return (new GroupJudgmentSubject(finding.MeasureId, role, [ToJudgmentObject(prohibition)]),
                "ad hoc prohibition " + Caption(cache, prohibition));
        }
        if (references.Length is < 2 or > 256)
            throw new InvalidOperationException("The finding does not carry its exact computed-group members.");
        var members = references.Select(reference =>
        {
            if (reference.BundleId != report.Inputs.BundleId ||
                reference.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !reference.Arguments.TryGetProperty("objectGuid", out var guidValue) ||
                guidValue.ValueKind != System.Text.Json.JsonValueKind.String)
                throw new InvalidOperationException("The finding does not carry its exact computed-group member identities.");

            var item = ResolveObject(cache, guidValue.GetString()!);
            var supportedMember = groupKind switch
            {
                ParsimonyGroupKind.AllomorphDuplicateForms or ParsimonyGroupKind.AlternationFamily =>
                    item is IMoForm && item.Owner is ILexEntry,
                ParsimonyGroupKind.AdhocSlotOrder =>
                    MatchesAuthoredKind(item, ParsimonyAuthoredObjectKind.AdhocProhibition),
                _ => false,
            };
            if (!supportedMember)
                throw new InvalidOperationException("A computed-group member has an unsupported model type or owner.");
            return item;
        }).ToArray();
        if (members.Select(item => item.Guid).Distinct().Count() != members.Length)
            throw new InvalidOperationException("The computed-group finding repeats a member identity.");

        if (groupKind == ParsimonyGroupKind.AllomorphDuplicateForms)
        {
            var entry = (ILexEntry)members[0].Owner!;
            if (members.Any(item => item.Owner is not ILexEntry owner || owner.Guid != entry.Guid) ||
                !string.Equals(finding.GroupKey, "entry/" + entry.Guid.ToString("D"), StringComparison.Ordinal))
                throw new InvalidOperationException("The duplicate-allomorph members do not match the finding's entry group.");
        }
        if (groupKind == ParsimonyGroupKind.AlternationFamily &&
            members.Select(item => item.Owner!.Guid).Distinct().Count() < 2)
            throw new InvalidOperationException("An alternation family must name allomorphs from distinct entries.");

        var judgmentMembers = members.Select(ToJudgmentObject).ToArray();
        var subject = new GroupJudgmentSubject(finding.MeasureId, role, judgmentMembers);
        var caption = groupKind switch
        {
            ParsimonyGroupKind.AllomorphDuplicateForms => "allomorphs on " +
                Caption(cache, (ILexEntry)members[0].Owner!) + ": " +
                string.Join(", ", members.Select(item => Caption(cache, item))),
            ParsimonyGroupKind.AlternationFamily => "allomorphs in the alternation family: " +
                string.Join(", ", members.Select(item => Caption(cache, item))),
            _ => "ad hoc prohibitions in slot-order theme " + finding.GroupKey,
        };
        return (subject, caption);
    }

    private static string WordCaseCaption(ParsimonyFinding finding)
    {
        var identity = finding.AttachesTo.Identity.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? finding.AttachesTo.Identity;
        var suffix = CanonicalId.TryParse(identity, out var canonical) ? canonical.Suffix : identity;
        var display = suffix.Length > 8 ? suffix[..8] : suffix;
        return "word case " + display;
    }

    private static ICmObject ResolveObject(LcmCache cache, string identity)
    {
        var guid = Guid.TryParse(identity, out var parsed) ? parsed :
            CanonicalId.TryParse(identity, out var canonical) ? canonical.ToGuid() : Guid.Empty;
        if (guid == Guid.Empty ||
            !cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(guid, out var item))
            throw new InvalidOperationException($"The finding's project object '{identity}' is unavailable.");
        return item;
    }

    private static JudgmentObject ToJudgmentObject(ICmObject item)
    {
        string? owningEntryId = null;
        if (item.ClassName.EndsWith("Msa", StringComparison.Ordinal))
        {
            for (var owner = item.Owner; owner is not null; owner = owner.Owner)
            {
                if (owner is not ILexEntry entry) continue;
                owningEntryId = CanonicalId.FromGuid(entry.Guid).Value;
                break;
            }
            if (owningEntryId is null)
                throw new InvalidOperationException("The finding's MSA has no owning entry.");
        }
        return new JudgmentObject(item.ClassName, CanonicalId.FromGuid(item.Guid).Value, owningEntryId);
    }

    private static string Caption(LcmCache cache, ICmObject item)
    {
        var caption = item switch
        {
            IMoForm form => WritingSystemTextReader.First(cache, form.Form).Text,
            ILexEntry entry => entry.HeadWord?.Text ?? string.Empty,
            ILexSense sense => sense.Gloss.BestAnalysisAlternative?.Text ?? string.Empty,
            ICmPossibility possibility => WritingSystemTextReader.BestAnalysis(cache, possibility.Name).Text,
            _ => string.Empty,
        };
        return string.IsNullOrWhiteSpace(caption) || caption == "***"
            ? $"{item.ClassName} {CanonicalId.FromGuid(item.Guid).Suffix[..8]}"
            : caption;
    }

    private static bool MatchesAuthoredKind(ICmObject item, ParsimonyAuthoredObjectKind? kind) => kind switch
    {
        ParsimonyAuthoredObjectKind.AdhocProhibition => item.ClassName is "MoAlloAdhocProhib" or "MoMorphAdhocProhib" or "MoAdhocProhibGr",
        ParsimonyAuthoredObjectKind.InflectionalMsa => item.ClassName == "MoInflAffMsa",
        ParsimonyAuthoredObjectKind.AffixTemplate => item.ClassName == "MoInflAffixTemplate",
        ParsimonyAuthoredObjectKind.AffixSlot => item.ClassName == "MoInflAffixSlot",
        ParsimonyAuthoredObjectKind.Allomorph => item is IMoForm,
        ParsimonyAuthoredObjectKind.NaturalClass => item.ClassName is "PhNCSegments" or "PhNCFeatures",
        ParsimonyAuthoredObjectKind.Environment => item.ClassName == "PhEnvironment",
        _ => false,
    };

    private static bool MatchesId(string identity, Guid expected) =>
        Guid.TryParse(identity, out var guid) ? guid == expected :
            CanonicalId.TryParse(identity, out var canonical) && canonical.ToGuid() == expected;

    private static string CanonicalEvidenceDigest(string digest)
    {
        var canonical = digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest : "sha256:" + digest;
        Sha256Value.RequireCanonical(canonical, nameof(digest));
        return canonical;
    }
}
