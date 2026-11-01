using System.Collections.Generic;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Contract.Responses;

/// <summary>The named non-human agent whose approval or disapproval the evidence supports.</summary>
/// <param name="Id">The agent's authored identity.</param>
/// <param name="Name">The agent's name, such as a source validation program.</param>
/// <param name="Version">The source release the agent stands for.</param>
/// <param name="Opinion"><c>approves</c> or <c>disapproves</c>, as staged.</param>
public sealed record DecisionEvidenceAgent(string Id, string Name, string Version, string Opinion);

/// <summary>One source-validated decision's evidence, read from the Applied Proposal that staged it.</summary>
/// <param name="ProposalId">The Applied Proposal holding the operation.</param>
/// <param name="ProposalLabel">The Proposal's short description, when it has one.</param>
/// <param name="OperationId">The operation's identity within the Proposal.</param>
/// <param name="Kind">The operation kind the evidence explains.</param>
/// <param name="Subject">The wordform or analysis the evidence is about: the operation's target, else the entity it created.</param>
/// <param name="Tier">One of <see cref="DecisionEvidence.Tiers"/>.</param>
/// <param name="Sources">The sources, in the order staged.</param>
/// <param name="Detail">The tier's facts by name.</param>
/// <param name="StagedBy">The staging tool and model as provenance text, when given.</param>
/// <param name="Agent">The non-human agent that decided, for an agent evaluation; <c>null</c> for a record that decides nothing.</param>
public sealed record DecisionEvidenceRow(
    string ProposalId,
    string? ProposalLabel,
    string OperationId,
    string Kind,
    string Subject,
    string Tier,
    IReadOnlyList<DecisionEvidenceSource> Sources,
    IReadOnlyDictionary<string, string> Detail,
    string? StagedBy,
    DecisionEvidenceAgent? Agent = null)
{
    /// <summary>Always <c>source-validated</c>: the decision was staged by a program from cited sources, not by a person.</summary>
    public string Label => "source-validated";

    /// <summary>Always <c>false</c>: no person made this decision.</summary>
    public bool Human => false;
}

/// <summary>The <c>evidence show</c> result: every record about one wordform or analysis, in Proposal id order.</summary>
/// <param name="Subject">The identity asked about.</param>
/// <param name="Records">The records; empty when nothing was staged for it.</param>
public sealed record DecisionEvidenceShowResponse(string Subject, IReadOnlyList<DecisionEvidenceRow> Records);

/// <summary>The <c>evidence list</c> result: counts first, then one page of rows.</summary>
/// <param name="AppliedProposals">How many Applied Proposals were read.</param>
/// <param name="Total">How many records match the filter.</param>
/// <param name="TierCounts">Matching records per tier.</param>
/// <param name="Offset">The first row of the page.</param>
/// <param name="Limit">The page size asked for.</param>
/// <param name="Rows">The page.</param>
public sealed record DecisionEvidenceListResponse(
    int AppliedProposals,
    int Total,
    IReadOnlyDictionary<string, int> TierCounts,
    int Offset,
    int Limit,
    IReadOnlyList<DecisionEvidenceRow> Rows);

/// <summary>One page of the <c>evidence export</c> read: rows in a fixed order, and where the next page starts.</summary>
/// <param name="Format">Always <see cref="DecisionEvidenceExport.Format"/>, so a saved file names its own layout.</param>
/// <param name="Rows">At most the requested number of rows, in Proposal id order.</param>
/// <param name="Next">The cursor to pass as <c>after</c> for the following page; <c>null</c> when this page ends the export.</param>
public sealed record DecisionEvidenceExportResponse(string Format, IReadOnlyList<DecisionEvidenceRow> Rows, string? Next);

/// <summary>What <c>evidence export --out</c> reports after writing the file.</summary>
/// <param name="Path">The file written.</param>
/// <param name="Total">How many records it holds.</param>
/// <param name="TierCounts">Records per tier.</param>
public sealed record DecisionEvidenceExportSummary(
    string Path, int Total, IReadOnlyDictionary<string, int> TierCounts);

/// <summary>The layout of an exported evidence file.</summary>
public static class DecisionEvidenceExport
{
    /// <summary>
    /// The file's <c>format</c> value. The file is one JSON object: <c>format</c>, then <c>rows</c> (each a
    /// <see cref="DecisionEvidenceRow"/>), then <c>total</c> and <c>tierCounts</c>, which come last because they are
    /// counted while the rows stream out.
    /// </summary>
    public const string Format = "motif-decision-evidence/1";
}
