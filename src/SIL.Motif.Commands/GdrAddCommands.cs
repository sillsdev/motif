using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>
/// Adds a Grammar Guide part or section by staging operations a FieldWorks person could make (ADR 0003): a new item in
/// the "Grammar Guide" list, which the same operation creates when the project has none (ADR 0005 decision 1).
/// </summary>
public static class GdrAddCommands
{
    /// <summary>Stages the item, with its body when one is given, in the pending changes or a named Draft.</summary>
    public static CommandOutcome<GdrAddResponse> Add(GdrAddRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Contains('\n') || request.Title.Contains('\r'))
            return CommandOutcome<GdrAddResponse>.Refused(new Refusal(RefusalCodes.GdrTitleInvalid, FailureReason.InvalidArgument,
                "Give the section a title on one line."));
        if (request.Markdown is { } markdown && markdown.TrimStart('﻿').StartsWith("---\nfwmd:", StringComparison.Ordinal))
            return CommandOutcome<GdrAddResponse>.Refused(new Refusal(RefusalCodes.GdrMarkdownInvalid, FailureReason.InvalidArgument,
                "Give the body only, in the ADR 0005 template, without the front matter that 'guide show --format fw-md' prints."));
        var list = GdrQuery.List(new GdrListRequest(request.ProjectPath));
        if (!list.Succeeded) return CommandOutcome<GdrAddResponse>.Refused(list.Refusal!);
        if (!list.Value!.HasBaseline)
            return CommandOutcome<GdrAddResponse>.Refused(new Refusal(RefusalCodes.GdrNoBaseline, FailureReason.NotFound,
                "This project has no Baseline yet. Capture one to add to its Grammar Guide."));
        if (list.Value.UsesOldListName)
            return CommandOutcome<GdrAddResponse>.Refused(new Refusal(RefusalCodes.GdrListNotWritable, FailureReason.Refused,
                "The Guide list is still named 'Grammar Design Records', which Motif reads but does not add to. Rename it 'Grammar Guide' in FieldWorks first."));
        var staged = ProposalCommands.StageGdrSection(request.ProjectPath, request.Title, request.Parent, request.After,
            request.Markdown, request.WritingSystem, string.IsNullOrWhiteSpace(request.Draft) ? null : request.Draft.Trim());
        return staged.Succeeded
            ? CommandOutcome<GdrAddResponse>.Success(new GdrAddResponse(staged.Value!.DraftName, staged.Value.Id, staged.Value.Abbreviation,
                staged.Value.ParentId, staged.Value.ListCreated, staged.Value.Fields, staged.Value.OperationCount, staged.Value.Warnings))
            : CommandOutcome<GdrAddResponse>.Refused(staged.Refusal!);
    }
}
