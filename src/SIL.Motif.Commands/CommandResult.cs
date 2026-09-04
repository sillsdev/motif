using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>The result of one CLI command: an exit code, the text to print, and why it refused.</summary>
/// <remarks>
/// <see cref="Reason"/> is null on success and on the few failures raised before a verb is entered.
/// It is what <c>--json</c> renders as a structured envelope; the text in <see cref="Output"/> is the
/// same wording either way, because the human interface did not need changing.
/// </remarks>
public sealed record CommandResult(int ExitCode, string Output, FailureReason? Reason = null);
