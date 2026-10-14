using System;
using System.Collections.Generic;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public class CanonicalIdByGuidTests
{
    [Fact]
    public void TwoSpellingsOfOneGuidAreOneObjectButStayDistinctIds()
    {
        var guid = Guid.NewGuid();
        var prefixed = CanonicalId.FromGuid(guid, "entry/");
        var bare = CanonicalId.FromGuid(guid);

        Assert.True(CanonicalId.ByGuid.Equals(prefixed, bare));
        Assert.Equal(CanonicalId.ByGuid.GetHashCode(prefixed), CanonicalId.ByGuid.GetHashCode(bare));
        Assert.NotEqual(prefixed, bare);
        Assert.False(CanonicalId.ByGuid.Equals(prefixed, CanonicalId.FromGuid(Guid.NewGuid(), "entry/")));
    }

    [Fact]
    public void AnEntityTheProposalCreatesIsLeftOutOfTheFootprintWhateverPrefixItsEditorUses()
    {
        var guid = Guid.NewGuid();
        // The editor comes first in the array: exclusion is by identity, never by position.
        var editor = new OperationEnvelope(CanonicalId.Mint(), "lexical/lexEntry/setCitationForm",
            target: CanonicalId.FromGuid(guid));
        var creator = new OperationEnvelope(CanonicalId.Mint(), "lexical/lexDb/createEntries",
            entityId: CanonicalId.FromGuid(guid, "entry/"), target: CanonicalId.Mint());
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null,
            [editor, creator]);

        var minted = FootprintPlan.TargetsMintedWithinProposal(proposal);

        Assert.False(FootprintPlan.ParticipatesInFootprint(editor, minted));
    }
}
