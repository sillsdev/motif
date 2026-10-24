namespace SIL.Motif.Contract.Responses;

/// <summary>The tag Motif stores for a project on this machine.</summary>
/// <param name="Tag">The unique machine-local project tag.</param>
public sealed record ProjectTagResponse(string Tag);
