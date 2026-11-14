using SIL.LCModel;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.RichText;
using SIL.Motif.Host.Store;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>The Proposal a selector named and whether a person wrote after the builder last did.</summary>
public sealed record NoteFinding(string ProposalId, bool CommentsChanged);

/// <summary>
/// A Proposal's Rationale. It says what the Proposal changes and why, and whoever builds the Proposal writes it;
/// people and agents may rewrite it while the Proposal is open, and each rewrite is kept as a revision until the
/// decision. It is stamped with the intent digest it was written for and does not enter that digest, so writing it
/// never changes what the Proposal does. Comments, the person's replies, are in <see cref="ProposalComments"/>.
/// </summary>
public static class ProposalTexts
{
    /// <summary>The longest author label or person name a revision or Comment may carry.</summary>
    public const int MaxAuthorLength = 100;

    /// <summary>Writes a Proposal's Rationale, replacing the one there; allowed until the Proposal is closed.</summary>
    public static CommandOutcome<ProposalTextChangedResponse> SetRationale(SetRationaleRequest request) =>
        SetRationale(request, Check);

    /// <summary>Writes a Rationale with <paramref name="check"/> as its check, which a test may interleave a write with.</summary>
    internal static CommandOutcome<ProposalTextChangedResponse> SetRationale(SetRationaleRequest request, TextCheck check)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Write(request.FwDataPath, request.ProductVersion, request.ProposalId, request.Text,
            request.AuthorLabel, request.PersonName, request.ExpectedRationale, check);
    }

    /// <summary>A Rationale or Comment check against a project's store and Baseline.</summary>
    internal delegate CheckedProposalText TextCheck(MotifDatabase database, ProjectLocator project, string text);

    /// <summary>
    /// The kept versions of a Proposal's Rationale, newest first, which a person may restore while it is open; none
    /// once it is decided.
    /// </summary>
    public static CommandOutcome<IReadOnlyList<RationaleRevisionView>> RationaleRevisions(RationaleRevisionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            if (!CanonicalId.TryParse(request.ProposalId, out var id))
                return CommandOutcome<IReadOnlyList<RationaleRevisionView>>.Refused(Refuse("proposal.not-found",
                    FailureReason.NotFound, $"'{request.ProposalId}' is not a full Proposal id.", request.ProposalId));
            IReadOnlyList<RationaleRevisionView> revisions = new ProposalDiscussionRepository(database)
                .ListRationaleRevisions(id).Reverse()
                .Select(revision => new RationaleRevisionView(revision.RevisionId, revision.Text, revision.Author.Label,
                    revision.Author.PersonName, revision.CreatedUtc)).ToArray();
            return CommandOutcome<IReadOnlyList<RationaleRevisionView>>.Success(revisions);
        });
    }

    /// <summary>
    /// Whether a person wrote after the builder last did, for the Proposal a full id or a Draft name selects; null
    /// when the selector names no Proposal.
    /// </summary>
    public static NoteFinding? CommentsChangedFor(
        string fwDataPath, string productVersion, string selector)
    {
        var outcome = ProjectStoreCommand.Run(fwDataPath, productVersion, (database, _) =>
        {
            var repository = new ProposalRepository(database);
            ProposalRecord record;
            try
            {
                record = CanonicalId.TryParse(selector, out var id)
                    ? repository.Get(id)
                    : repository.DraftNameExists(selector) ? repository.GetDraft(selector)
                    : throw new KeyNotFoundException(selector);
            }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<NoteFinding?>.Success(null);
            }
            return CommandOutcome<NoteFinding?>.Success(new NoteFinding(record.ProposalId.Value,
                CommentsChanged(database, record)));
        });
        return outcome.Succeeded ? outcome.Value : null;
    }

    internal static bool CommentsChanged(MotifDatabase database, ProposalRecord record) =>
        ProposalDetailProjectionBuilder.PersonCommentIsNewer(ProposalRecordMapping.ToManifest(record,
            new ProposalDiscussionRepository(database).ListComments(record.ProposalId)));

    private static CommandOutcome<ProposalTextChangedResponse> Write(string fwDataPath, string productVersion,
        string proposalId, string text, string? authorLabel, string? personName, string? expectedRationale,
        TextCheck check)
    {
        return ProjectStoreCommand.Run(fwDataPath, productVersion, (database, project) =>
        {
            if (!CanonicalId.TryParse(proposalId, out var id))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.not-found",
                    FailureReason.NotFound, $"'{proposalId}' is not a full Proposal id. Use the proposalId a Proposal " +
                    "listing returns; prefixes and names are not accepted.", proposalId));
            if (string.IsNullOrWhiteSpace(text))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.rationale-empty",
                    FailureReason.InvalidArgument, "A Rationale cannot be empty. Say what the Proposal changes and why.",
                    id.Value));
            if (ProposalComments.AuthorProblem(authorLabel, personName) is { } problem)
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse(RefusalCodes.ProposalCommentInvalid,
                    FailureReason.InvalidArgument, problem, id.Value));
            var repository = new ProposalRepository(database);
            ProposalRecord record;
            try { record = repository.Get(id); }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.not-found",
                    FailureReason.NotFound, $"Proposal '{id.Value}' was not found.", id.Value));
            }
            if (record.DraftName is null && ProposalLifecycle.IsTerminal(record.Status))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.closed",
                    FailureReason.Refused, $"Proposal '{id.Value}' is {record.Status}, so its rationale can no longer change.",
                    id.Value));

            if (expectedRationale is not null && !string.Equals(record.Rationale ?? string.Empty, expectedRationale, StringComparison.Ordinal))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(ChangedWhileEditing(id));
            var checkedText = check(database, project, text);
            if (IsReady(record) && checkedText.ProjectUnavailable)
                return CommandOutcome<ProposalTextChangedResponse>.Refused(RationaleUnchecked(
                    $"Proposal '{id.Value}' is Ready and its Rationale links to the project, but the project's " +
                    "Baseline could not be read to check it, so it is not saved. Try again, or capture a Baseline first.",
                    ("proposalId", id.Value)));
            if (!checkedText.IsClean && IsReady(record))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(RationaleFindings(
                    $"Proposal '{id.Value}' is Ready, so a Rationale a reader would find wrong is not saved; the " +
                    "last clean one stays. Fix these and write it again:", checkedText.Findings,
                    ("proposalId", id.Value)));

            // Only onto the state the check was for: a Draft finalized meanwhile must not take unchecked findings.
            if (!repository.SetRationale(id, checkedText.Text, new DiscussionAuthor(authorLabel, personName ?? ""),
                    new RationaleWriteExpectation(record.Status, record.DraftName, expectedRationale)))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(expectedRationale is not null ? ChangedWhileEditing(id) : RationaleStale(
                    $"Proposal '{id.Value}' changed while its Rationale was being checked, so it was not saved. " +
                    "Write it again.", ("proposalId", id.Value)));
            var written = repository.Get(id);
            return CommandOutcome<ProposalTextChangedResponse>.Success(new ProposalTextChangedResponse(
                id.Value, "rationale", written.RationaleUpdatedUtc, CommentsChanged(database, written))
            {
                Findings = checkedText.Findings,
                Text = written.Rationale,
            });
        });
    }

    // Finalized and still open: a reviewer may be reading it, whether or not it has been reopened as a Draft.
    private static bool IsReady(ProposalRecord record) =>
        ProposalLifecycle.Parse(record.Status) is ProposalStatus.Proposed or ProposalStatus.Deferred;

    /// <summary>
    /// Checks a Rationale or Comment against the project's current Baseline, which is opened only when the text
    /// links to something or names a writing system or style. With no Baseline only the markup is checked, and a link
    /// must carry its GUID.
    /// </summary>
    /// <remarks>
    /// A Baseline that is recorded but cannot be read (its file gone or replaced, or not a project LibLCM can open)
    /// gives the markup-only check, marked <see cref="CheckedProposalText.ProjectUnavailable"/>.
    /// </remarks>
    internal static CheckedProposalText Check(MotifDatabase database, ProjectLocator project, string text)
    {
        if (!ProposalTextChecker.NeedsProject(text)) return ProposalTextChecker.Check(text, null, null);
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
        if (baseline is null) return ProposalTextChecker.Check(text, null, null);
        try
        {
            if (!File.Exists(baseline.FwDataPath)) throw new FileNotFoundException(null, baseline.FwDataPath);
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            return ProposalTextChecker.Check(text, reader.Cache, Path.GetFileNameWithoutExtension(project.FullFwDataPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or LcmInitializationException)
        {
            return ProposalTextChecker.Check(text, null, null) with { ProjectUnavailable = true };
        }
    }

    /// <summary>The refusal for a Rationale that needed the project when its Baseline could not be read.</summary>
    internal static Refusal RationaleUnchecked(string message, params (string Key, string Value)[] facts) =>
        new(RefusalCodes.ProposalRationaleUnchecked, FailureReason.Refused, message,
            facts.ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal));

    private static Refusal ChangedWhileEditing(CanonicalId id) => RationaleStale(
        $"The Rationale of Proposal '{id.Value}' was changed by someone else while this edit was being made, so the " +
        "edit was not saved. Read the current Rationale and make the edit again.", ("proposalId", id.Value));

    /// <summary>The refusal for a Rationale write or Finalize that found the Proposal changed since its check.</summary>
    internal static Refusal RationaleStale(string message, params (string Key, string Value)[] facts) =>
        new(RefusalCodes.ProposalRationaleStale, FailureReason.Refused, message,
            facts.ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal));

    /// <summary>The refusal that lists a Rationale's findings, one per line with its line and column.</summary>
    internal static Refusal RationaleFindings(string lead, IReadOnlyList<TextFinding> findings,
        params (string Key, string Value)[] facts)
    {
        var listed = string.Join('\n', findings.Select(finding =>
            $"{finding.Line}:{finding.Column} {finding.Id} {finding.Message}"));
        var allFacts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["findingCount"] = findings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["findings"] = listed,
        };
        foreach (var (key, value) in facts) allFacts[key] = value;
        return new Refusal(RefusalCodes.ProposalRationaleFindings, FailureReason.Refused, lead + '\n' + listed, allFacts);
    }

    internal static Refusal Refuse(string code, FailureReason reason, string message, string proposalId) =>
        new(code, reason, message, new Dictionary<string, string>(StringComparer.Ordinal) { ["proposalId"] = proposalId });
}
