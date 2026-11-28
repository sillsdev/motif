using SIL.Motif.Tests.Parser;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.LcmUtils;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Core.Text;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>The autonomous-apply cases that need a real PanGloss; they skip where none is staged. Kept apart from
/// <see cref="ApplyPromotionGatingAndSweepTests"/> so a class never mixes parser and deterministic tests.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class ApplyPromotionGatingRealParserTests : ApplyPromotionGatingFixtureBase
{
    public ApplyPromotionGatingRealParserTests(PristineProjectFixture pristine) : base(pristine)
    {
    }

    [RealParserTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAutonomousApplyWithAMessageAndNoForceSucceeds_AndTheLogShowsModeAndMessage(bool expectedLoss)
    {
        WriteGateConfiguration();
        var proposalId = FinalizeAndTrial("autonomous-ready", "autonomous ready gloss");
        RecordAssessment(proposalId, GetRecord(proposalId).IntentDigest!, "Correctness", ("alpha", true));
        RecordGate(proposalId, expectedLoss ? "covered-loss" : "green");

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Glossed from the corpus", WriteNote: true)));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(("autonomous", "Glossed from the corpus"),
            (result.Value!.AppliedLogEntry.Mode, result.Value.AppliedLogEntry.Message));
        var entry = Assert.Single(ProposalCommands.Log(new LogRequest(_fwDataPath)).Value!.Entries);
        Assert.Equal(("autonomous", "Glossed from the corpus"), (entry.Mode, entry.Description));
    }

    [RealParserFact]
    public void AnAutonomousApplyMarksAndThenClearsAMisspellingWithItsIntendedFormReadBack()
    {
        var wordformGuid = CreateWordform("motifmisspelt");
        WriteGateConfiguration();
        var wordform = CanonicalId.FromGuid(wordformGuid).Value;
        var tag = NewLangProjFixture.VernacularTag;

        var mark = FinalizeAndTrial("autonomous-misspelled", draft => Assert.True(
            ProposalCommands.ComposeEditAnalysisDecision(new(_fwDataPath, ProductVersion, draft, JsonSerializer.Serialize(new
            {
                decision = "misspelled", wordform,
                intendedForm = new { form = "motifmisspelled", writingSystem = tag },
                note = "The Bible text spells it this way throughout",
            }))).Succeeded));
        RecordAssessment(mark, GetRecord(mark).IntentDigest!, "Correctness", ("alpha", true));
        RecordGate(mark, "green");
        var marked = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, mark,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Mark the misspelling")));
        Assert.True(marked.Succeeded, marked.Refusal?.Message);
        Assert.Equal("autonomous", marked.Value!.AppliedLogEntry.Mode);
        Assert.Equal(2, SpellingStatusOf(wordformGuid));
        var shown = ProposalCommands.EvidenceShow(new(_fwDataPath, ProductVersion, wordform));
        var row = Assert.Single(shown.Value!.Records);
        Assert.Equal(("misspelled", WfiWordformSpellingStatusOperationKinds.SetSpellingStatus), (row.Tier, row.Kind));
        Assert.Equal("motifmisspelled", row.Detail["intendedForm"]);
        Assert.Equal(tag, row.Detail["intendedFormWritingSystem"]);
        Assert.Equal("The Bible text spells it this way throughout", row.Detail["note"]);

        WriteGateConfiguration();
        var clear = FinalizeAndTrial("autonomous-cleared", draft => Assert.True(
            ProposalCommands.ComposeEditAnalysisDecision(new(_fwDataPath, ProductVersion, draft, JsonSerializer.Serialize(new
            {
                decision = "clear-misspelled", wordform, note = "The form is attested in the lexicon",
            }))).Succeeded));
        RecordAssessment(clear, GetRecord(clear).IntentDigest!, "Correctness", ("alpha", true));
        RecordGate(clear, "green");
        var cleared = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, clear,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Clear the misspelling")));
        Assert.True(cleared.Succeeded, cleared.Refusal?.Message);
        Assert.Equal(0, SpellingStatusOf(wordformGuid));
        var history = ProposalCommands.EvidenceShow(new(_fwDataPath, ProductVersion, wordform)).Value!.Records;
        Assert.Equal(2, history.Count);
        Assert.Contains(history, record => record.Tier == "undecided" &&
            record.Kind == WfiWordformSpellingStatusOperationKinds.ClearSpellingStatus &&
            record.Detail["note"] == "The form is attested in the lexicon");
    }
}
