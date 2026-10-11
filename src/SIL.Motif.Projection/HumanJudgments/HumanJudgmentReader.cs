using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Projection.HumanJudgments;

/// <summary>A valid, immutable judgment read from one Notebook record.</summary>
/// <param name="PhysicalValue">The data field's text, exactly as stored.</param>
/// <param name="VisibleId">The ID derived from the data, which the ID field should show.</param>
/// <param name="RecordTitle">The record's Title as the linguist sees it; informational only.</param>
/// <param name="Warnings">Edits to the ID or sentence that leave the judgment in force.</param>
public sealed record StoredHumanJudgment(
    string RecordId,
    HumanJudgment Judgment,
    string ContentDigest,
    string PhysicalValue,
    string VisibleId,
    string RecordTitle,
    IReadOnlyList<HumanJudgmentWarning> Warnings);

/// <summary>A Notebook record with judgment fields that could not be accepted as a judgment.</summary>
/// <param name="Kind">
/// <c>damaged</c>, <c>hash-failed</c>, <c>identity</c>, <c>incomplete</c> or <c>duplicate-id</c>.
/// </param>
/// <param name="VisibleId">The ID the record's ID field shows, when it shows one.</param>
public sealed record UnavailableHumanJudgment(
    string RecordId,
    string PhysicalValue,
    string Message,
    string Kind,
    string? VisibleId,
    string RecordTitle);

/// <summary>An immutable view of the judgments and unavailable values in one project.</summary>
public sealed record HumanJudgmentProjectSnapshot(
    string ProjectId,
    HumanJudgmentFieldCapability Capability,
    string Message,
    IReadOnlyList<StoredHumanJudgment> Judgments,
    IReadOnlyList<UnavailableHumanJudgment> Unavailable,
    string LogicalDigest);

/// <summary>Reads reserved values from an already-open project without changing or saving it.</summary>
public static class HumanJudgmentReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Reads top-level Notebook records and binds every marker to the actual project and record ids.</summary>
    public static HumanJudgmentProjectSnapshot Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var resolution = HumanJudgmentFieldResolver.Resolve(cache);
        if (resolution.Capability != HumanJudgmentFieldCapability.Available)
            return Snapshot(projectId, resolution.Capability, resolution.Message, [], []);

        var notebook = cache.LangProject.ResearchNotebookOA;
        if (notebook is null)
            return Snapshot(projectId, HumanJudgmentFieldCapability.NotebookUnavailable,
                "The project has no Research Notebook to read.", [], []);

        var judgments = new List<StoredHumanJudgment>();
        var unavailable = new List<UnavailableHumanJudgment>();
        foreach (var record in notebook.RecordsOC.OrderBy(item => item.Guid))
        {
            var recordId = CanonicalId.FromGuid(record.Guid).Value;
            var physicalValue = cache.DomainDataByFlid.get_StringProp(record.Hvo, resolution.CacheFieldId)?.Text
                ?? string.Empty;
            var idText = cache.DomainDataByFlid.get_StringProp(record.Hvo, resolution.IdFieldId)?.Text ?? string.Empty;
            if (physicalValue.Length == 0 && idText.Length == 0) continue;
            var title = record.Title?.Text ?? string.Empty;
            var shownId = idText.Length == 0 ? null : idText;
            if (physicalValue.Length == 0)
            {
                unavailable.Add(new(recordId, idText, "The data field on this record is empty.", "incomplete",
                    shownId, title));
                continue;
            }

            var paragraphs = record.DescriptionOA?.ParagraphsOS.OfType<IStTxtPara>()
                .Select(paragraph => paragraph.Contents?.Text ?? string.Empty).ToArray() ?? [];
            try
            {
                var reading = HumanJudgmentCodec.Read(physicalValue, idText, paragraphs, projectId, recordId);
                var value = Freeze(reading.Judgment);
                judgments.Add(new(recordId, value, HumanJudgmentCodec.LogicalDigest(value), physicalValue,
                    HumanJudgmentCodec.VisibleId(value.JudgmentId), title, reading.Warnings));
            }
            catch (FormatException error)
            {
                var kind = error is HumanJudgmentFormatException failure ? failure.Failure switch
                {
                    HumanJudgmentFailure.HashFailed => "hash-failed",
                    HumanJudgmentFailure.Identity => "identity",
                    _ => "damaged",
                } : "damaged";
                unavailable.Add(new(recordId, physicalValue, error.Message, kind, shownId, title));
            }
        }

        // Two different judgments cannot share one ID: neither may be trusted to be the one a person means.
        foreach (var group in judgments.GroupBy(item => item.VisibleId, StringComparer.Ordinal)
                     .Where(group => group.Select(item => item.Judgment.JudgmentId).Distinct(StringComparer.Ordinal).Count() > 1)
                     .ToArray())
            foreach (var item in group)
            {
                judgments.Remove(item);
                unavailable.Add(new(item.RecordId, item.PhysicalValue,
                    $"Another judgment has the same ID {group.Key}; neither can be used until one is deleted.",
                    "duplicate-id", group.Key, item.RecordTitle));
            }

        return Snapshot(projectId, HumanJudgmentFieldCapability.Available,
            resolution.Message, judgments, unavailable);
    }

    private static HumanJudgmentProjectSnapshot Snapshot(
        string projectId,
        HumanJudgmentFieldCapability capability,
        string message,
        IEnumerable<StoredHumanJudgment> judgments,
        IEnumerable<UnavailableHumanJudgment> unavailable)
    {
        var orderedJudgments = judgments.OrderBy(item => item.RecordId, StringComparer.Ordinal).ToArray();
        var orderedUnavailable = unavailable.OrderBy(item => item.RecordId, StringComparer.Ordinal).ToArray();
        return new(projectId, capability, message,
            Array.AsReadOnly(orderedJudgments), Array.AsReadOnly(orderedUnavailable),
            Digest(projectId, capability, orderedJudgments, orderedUnavailable));
    }

    private static HumanJudgment Freeze(HumanJudgment value)
    {
        var body = value.Body switch
        {
            DispositionJudgment { Subject: GroupJudgmentSubject group } disposition =>
                disposition with { Subject = group with { Members = group.Members.ToImmutableArray() } },
            ReviewedNegativeJudgment { Target: ReadingNegativeTarget reading } negative =>
                negative with { Target = reading with { Morphs = reading.Morphs.ToImmutableArray() } },
            _ => value.Body,
        };
        return value with
        {
            Replaces = value.Replaces.ToImmutableArray(),
            Body = body,
            Extensions = value.Extensions?.ToImmutableDictionary(
                pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
        };
    }

    private static string Digest(
        string projectId,
        HumanJudgmentFieldCapability capability,
        IReadOnlyList<StoredHumanJudgment> judgments,
        IReadOnlyList<UnavailableHumanJudgment> unavailable)
    {
        var content = new StringBuilder()
            .Append(projectId).Append('\0')
            .Append(capability).Append('\n');
        foreach (var item in judgments)
        {
            content.Append(item.RecordId).Append('\0').Append(item.ContentDigest);
            foreach (var warning in item.Warnings) content.Append('\0').Append(warning.Kind);
            content.Append('\n');
        }
        foreach (var item in unavailable)
            content.Append(item.RecordId).Append('\0').Append("unavailable:").Append(item.Kind).Append(':')
                .Append(Hash(item.PhysicalValue.Normalize(NormalizationForm.FormD))).Append('\n');
        return Hash(content.ToString());
    }

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(value)));
}
