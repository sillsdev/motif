using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Renders <see cref="ConfigCommands"/>' typed outcomes back into the pre-typed-outcome
/// <see cref="CommandResult"/> shape, through the same <see cref="CommandTextRenderer"/> the CLI itself
/// uses. See <see cref="LegacyProposalCommands"/> for why.
/// </summary>
internal static class LegacyConfigCommands
{
    public static CommandResult Show(string fwDataPath, string productVersion) =>
        CommandTextRenderer.Render(
            ConfigCommands.Show(new ShowConfigRequest(fwDataPath, productVersion)), asJson: false);

    public static CommandResult ShowJson(string fwDataPath, string productVersion) =>
        CommandTextRenderer.Render(
            ConfigCommands.Show(new ShowConfigRequest(fwDataPath, productVersion)), asJson: true);
}
