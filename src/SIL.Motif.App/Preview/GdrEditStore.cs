using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.App.Preview;

/// <summary>Where the preview window puts a record's Discussion edit and how it applies the pending changes.</summary>
public interface IGdrEditStore
{
    /// <summary>Adds the record's edit to the pending-changes Draft or updates it there; returns a refusal message or null.</summary>
    Task<string?> StageAsync(string recordId, TextParagraphsPayload paragraphs);

    /// <summary>Takes the record's edit out of the pending-changes Draft; returns a refusal message or null.</summary>
    Task<string?> DiscardAsync(string recordId);

    /// <summary>The record's edit the pending-changes Draft holds, none, or why the Draft could not be read.</summary>
    Task<GdrPendingRead> PendingAsync(string recordId);

    /// <summary>Applies the pending changes, then recaptures the Baseline; returns why nothing was applied, or null.</summary>
    Task<GdrApplyRefusal?> ApplyAsync();
}

/// <summary>
/// Why Apply wrote nothing. <paramref name="StaleRecordId"/> names an object whose text changed in FieldWorks since its
/// edit began, and <paramref name="StaleWritingSystem"/> the alternative when the field has several; the Baseline is
/// recaptured then too, so the object reads as it now is. <paramref name="StaleField"/> is the field, as <c>Class.Field</c>.
/// </summary>
public sealed record GdrApplyRefusal(string Message, string? StaleRecordId = null, string? StaleWritingSystem = null,
    string? StaleField = null);

/// <summary>A read of a record's edit in the Draft: the edit, or null for none, unless <paramref name="Refusal"/> says why it failed.</summary>
public sealed record GdrPendingRead(TextParagraphsPayload? Paragraphs, string? Refusal = null);

/// <summary>
/// Stages and applies through the project's own commands, off the UI thread. <paramref name="managedRoot"/> and
/// <paramref name="runnerLauncher"/>, when given, replace the installation's managed root and Runner, so a test can
/// apply into a disposable root of its own.
/// </summary>
public sealed class GdrEditStore(string projectPath, string? managedRoot = null, IJobRunnerLauncher? runnerLauncher = null) : IGdrEditStore
{
    /// <summary>What Apply says when FieldWorks changed the project since it was read, and it has been read again.</summary>
    public const string RefreshedMessage = "Nothing was applied: the project changed in FieldWorks and has been read again. Apply again.";

    public Task<string?> StageAsync(string recordId, TextParagraphsPayload paragraphs) => Task.Run(() =>
    {
        var staged = ProposalCommands.StageTextParagraphs(new StageTextParagraphsRequest(projectPath, recordId, paragraphs));
        return staged.Succeeded ? null : staged.Refusal!.Message;
    });

    public Task<string?> DiscardAsync(string recordId) => Task.Run(() =>
    {
        var discarded = ProposalCommands.DiscardTextParagraphs(new TextParagraphsFieldRequest(projectPath, recordId, GdrDiscussionPayload.Field));
        return discarded.Succeeded ? null : discarded.Refusal!.Message;
    });

    public Task<GdrPendingRead> PendingAsync(string recordId) => Task.Run(() =>
    {
        var pending = ProposalCommands.PendingTextParagraphs(new TextParagraphsFieldRequest(projectPath, recordId, GdrDiscussionPayload.Field));
        return pending.Succeeded ? new GdrPendingRead(pending.Value!.Paragraphs) : new GdrPendingRead(null, pending.Refusal!.Message);
    });

    public Task<GdrApplyRefusal?> ApplyAsync() => Task.Run(() =>
    {
        var applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(projectPath, null, null), runnerLauncher: runnerLauncher);
        if (!applied.Succeeded && applied.Refusal!.Code == RefusalCodes.ChangeRefreshRequired)
        {
            // The person applies again after the refresh, so nothing is applied against a project they have not seen.
            var refreshed = Capture();
            return new GdrApplyRefusal(refreshed.Succeeded ? RefreshedMessage : refreshed.Refusal!.Message);
        }
        if (!applied.Succeeded && applied.Refusal!.Code != RefusalCodes.TextFieldStale) return new GdrApplyRefusal(applied.Refusal.Message);
        // GdrQuery reads the Baseline, so recapture it to show what Apply or FieldWorks wrote.
        var captured = Capture();
        if (!applied.Succeeded)
            return new GdrApplyRefusal(applied.Refusal!.Message, applied.Refusal.Facts.GetValueOrDefault("owner"),
                applied.Refusal.Facts.GetValueOrDefault("ws"), applied.Refusal.Facts.GetValueOrDefault("field"));
        return captured.Succeeded ? null : new GdrApplyRefusal(captured.Refusal!.Message);
    });

    private CommandOutcome<BaselineCaptureResponse> Capture() => managedRoot is null
        ? BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath))
        : BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
}

/// <summary>Turns a Discussion edit into the <c>text/setParagraphs</c> payload of a CmPossibility's Discussion.</summary>
public static class GdrDiscussionPayload
{
    public const string Field = "CmPossibility.Discussion";

    /// <summary>
    /// The payload for <paramref name="edit"/> of <paramref name="discussion"/>'s record: each changed paragraph with
    /// every run and paragraph property, and what each changed or removed paragraph held when the edit began, from
    /// <paramref name="began"/> where it names the paragraph and otherwise from the project as the record was shown.
    /// </summary>
    public static TextParagraphsPayload From(GdrDiscussionEdit edit, GdrDiscussion discussion,
        IReadOnlyDictionary<string, TextParagraphExpected>? began = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(discussion);
        var changed = edit.Changed.Select(change => new TextParagraphChange(change.Index, change.Key,
            change.Content.Style.Length > 0 ? change.Content.Style : null, discussion.ProjectRuns(change.Content),
            GdrDiscussion.ParagraphProps(change.Content))).ToArray();
        var expected = edit.Starting.Keys.ToDictionary(key => key,
            key => began?.GetValueOrDefault(key) ?? discussion.Expected.GetValueOrDefault(key) ?? new TextParagraphExpected(edit.Starting[key], "", ""),
            StringComparer.Ordinal);
        return new(Field, edit.Order, changed, edit.Removed, expected);
    }
}
