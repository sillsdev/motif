using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Authors a Draft fixture's label and Rationale through the same commands production callers use.</summary>
internal static class DraftRationale
{
    private const string ProductVersion = "1.0";

    /// <summary>Writes a Draft's Rationale by Draft name, as the old comment verb did.</summary>
    public static CommandOutcome<ProposalTextChangedResponse> Write(
        string fwDataPath, string productVersion, string draftName, string text)
    {
        var shown = ProposalCommands.Show(new ShowProposalRequest(fwDataPath, productVersion, draftName));
        if (!shown.Succeeded) return CommandOutcome<ProposalTextChangedResponse>.Refused(shown.Refusal!);
        return ProposalTexts.SetRationale(new SetRationaleRequest(fwDataPath, productVersion, shown.Value!.ProposalId, text));
    }

    /// <summary>Authors the label and Rationale a Proposal fixture needs before it can be finalized.</summary>
    public static void Author(
        string fwDataPath,
        string draftName,
        string shortDescription,
        string extendedExplanation)
    {
        var label = ProposalCommands.Label(new LabelRequest(fwDataPath, ProductVersion, draftName, shortDescription));
        if (!label.Succeeded) throw new InvalidOperationException(label.Refusal!.Message);

        var shown = ProposalCommands.Show(new ShowProposalRequest(fwDataPath, ProductVersion, draftName));
        if (!shown.Succeeded) throw new InvalidOperationException(shown.Refusal!.Message);
        var rationale = ProposalTexts.SetRationale(
            new SetRationaleRequest(fwDataPath, ProductVersion, shown.Value!.ProposalId, extendedExplanation));
        if (!rationale.Succeeded) throw new InvalidOperationException(rationale.Refusal!.Message);
    }
}
