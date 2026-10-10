using System;
using System.Linq;
using System.Text;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Contract.HumanJudgments;

internal static class HumanJudgmentValidation
{
    private static readonly string[] Classes =
    [
        "LangProject", "LexEntry", "LexSense", "WfiWordform", "WfiAnalysis", "MoStemAllomorph",
        "MoAffixAllomorph", "MoAffixProcess", "MoStemMsa", "MoInflAffMsa", "MoDerivAffMsa",
        "MoDerivStepMsa", "MoUnclassifiedAffixMsa", "MoInflAffixSlot", "MoInflAffixTemplate",
        "PhEnvironment", "PhNCSegments", "PhNCFeatures", "PhRegularRule", "PhMetathesisRule",
        "MoAlloAdhocProhib", "MoMorphAdhocProhib", "MoAdhocProhibGr", "PartOfSpeech", "MoInflClass",
        "MoStemName", "FsClosedFeature", "FsComplexFeature", "FsOpenFeature", "FsSymFeatVal",
        "FsFeatStruc", "PhPhoneme", "PhBdryMarker"
    ];

    internal static void Validate(HumanJudgment value)
    {
        Require(value.Format == "motif-human-judgment" && value.Version == 1,
            "Unsupported judgment format/version. Deliberately delete only Motif-owned demo records and recreate them.");
        Require(value.ReadableFormat == "en-v1", "Unsupported readable-format version.");
        Id(value.ProjectId); Id(value.JudgmentId); Id(value.RevisionId);
        Require(value.Replaces is not null && value.Replaces.Count <= 256, "Invalid predecessor list.");
        var heads = value.Replaces!;
        foreach (var head in heads)
        {
            Require(head is not null, "Null predecessor.");
            Id(head!.RevisionId);
            Require(!SameId(head.RevisionId, value.RevisionId), "A revision cannot replace itself.");
            Digest(head.ContentDigest);
        }
        Require(heads.Select(h => Identity(h.RevisionId)).Distinct().Count() == heads.Count,
            "Duplicate predecessor identity.");
        Require(value.ResolvesConflict ? heads.Count >= 2 : heads.Count <= 1,
            "Multiple heads require explicit conflict resolution; resolution requires at least two heads.");
        OptionalText(value.Reason, 4096);
        if (value.Actor is { } actor)
        {
            Require(Enum.IsDefined(actor.Kind), "Unknown actor kind.");
            OptionalText(actor.Id, 1024); OptionalText(actor.Name, 1024);
        }
        Require(value.JudgedAtUtc is null || value.JudgedAtUtc.Value.Offset == TimeSpan.Zero,
            "Judgment time must be explicitly UTC.");
        if (value.Source is { } source)
        {
            if (source.ReportId is not null) Id(source.ReportId);
            if (source.ProposalId is not null) Id(source.ProposalId);
        }
        switch (value.Body)
        {
            case DispositionJudgment disposition:
                Subject(disposition.Subject, value.ProjectId);
                Text(disposition.MeasureId, 256); Text(disposition.EvidenceContract, 1024);
                Text(disposition.SubjectCaption, 2048); Text(disposition.MeasureCaption, 2048);
                Require(Enum.IsDefined(disposition.Disposition), "Unknown disposition.");
                Digest(disposition.EvidenceDigest);
                if (disposition.Disposition == ParsimonyDispositionKind.Ask) Text(disposition.Question, 4096);
                else Require(disposition.Question is null, "Only ask carries a pending question.");
                if (disposition.Subject is GroupJudgmentSubject group)
                    Require(group.MeasureId == disposition.MeasureId, "Group and disposition measures differ.");
                if (disposition.Subject is ParsimonyFindingJudgmentSubject finding)
                    Require(finding.MeasureId == disposition.MeasureId,
                        "Finding and disposition measures differ.");
                break;
            case ReviewedNegativeJudgment negative:
                Require(value.Actor?.Kind == JudgmentActorKind.Human,
                    "A reviewed negative requires explicit human confirmation provenance.");
                Id(negative.CaseId); Text(negative.WritingSystem, 128); Text(negative.Form, 4096);
                Text(negative.Context, 4096);
                if (negative.WordformId is not null) Id(negative.WordformId);
                if (negative.AnalysisId is not null) Id(negative.AnalysisId);
                Require(negative.AnalysisId is null || negative.WordformId is not null,
                    "A stored analysis identity requires its wordform identity.");
                switch (negative.Target)
                {
                    case SurfaceNegativeTarget:
                        Require(negative.AnalysisId is null, "A surface negative cannot name an analysis.");
                        break;
                    case ReadingNegativeTarget reading:
                        Require(reading.Morphs is { Count: > 0 and <= 512 }, "Invalid reading morph count.");
                        foreach (var morph in reading.Morphs!)
                        {
                            Require(morph?.Identity is not null, "Missing morphology identity.");
                            var identity = morph!.Identity;
                            Id(identity.Msa);
                            if (identity.InflType is not null) Id(identity.InflType);
                            if (identity.Form is not null) Id(identity.Form);
                            if (identity.GuessedString is not null)
                            {
                                Text(identity.GuessedString, 4096); Text(morph.GuessedWritingSystem, 128);
                            }
                            else
                            {
                                Require(identity.Form is not null, "An ordinary morph needs an authoritative Form identity.");
                                Require(morph.GuessedWritingSystem is null, "Guessed WS requires guessed text.");
                            }
                            Text(morph.FormCaption, 2048); Text(morph.MsaCaption, 2048);
                        }
                        break;
                    default: throw new FormatException("Unknown negative target.");
                }
                break;
            case RetractionJudgment:
                Require(heads.Count > 0, "Retraction must name the heads it withdraws.");
                break;
            default: throw new FormatException("Unknown judgment kind.");
        }
    }

