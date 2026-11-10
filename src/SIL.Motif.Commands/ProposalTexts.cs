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
    public static CommandOutcome<ProposalTextChangedResponse> SetRationale(SetRationaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Write(request.FwDataPath, request.ProductVersion, request.ProposalId, request.Text,
            request.AuthorLabel, request.PersonName);
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
        string proposalId, string text, string? authorLabel, string? personName)
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

            var checkedText = Check(database, project, text);
            if (!checkedText.IsClean && IsReady(record))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(RationaleFindings(
                    $"Proposal '{id.Value}' is Ready, so a Rationale a reader would find wrong is not saved; the " +
                    "last clean one stays. Fix these and write it again:", checkedText.Findings,
                    ("proposalId", id.Value)));

            repository.SetRationale(id, checkedText.Text, new DiscussionAuthor(authorLabel, personName ?? ""));
            var written = repository.Get(id);
            return CommandOutcome<ProposalTextChangedResponse>.Success(new ProposalTextChangedResponse(
                id.Value, "rationale", written.RationaleUpdatedUtc, CommentsChanged(database, written))
            {
                Findings = checkedText.Findings,
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
    internal static CheckedProposalText Check(MotifDatabase database, ProjectLocator project, string text)
    {
        if (!ProposalTextChecker.NeedsProject(text)) return ProposalTextChecker.Check(text, null, null);
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
        if (baseline is null || !File.Exists(baseline.FwDataPath)) return ProposalTextChecker.Check(text, null, null);
        using var reader = BaselineReadCache.Open(baseline.FwDataPath);
        return ProposalTextChecker.Check(text, reader.Cache, Path.GetFileNameWithoutExtension(project.FullFwDataPath));
    }

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
