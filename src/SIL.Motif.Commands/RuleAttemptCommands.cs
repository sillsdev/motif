using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony.RuleAttempts;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Config;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>The <c>parsimony --rule-attempts</c> check: the same-shape rule-attempt requirement over a plan file (ADR 0116).</summary>
public static class RuleAttemptCommands
{
    /// <summary>
    /// Reads the intents of a <c>compose-batch</c> file and groups the variants they add by shape. Cost is O(intents) plus
    /// one object lookup per variant link in the project's current Baseline; no parse runs.
    /// </summary>
    public static CommandOutcome<RuleAttemptSummary> Check(CheckRuleAttemptsRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                var configuration = new ProjectConfigurationReader().Read(project);
                using var document = JsonDocument.Parse(request.PlanJson);
                var root = document.RootElement;
                var intents = root.ValueKind == JsonValueKind.Array ? root
                    : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("intents", out var inner) ? inner : default;
                if (intents.ValueKind != JsonValueKind.Array)
                    return Refuse(RefusalCodes.RuleAttemptsPlanInvalid, "The plan file must be a JSON array of intents, or an object with an 'intents' array.");
                var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project,
                    includeResolvedReadings: false, includeWordContext: false);
                if (!current.Succeeded) return CommandOutcome<RuleAttemptSummary>.Refused(current.Refusal!);
                if (current.Value!.Baseline is not { } baseline || !File.Exists(baseline.FwDataPath))
                    return Refuse(RefusalCodes.RuleAttemptsNoBaseline, "A published Baseline is required to read the main entries' forms.");
                using var reader = BaselineReadCache.Open(baseline.FwDataPath);
                var repository = reader.Cache.ServiceLocator.GetInstance<ICmObjectRepository>();
                var shapes = VariantIntentShapes.FromIntents(intents.EnumerateArray().ToArray(),
                    id => VariantFormCapture.FormOf(repository, id));
                return CommandOutcome<RuleAttemptSummary>.Success(
                    RuleAttemptSummary.Create(shapes, new HypothesisRuleAttemptLedger(new HypothesisRepository(database)), configuration.ParsimonyBlockOn));
            }
            catch (Exception exception) when (exception is JsonException or ProjectConfigurationException)
            {
                return Refuse(RefusalCodes.RuleAttemptsInvalid, exception.Message);
            }
        });

    private static CommandOutcome<RuleAttemptSummary> Refuse(string code, string message) =>
        CommandOutcome<RuleAttemptSummary>.Refused(new Refusal(code, FailureReason.Refused, message,
            new Dictionary<string, string>()));
}