    internal static void Subject(HumanJudgmentSubject subject, string? projectId = null)
    {
        switch (subject)
        {
            case ObjectJudgmentSubject item: Object(item.Object); break;
            case ProjectJudgmentSubject project:
                Id(project.Id);
                Require(projectId is null || SameId(project.Id, projectId), "Project subject differs from containing project.");
                break;
            case EdgeJudgmentSubject edge:
                Require(Enum.IsDefined(edge.Role), "Unknown relationship role.");
                Object(edge.Owner); Object(edge.From); Object(edge.To);
                break;
            case GroupJudgmentSubject group:
                Text(group.MeasureId, 256);
                Require(Enum.IsDefined(group.Role), "Unknown group role.");
                Require(group.Members is { Count: > 0 and <= 256 }, "Invalid group member count.");
                foreach (var member in group.Members!) Object(member);
                Require(group.Members.Select(ObjectKey).Distinct().Count() == group.Members.Count,
                    "Duplicate typed group member.");
                break;
            case ParsimonyFindingJudgmentSubject finding:
                Text(finding.MeasureId, 256);
                Text(finding.FindingId, 4096);
                break;
            default: throw new FormatException("Unknown subject kind.");
        }
    }

    private static void Object(JudgmentObject value)
    {
        Require(value is not null && Classes.Contains(value.Class, StringComparer.Ordinal), "Unknown model class.");
        Id(value!.Id);
        if (value.Class.EndsWith("Msa", StringComparison.Ordinal)) Id(value.OwningEntryId);
        else Require(value.OwningEntryId is null, "Only an MSA carries an owning-entry qualifier.");
    }

    internal static string ObjectKey(JudgmentObject value) =>
        value.Class + ":" + Identity(value.Id) + ":" +
        (value.OwningEntryId is null ? "" : Identity(value.OwningEntryId));

    internal static string Identity(string id) => CanonicalId.FromGuid(CanonicalId.Parse(id).ToGuid()).Suffix;
    internal static bool SameId(string a, string b) => Identity(a) == Identity(b);
    private static void Id(string? id) => Require(CanonicalId.TryParse(id, out _), "Invalid portable entity identity.");
    private static void Digest(string digest) => Require(Sha256Value.IsCanonical(digest), "Invalid SHA-256 digest.");
    private static void Text(string? value, int bound) =>
        Require(!string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value) <= bound &&
            value.All(c => !char.IsControl(c) && c is not '\u2028' and not '\u2029'),
            "Missing text or exceeded UTF-8 text bound.");
    private static void OptionalText(string? value, int bound)
    {
        if (value is not null) Require(Encoding.UTF8.GetByteCount(value) <= bound, "Exceeded UTF-8 text bound.");
    }
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new FormatException(message);
    }
}
