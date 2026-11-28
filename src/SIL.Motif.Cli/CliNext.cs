using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Help;

namespace SIL.Motif.Cli;

/// <summary>
/// The <c>next</c> steering sentence of a CLI failure envelope: what MCP's <c>Next:</c> line says, with a next
/// command written as a <c>motif</c> line the caller can run.
/// </summary>
internal static class CliNext
{
    private static readonly HelpCatalog Help = HelpCatalog.Load();

    /// <summary>The steering sentence for a failure, from its code's Help and next step when it has them.</summary>
    public static string For(FailureReason reason, string? code = null, string? message = null,
        IReadOnlyDictionary<string, string>? facts = null)
    {
        var fallback = RefusalCatalog.ReasonHint(reason);
        if (code is null) return fallback;
        var refusal = new Refusal(code, reason, string.IsNullOrWhiteSpace(message) ? code : message, facts);
        var outcomeHelp = RefusalCatalog.WaitOutcomeHelp(refusal);
        var hint = outcomeHelp is not null ? Help.RefusalWhy(outcomeHelp) ?? fallback :
            RefusalCatalog.Specs.GetValueOrDefault(code)?.Next is { } step && Help.RefusalWhy(code) is { } why
                ? why + RenderStep(step, refusal)
                : fallback;
        foreach (var (key, value) in refusal.Facts) hint = hint.Replace("{" + key + "}", value, StringComparison.Ordinal);
        return hint;
    }

    /// <summary>A failure envelope carrying the steering sentence its reason and code earn.</summary>
    public static FailureEnvelope Envelope(FailureReason reason, string message,
        IReadOnlyDictionary<string, string>? detail = null, string? code = null,
        ParserCompileDiagnostic? parserDiagnostic = null) =>
        new(reason, message, detail, code, parserDiagnostic, For(reason, code, message, detail));

    private static string RenderStep(NextStep step, Refusal refusal)
    {
        if (step.Command == NextStep.AskPerson) return string.Empty;
        var empty = new Dictionary<string, string>();
        var values = step.Args.Select(pair => (pair.Key, Value: pair.Value.Resolve(refusal.Facts, empty))).ToList();
        if (step.Command == NextStep.RepeatCall)
        {
            var given = values.Where(pair => pair.Value is not null).Select(pair => pair.Key + " set to " + pair.Value).ToList();
            return " Repeat this command" + (given.Count == 0 ? "" : " with " + string.Join(" and ", given)) + ".";
        }
        var flags = string.Concat(values.Select(pair =>
            " --" + pair.Key.Replace('_', '-') + " " + (pair.Value ?? "<" + pair.Key + ">")));
        // Only a person may run a HumanOnly command, so MCP and the CLI both tell the caller to ask.
        var humanOnly = CommandCatalog.All.Any(command => command.Name == step.Command && command.Agent == AgentClass.HumanOnly);
        return (humanOnly ? " Ask the person to run `motif " : " Run `motif ") + step.Command + flags + "`.";
    }
}
