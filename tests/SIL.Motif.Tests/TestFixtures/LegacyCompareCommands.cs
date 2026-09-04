using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Renders <see cref="CompareCommands"/>' typed outcomes back into the pre-typed-outcome
/// <see cref="CommandResult"/> shape, through the same <see cref="CommandTextRenderer"/> the CLI itself
/// uses. See <see cref="LegacyProposalCommands"/> for why.
/// </summary>
internal static class LegacyCompareCommands
{
    public static CommandResult Produce(
        string fwDataPath, string productVersion, string fromAssessmentId, string toAssessmentId, bool asJson) =>
        CommandTextRenderer.Render(
            CompareCommands.Produce(
                new ProduceComparisonRequest(fwDataPath, productVersion, fromAssessmentId, toAssessmentId)),
            asJson);
}
