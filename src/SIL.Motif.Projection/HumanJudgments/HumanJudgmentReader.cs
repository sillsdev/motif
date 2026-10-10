using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Projection.HumanJudgments;

/// <summary>A valid, immutable judgment read from one Notebook record.</summary>
public sealed record StoredHumanJudgment(
    string RecordId,
    HumanJudgment Judgment,
    string ContentDigest,
    string PhysicalValue);

/// <summary>A non-empty Notebook field value that could not be accepted as a judgment.</summary>
public sealed record UnavailableHumanJudgment(string RecordId, string PhysicalValue, string Message);

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
            var richValue = cache.DomainDataByFlid.get_StringProp(record.Hvo, resolution.CacheFieldId);
            var physicalValue = richValue?.Text ?? string.Empty;
            if (physicalValue.Length == 0) continue;

            try
            {
                var value = Freeze(ReadableJudgmentCodec.Parse(richValue!, projectId, recordId));
                judgments.Add(new(recordId, value, HumanJudgmentCodec.LogicalDigest(value), physicalValue));
            }
            catch (FormatException error)
            {
                unavailable.Add(new(recordId, physicalValue, error.Message));
            }
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
            content.Append(item.RecordId).Append('\0').Append(item.ContentDigest).Append('\n');
        foreach (var item in unavailable)
            content.Append(item.RecordId).Append('\0').Append("unavailable:")
                .Append(Hash(item.PhysicalValue.Normalize(NormalizationForm.FormD))).Append('\n');
        return Hash(content.ToString());
    }

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(value)));
}
