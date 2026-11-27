using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Spells each id in both forms: the canonical id a Proposal names and the hyphenated guid a project file stores. It
/// needs no project and reads nothing; an argument that is neither form is reported on its own item and the others
/// still convert.
/// </summary>
public static class IdConvertCommand
{
    /// <summary>The most ids one call takes.</summary>
    public const int MaxIds = 1000;

    public static CommandOutcome<IdConvertResponse> Convert(IdConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Ids is null || request.Ids.Count == 0)
            return Refuse("Name at least one id to convert.");
        if (request.Ids.Count > MaxIds)
            return Refuse($"Convert at most {MaxIds} ids at a time.");
        var items = request.Ids.Select(raw =>
        {
            if (ObjectId.TryParse(raw, out var id, out var error))
                return new IdConvertItem(raw, ObjectId.ToGuidText(id), CanonicalId.FromGuid(id.ToGuid()).Value);
            return new IdConvertItem(raw ?? "", null, null, IdConvertResponse.InvalidIdCode, error);
        }).ToList();
        return CommandOutcome<IdConvertResponse>.Success(new IdConvertResponse(items));
    }

    private static CommandOutcome<IdConvertResponse> Refuse(string message) =>
        CommandOutcome<IdConvertResponse>.Refused(new Refusal(RefusalCodes.IdConvertInvalidRequest, FailureReason.InvalidArgument, message));
}
