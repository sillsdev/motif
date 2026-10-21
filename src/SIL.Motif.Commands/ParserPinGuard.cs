using System.Collections.Generic;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands;

/// <summary>Turns a parser whose version cannot be read into an enqueue refusal.</summary>
internal static class ParserPinGuard
{
    /// <summary>A refusal when the parser file reports no version; null when pinning can proceed or there is no parser.</summary>
    internal static Refusal? Refuse(string? parserPath)
    {
        if (ParserIdentity.Read(parserPath) is not { } identity || identity.Version != "unavailable") return null;
        return new Refusal(RefusalCodes.JobParserUnidentifiable, FailureReason.Refused,
            "The parser at '" + identity.Path + "' did not report its version, so Motif cannot tell which parser a " +
            "queued job should run on. Rebuild or reinstall PanGloss, then queue the job again.",
            new Dictionary<string, string>(System.StringComparer.Ordinal) { ["parserPath"] = identity.Path });
    }
}
