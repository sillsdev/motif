using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>
/// Moves a Grammar Guide section, with its subtree, under another parent or to another place among its siblings by
/// staging one operation a FieldWorks person could make (drag in the list). The section keeps its GDR number and id, so
/// links to it stay valid (ADR 0005).
/// </summary>
public static class GdrMoveCommands
{
    /// <summary>Stages the move in the pending changes or a named Draft.</summary>
    public static CommandOutcome<GdrMoveResponse> Move(GdrMoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Id))
            return CommandOutcome<GdrMoveResponse>.Refused(new Refusal(RefusalCodes.GdrEmptyId, FailureReason.InvalidArgument,
                "Name the section to move, by GDR number or id."));
        var list = Queries.GdrQuery.List(new GdrListRequest(request.ProjectPath));
        if (!list.Succeeded) return CommandOutcome<GdrMoveResponse>.Refused(list.Refusal!);
        if (!list.Value!.HasBaseline)
            return CommandOutcome<GdrMoveResponse>.Refused(new Refusal(RefusalCodes.GdrNoBaseline, FailureReason.NotFound,
                "This project has no Baseline yet. Capture one to move sections of its Grammar Guide."));
        if (list.Value.UsesOldListName)
            return CommandOutcome<GdrMoveResponse>.Refused(new Refusal(RefusalCodes.GdrListNotWritable, FailureReason.Refused,
                "The Guide list is still named 'Grammar Design Records', which Motif reads but does not change. Rename it 'Grammar Guide' in FieldWorks first."));
        return ProposalCommands.StageGdrMove(request, string.IsNullOrWhiteSpace(request.Draft) ? null : request.Draft.Trim());
    }
}
