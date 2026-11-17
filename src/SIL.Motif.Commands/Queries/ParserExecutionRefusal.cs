using SIL.Motif.Contract.Jobs;
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
        code = failure.FailureCategory switch
        {
            JobFailureCategory.ParserKilled => RefusalCodes.AssessParserKilled,
            JobFailureCategory.OutOfMemory => RefusalCodes.AssessOutOfMemory,
            _ => code,
        };
        var raw = failure.StandardError + (string.IsNullOrWhiteSpace(failure.StandardOutput)
            ? string.Empty : Environment.NewLine + failure.StandardOutput);
        var diagnostic = failure.FailureCategory == JobFailureCategory.ParserRefusal
            ? ParserCompileDiagnosticReader.Read(raw) : null;
        var facts = new Dictionary<string, string>
        {
            ["projectPath"] = projectPath,
            ["exitCode"] = failure.ExitCode.ToString(CultureInfo.InvariantCulture),
            ["parserMessage"] = failure.Message,
            ["standardError"] = failure.StandardError,
            ["standardOutput"] = failure.StandardOutput,
        };
        if (failure.TerminationSignal is { } signal)
            facts["signal"] = signal.ToString(CultureInfo.InvariantCulture);
        return new(code, FailureReason.Refused, diagnostic?.Summary ?? failure.Message, facts, diagnostic);
    }
}
