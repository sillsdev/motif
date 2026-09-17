using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>The result of capturing a Baseline from a project's saved <c>.fwdata</c>.</summary>
public sealed record BaselineCaptureResponse(
    BaselineToken Token,
    string FwDataPath,
    DateTimeOffset SourceLastWriteUtc,
    bool FieldWorksHeldProject,
    bool ReusedExistingBytes,
    KnownProjectRegistrationFailure? RegistrationFailure = null);

/// <summary>Explains why a published Baseline could not be added to the machine's known-project registry.</summary>
public sealed record KnownProjectRegistrationFailure(string Message);
