using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;

namespace SIL.Motif.App.Preview;

/// <summary>Where the preview window puts a record's Discussion edit and how it applies the pending changes.</summary>
public interface IGdrEditStore
{
    /// <summary>Adds the record's edit to the pending-changes Draft or updates it there; returns a refusal message or null.</summary>
    Task<string?> StageAsync(string recordId, TextParagraphsPayload paragraphs);

    /// <summary>Applies the pending changes, then recaptures the Baseline; returns why nothing was applied, or null.</summary>
    Task<GdrApplyRefusal?> ApplyAsync();
}

/// <summary>
/// Why Apply wrote nothing. <paramref name="StaleRecordId"/> names a record whose text changed in FieldWorks since its
/// edit began; the Baseline is recaptured then too, so the record reads as it now is.
/// </summary>
public sealed record GdrApplyRefusal(string Message, string? StaleRecordId = null);

/// <summary>Stages and applies through the project's own commands, off the UI thread.</summary>
public sealed class GdrEditStore(string projectPath) : IGdrEditStore
{
    public Task<string?> StageAsync(string recordId, TextParagraphsPayload paragraphs) => Task.Run(() =>
    {
        var staged = ProposalCommands.StageTextParagraphs(new StageTextParagraphsRequest(projectPath, recordId, paragraphs));
        return staged.Succeeded ? null : staged.Refusal!.Message;
    });

    public Task<GdrApplyRefusal?> ApplyAsync() => Task.Run(() =>
    {
        var applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(projectPath, null, null));
        if (!applied.Succeeded && applied.Refusal!.Code != RefusalCodes.TextFieldStale) return new GdrApplyRefusal(applied.Refusal.Message);
        // GdrQuery reads the Baseline, so recapture it to show what Apply or FieldWorks wrote.
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath));
        if (!applied.Succeeded)
            return new GdrApplyRefusal(applied.Refusal!.Message, applied.Refusal.Facts.GetValueOrDefault("owner"));
        return captured.Succeeded ? null : new GdrApplyRefusal(captured.Refusal!.Message);
    });
}

/// <summary>Turns a Discussion edit into the <c>text/setParagraphs</c> payload of a CmPossibility's Discussion.</summary>
public static class GdrDiscussionPayload
{
    public const string Field = "CmPossibility.Discussion";

    /// <summary>The payload for <paramref name="edit"/>, or for <paramref name="document"/> unchanged when there is no edit.</summary>
    public static TextParagraphsPayload From(GdrDiscussionEdit? edit, Document document) => edit is null
        ? new(Field, document.Paragraphs.Select(paragraph => paragraph.Key as string).ToArray(), [], [],
            new Dictionary<string, string>())
        : new(Field, edit.Order, edit.Changed.Select(change => new TextParagraphChange(change.Index, change.Key,
                change.Content.Style.Length > 0 ? change.Content.Style : null,
                change.Content.Runs.Where(run => !GdrDiscussion.IsMarker(run)).Select(run => new TextRun(run.Text, run.Ws,
                    run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var style) && style.Length > 0 ? style : null))
                    .ToArray())).ToArray(),
            edit.Removed, edit.Starting);
}
