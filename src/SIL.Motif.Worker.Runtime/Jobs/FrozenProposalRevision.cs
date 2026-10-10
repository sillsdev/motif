using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using ProposalIntentDigest = SIL.Motif.Contract.Canonicalization.IntentDigest;

namespace SIL.Motif.Worker.Jobs;

/// <summary>One immutable Proposal document captured for future Dry Run preparation.</summary>
public sealed record FrozenProposalRevision(string ProposalId, string IntentDigest, string ProposalJson,
    string ContentSha256)
{
    public static FrozenProposalRevision Create(string proposalJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalJson);
        var proposal = ProposalJsonParser.Parse(proposalJson);
        return new FrozenProposalRevision(proposal.ProposalId.Value, ProposalIntentDigest.Compute(proposal), proposalJson,
            Digest(Encoding.UTF8.GetBytes(proposalJson)));
    }

    public Proposal Validate()
    {
        var proposal = ProposalJsonParser.Parse(ProposalJson);
        if (proposal.ProposalId.Value != ProposalId || ProposalIntentDigest.Compute(proposal) != IntentDigest ||
            Digest(Encoding.UTF8.GetBytes(ProposalJson)) != ContentSha256)
            throw new InvalidDataException("Frozen Proposal material does not match its recorded identity.");
        return proposal;
    }

    internal static string Digest(ReadOnlySpan<byte> bytes) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}
