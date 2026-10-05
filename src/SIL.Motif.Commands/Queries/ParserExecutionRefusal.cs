using System.Globalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>Preserves a parser failure's message, facts and compile issues across command surfaces.</summary>
public static class ParserExecutionRefusal
{
    public static Refusal From(string code, string projectPath, PanGlossOutcome.Refused failure)
    {
        var raw = failure.StandardError + (string.IsNullOrWhiteSpace(failure.StandardOutput)
            ? string.Empty : Environment.NewLine + failure.StandardOutput);
        var diagnostic = ParserCompileDiagnosticReader.Read(raw);
        return new(code, FailureReason.Refused, diagnostic?.Summary ?? failure.Message,
            new Dictionary<string, string>
            {
                ["projectPath"] = projectPath,
                ["exitCode"] = failure.ExitCode.ToString(CultureInfo.InvariantCulture),
                ["parserMessage"] = failure.Message,
                ["standardError"] = failure.StandardError,
                ["standardOutput"] = failure.StandardOutput,
            }, diagnostic);
    }
}
