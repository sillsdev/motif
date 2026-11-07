using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

/// <summary>What a person pointed at when they asked an assistant to fix something.</summary>
/// <param name="Kind"><c>word</c>, <c>warning</c> or <c>matrix-cell</c>.</param>
/// <param name="Id">The word, Warning or cell, as the window names it.</param>
/// <param name="Label">A short label for it.</param>
/// <param name="Note">The person's optional note.</param>
public sealed record AskAssistantTarget(string Kind, string Id, string Label, string? Note);

/// <summary>Records a Request from a word card, Warning row or Matrix cell, and says how it went.</summary>
public static class AskAssistantAction
{
    /// <summary>Records the Request; returns the sentence to show, which is a refusal's sentence when it failed.</summary>
    public static async Task<string> RunAsync(WorkspaceContext context, AskAssistantTarget target)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        if (context.ProjectPath is not { } path) return WindowRefusal.GenericSentence;
        var note = string.IsNullOrWhiteSpace(target.Note) ? null : target.Note.Trim();
        var asked = await context.Commands.CreateRequestAsync(new CreateRequestRequest(path,
            MotifProductVersion.CurrentText, target.Kind, target.Id, target.Label, note),
            CancellationToken.None).ConfigureAwait(true);
        if (!asked.Succeeded) return WindowRefusal.From(asked.Refusal!).Sentence;
        if (path == context.ProjectPath) await context.PublishRequestsChangedAsync().ConfigureAwait(true);
        return MotifAppStrings.RequestAskedStatus;
    }
}
