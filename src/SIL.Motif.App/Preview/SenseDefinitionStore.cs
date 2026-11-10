using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.App.Preview;

/// <summary>Where the sense Definition preview puts one writing system's edit and how it applies the pending changes.</summary>
public interface ISenseDefinitionStore
{
    /// <summary>Adds the alternative's edit to the pending-changes Draft or updates it there; returns a refusal message or null.</summary>
    Task<string?> StageAsync(string senseId, TextRichStringPayload richString);

    /// <summary>Takes the alternative's edit out of the pending-changes Draft; returns a refusal message or null.</summary>
    Task<string?> DiscardAsync(string senseId, string ws);

    /// <summary>The alternative's edit the pending-changes Draft holds, none, or why the Draft could not be read.</summary>
    Task<RichStringPendingRead> PendingAsync(string senseId, string ws);

    /// <summary>Applies the pending changes, then recaptures the Baseline; returns why nothing was applied, or null.</summary>
    Task<GdrApplyRefusal?> ApplyAsync();
}

/// <summary>A read of an alternative's edit in the Draft: the edit, or null for none, unless <paramref name="Refusal"/> says why it failed.</summary>
public sealed record RichStringPendingRead(TextRichStringPayload? RichString, string? Refusal = null);

/// <summary>Stages and applies through the project's own commands, off the UI thread.</summary>
public sealed class SenseDefinitionStore(string projectPath) : ISenseDefinitionStore
{
    /// <summary>The field the preview edits.</summary>
    public const string Field = "LexSense.Definition";

    public Task<string?> StageAsync(string senseId, TextRichStringPayload richString) => Task.Run(() =>
    {
        var staged = ProposalCommands.StageTextRichString(new StageTextRichStringRequest(projectPath, senseId, richString));
        return staged.Succeeded ? null : staged.Refusal!.Message;
    });

    public Task<string?> DiscardAsync(string senseId, string ws) => Task.Run(() =>
    {
        var discarded = ProposalCommands.DiscardTextRichString(new TextRichStringFieldRequest(projectPath, senseId, Field, ws));
        return discarded.Succeeded ? null : discarded.Refusal!.Message;
    });

    public Task<RichStringPendingRead> PendingAsync(string senseId, string ws) => Task.Run(() =>
    {
        var pending = ProposalCommands.PendingTextRichString(new TextRichStringFieldRequest(projectPath, senseId, Field, ws));
        return pending.Succeeded ? new RichStringPendingRead(pending.Value!.RichString) : new RichStringPendingRead(null, pending.Refusal!.Message);
    });

    // Apply covers the whole Draft, so it refreshes and reports drift exactly as the GDR preview does.
    public Task<GdrApplyRefusal?> ApplyAsync() => new GdrEditStore(projectPath).ApplyAsync();
}
