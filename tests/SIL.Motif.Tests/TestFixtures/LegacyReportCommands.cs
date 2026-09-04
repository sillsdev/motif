using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Renders <see cref="ReportCommands"/>' typed outcomes back into the pre-typed-outcome
/// <see cref="CommandResult"/> shape, through the same <see cref="CommandTextRenderer"/> the CLI itself
/// uses. See <see cref="LegacyProposalCommands"/> for why.
/// </summary>
internal static class LegacyReportCommands
{
    public static CommandResult ListKinds(bool asJson) =>
        CommandTextRenderer.Render(ReportCommands.ListKinds(new ListReportKindsRequest()), asJson);

    public static CommandResult Produce(
        string fwDataPath, string productVersion, string assessmentId, string kind, string? word, string? text,
        bool asJson) =>
        CommandTextRenderer.Render(
            ReportCommands.Produce(
                new ProduceReportRequest(fwDataPath, productVersion, assessmentId, kind, word, text)),
            asJson);
}
