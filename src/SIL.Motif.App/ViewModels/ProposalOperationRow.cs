using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.App.ViewModels;

/// <summary>One authored operation and the values that can be shown for it.</summary>
public sealed record ProposalOperationRow(string Title, string Target, string Values)
{
    public bool HasTarget => Target.Length != 0;
    public bool HasValues => Values.Length != 0;

    internal static ProposalOperationRow For(ProposalOperationView operation, IEnumerable<EffectView> operationEffects)
    {
        var payload = ProposalOperationSentence.Payload(operation.AfterJson);
        var ws = Property("ws");
        var value = Property("text") ?? Property("form") ?? Property("name") ?? Property("value") ?? Property("label");
        if (operation.Kind == LexicalSenseOperationKinds.ClearGloss && ws is not null)
            value = MotifAppStrings.ProposalChangeEmpty;
        var identity = operation.Target ?? operation.EntityId;
        var target = identity is null ? string.Empty : Format(
            operation.Kind.StartsWith("lexical/lexSense/", StringComparison.Ordinal) ? MotifAppStrings.ProposalChangeSense :
            operation.Kind.StartsWith("lexical/lexEntry/", StringComparison.Ordinal) ? MotifAppStrings.ProposalChangeEntry :
            MotifAppStrings.ProposalChangeTarget, identity);
        var transitions = operationEffects.SelectMany(effect => effect.Changes
                .Where(change => ws is null || string.Equals(change.Ws, ws, StringComparison.OrdinalIgnoreCase))
                .Select(change => ProposalEffectText.Transition(effect, change)))
            .ToArray();
        var values = transitions.Length > 0 ? string.Join(Environment.NewLine, transitions) :
            value is null ? (ws is null ? string.Empty : Format(MotifAppStrings.ProposalChangeWritingSystem, ws)) :
            ws is null ? Format(MotifAppStrings.ProposalChangeValue, value) :
            Format(MotifAppStrings.ProposalChangeValueWithWritingSystem, ws, value);

        return new ProposalOperationRow(ProposalOperationSentence.For(operation, 0), target, values);

        string? Property(string key) => payload is { } data && data.TryGetProperty(key, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.True or JsonValueKind.False or
                    JsonValueKind.Number => property.GetRawText(),
                _ => null,
            }
            : null;
    }

    private static string Format(string format, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, format, values);
}
