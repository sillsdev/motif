using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.DryRun;

namespace SIL.Motif.Worker.Jobs;

/// <summary>The closed, frozen request carried by a queued Dry Run.</summary>
public sealed record DryRunJobInput(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("proposal")] FrozenProposalRevision Proposal,
    [property: JsonPropertyName("prerequisites")] IReadOnlyList<FrozenProposalRevision> Prerequisites,
    [property: JsonPropertyName("sourceBaseline")] DryRunSourceBinding? SourceBaseline)
{
    public const int CurrentSchemaVersion = 1;

    public static DryRunJobInput Parse(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Any(property => property.Name is not
                    ("schemaVersion" or "proposal" or "prerequisites" or "sourceBaseline")))
                throw new InvalidDataException("The Dry Run job input contains unsupported fields.");
            var input = JsonSerializer.Deserialize<DryRunJobInput>(inputJson, MotifJson.CreateOptions())
                ?? throw new InvalidDataException("The Dry Run job input is empty.");
            input.Validate();
            return input;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("This Dry Run job lacks frozen source or Proposal evidence. Rerun its Dry Run.",
                exception);
        }
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || Proposal is null || Prerequisites is null)
            throw new InvalidDataException("This Dry Run job lacks frozen source or Proposal evidence. Rerun its Dry Run.");
        var requested = Proposal.Validate();
        var ids = new HashSet<string>(StringComparer.Ordinal) { requested.ProposalId.Value };
        foreach (var prerequisite in Prerequisites)
        {
            if (prerequisite is null || !ids.Add(prerequisite.ProposalId))
                throw new InvalidDataException("The frozen prerequisite closure contains a repeated Proposal.");
            prerequisite.Validate();
        }
        if (SourceBaseline is not null) SourceBaseline.Validate();
    }

    public PrerequisiteExecutionPlan BuildExecutionPlan(IReadOnlyCollection<Guid> appliedProposalIds)
    {
        Validate();
        var requested = Proposal.Validate();
        var frozen = Prerequisites.Select(item => item.Validate()).ToArray();
        return PrerequisiteExecutionPlan.CreateFromFrozen(requested, frozen, appliedProposalIds);
    }
}
