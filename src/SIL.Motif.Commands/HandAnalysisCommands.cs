using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>Checks asserted intent against saved records. It creates no records and never saves the project.</summary>
public static class HandAnalysisCommands
{
    public static CommandOutcome<HandAnalysisCheckResponse> Check(HandAnalysisCheckRequest request)
    {
        var parsed = HandAnalysisJson.Parse(request.InputJson);
        if (parsed.Input is null) return Invalid(parsed.Errors);
        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<HandAnalysisCheckResponse>.Refused(located.Refusal!);
        return ProjectStoreCommand.RunOperation(request.ProjectPath, () =>
            ProjectReadCache.ReadSaved(located.Value!, (_, cache) => Check(cache, parsed.Input)));
    }

    /// <summary>The caller owns the loaded model. Resolution performs no unit of work or persistence.</summary>
    public static CommandOutcome<HandAnalysisCheckResponse> Check(LcmCache cache, HandAnalysisInput input)
    {
        var parsed = HandAnalysisJson.Parse(HandAnalysisJson.Serialize(input));
        if (parsed.Input is null) return Invalid(parsed.Errors);
        try { return CommandOutcome<HandAnalysisCheckResponse>.Success(new HandAnalysisResolution(cache, parsed.Input).Read()); }
        catch (HandAnalysisResolution.Refused exception)
        { return CommandOutcome<HandAnalysisCheckResponse>.Refused(exception.Refusal); }
    }

    private static CommandOutcome<HandAnalysisCheckResponse> Invalid(IReadOnlyList<HandAnalysisValidationError> errors) =>
        CommandOutcome<HandAnalysisCheckResponse>.Refused(new(RefusalCodes.HandAnalysisInvalidLink,
            FailureReason.InvalidArgument, "The hand-analysis input is invalid.",
            errors.GroupBy(error => error.Pointer).ToDictionary(group => group.Key,
                group => string.Join(" ", group.Select(error => error.Message)))));
}
