using System.Collections.Immutable;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class TrialReadingDeltaTests
{
    private static string Id(int value) => CanonicalId.FromGuid(new Guid(value, 0, 0, [1, 2, 3, 4, 5, 6, 7, 8])).Value;

    private static ReadingSignature Sig(int number, bool stem = true) => new([new ReadingMorpheme(Id(number), Id(900001), [],
        ReadingSenseStatus.Resolved, Id(number + 100), Id(number + 200), null, stem, null,
        [new ReadingFeatureValue(ReadingFeatureRole.Inflection, [Id(7)], [Id(9), Id(8)])])]);

    private static ProposalCompareExports.FormSpec Form(string form, int[] before, int[] after) =>
        new(form, [], []) { BeforeSignatures = [.. before.Select(item => Sig(item))], AfterSignatures = [.. after.Select(item => Sig(item))] };

    private static string Publish(string project, params ProposalCompareExports.FormSpec[] forms)
    {
        var id = "";
        var result = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, located) =>
        {
            id = ProposalCompareExports.AddTrial(database, located, forms);
            ProposalCompareExports.RecordCurrentBaseline(database, located);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return id;
    }

    [Fact]
    public void LostGainedChangedAndDoubledAreClassifiedFromTheRetainedEvidence()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("lost", [1], []), Form("gained", [], [2]), Form("changed", [3], [4]),
            Form("doubled", [5], [5, 6]), Form("partial", [7, 7], [7]), Form("same", [8], [8]));

        var result = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var delta = result.Value!;
        Assert.Equal(new ReadingDeltaCounts(6, 5, 1, 1, 1, 1, 0, 3, 3, 0, 0), delta.Counts);
        Assert.Equal(["lost"], delta.Classes.Single(item => item.Class == "lost").Rows.Select(row => row.Word));
        Assert.Equal(["doubled"], delta.Classes.Single(item => item.Class == "doubled").Rows.Select(row => row.Word));
        var changed = delta.Classes.Single(item => item.Class == "changed").Rows.Single();
        Assert.Equal(Sig(3).Id, changed.Retired.Single().Id);
        Assert.Equal(Sig(4).Id, changed.Added.Single().Id);
        Assert.StartsWith("case/", changed.CaseKey);
    }

    private static ReadingSignature AmbiguousSig(int number) => new([new ReadingMorpheme(Id(number), null, [Id(900001), Id(900002)],
        ReadingSenseStatus.Ambiguous, Id(number + 100), Id(number + 200), null, true, null,
        [new ReadingFeatureValue(ReadingFeatureRole.Inflection, [Id(7)], [Id(9), Id(8)])])]);

    [Fact]
    public void AFormWhoseSenseBecameAmbiguousCountsAsOneChangeNotALossAndAGain()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var become = new ProposalCompareExports.FormSpec("become", [], []) { BeforeSignatures = [Sig(1)], AfterSignatures = [AmbiguousSig(1)] };
        var proposal = Publish(fixture.Project, become, Form("other", [3], [4]));

        var delta = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal)).Value!;

        Assert.NotEqual(Sig(1).Id, AmbiguousSig(1).Id);
        Assert.Equal(Sig(1).SenseFree(), AmbiguousSig(1).SenseFree());
        Assert.Equal(4, delta.Counts.RetiredReadings + delta.Counts.AddedReadings);
        Assert.Equal(1, delta.Counts.SenseChanged);
        Assert.Equal(3, delta.Counts.ReadingChanges);
        var row = delta.Classes.Single(item => item.Class == "changed").Rows.Single(item => item.Word == "become");
        Assert.Equal(1, row.SenseChanged);
        Assert.Equal(Sig(1).Id, row.Retired.Single().Id);
        Assert.Equal(AmbiguousSig(1).Id, row.Added.Single().Id);
        Assert.Equal(0, delta.Classes.Single(item => item.Class == "changed").Rows.Single(item => item.Word == "other").SenseChanged);
    }

    private static ReadingSignature SenseSig(int number, int sense) => new([new ReadingMorpheme(Id(number), Id(sense), [],
        ReadingSenseStatus.Resolved, Id(number + 100), Id(number + 200), null, true, null,
        [new ReadingFeatureValue(ReadingFeatureRole.Inflection, [Id(7)], [Id(9), Id(8)])])]);

    [Fact]
    public void SeveralReadingsOfOneSenseFreeIdentityPairOneToOneAndTheRestStayUnpaired()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var form = new ProposalCompareExports.FormSpec("many", [], [])
        {
            BeforeSignatures = [SenseSig(1, 900001), SenseSig(1, 900002), SenseSig(1, 900003)],
            AfterSignatures = [AmbiguousSig(1), AmbiguousSig(1)]
        };
        var proposal = Publish(fixture.Project, form);

        var delta = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal)).Value!;

        Assert.Equal(3, delta.Counts.RetiredReadings);
        Assert.Equal(2, delta.Counts.AddedReadings);
        Assert.Equal(2, delta.Counts.SenseChanged);
        Assert.Equal(3, delta.Counts.ReadingChanges);
        Assert.Equal(2, delta.Classes.Single(item => item.Class == "changed").Rows.Single().SenseChanged);
    }

    [Fact]
    public void ReadingsThatDifferBeyondSenseAreNotPaired()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, new ProposalCompareExports.FormSpec("apart", [], [])
            { BeforeSignatures = [Sig(1)], AfterSignatures = [AmbiguousSig(2)] });

        var delta = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal)).Value!;

        Assert.Equal(0, delta.Counts.SenseChanged);
        Assert.Equal(2, delta.Counts.ReadingChanges);
    }

    [Fact]
    public void SiblingsAreReportedSeparatelyAndJudgedByTheSameExpectations()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var forms = new[] { Form("gold", [1], [1]), Form("kuish", [2], []), Form("kuishe", [3], [3, 4]), Form("kuishi", [5], [5]) };
        var proposal = "";
        var result = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, located) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, located, forms, siblings: ["kuish", "kuishe", "kuishi"]);
            ProposalCompareExports.RecordCurrentBaseline(database, located);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);

        var delta = TrialReadingDelta.Read(request).Value!;
        var siblings = delta.Siblings!;

        Assert.Equal(3, siblings.Sampled);
        Assert.Equal(3, siblings.Measured);
        Assert.Equal(1, siblings.Lost);
        Assert.Equal(["kuish"], siblings.LostSample);
        Assert.Equal(1, siblings.Doubled);
        Assert.Equal(["kuishe"], siblings.DoubledSample);
        Assert.Equal((42L, 1000, "per-form"), (siblings.Seed, siblings.Max, siblings.Pattern));
        Assert.Equal(["ish"], siblings.TouchedForms);
        var directory = Path.Combine(fixture.Root, "manifests");
        TrialReadingDelta.ReadAndWrite(request, directory);
        var green = TrialReadingDelta.Check(request, new TrialExpectations(Rows(directory, "expect-retired.jsonl"),
            Rows(directory, "expect-added.jsonl"))).Value!;
        Assert.Equal(ReadingExpectationStates.Green, green.Expectations!.State);
        Assert.Equal(1, green.Siblings!.Lost);
        // A sibling lost with no manifest row is a mismatch, and no new state is added for it.
        var lostUndeclared = Rows(directory, "expect-retired.jsonl").Where(line => !line.Contains("\"kuish\"")).ToArray();
        var gate = TrialReadingDelta.Check(request, new TrialExpectations(lostUndeclared, Rows(directory, "expect-added.jsonl")))
            .Value!.Expectations!;
        Assert.Equal(ReadingExpectationStates.Mismatch, gate.State);
        Assert.Contains(gate.Mismatches, item => item.Word == "kuish");
        // A doubled sibling counts toward the doubled cap.
        var capped = TrialReadingDelta.Check(request, new TrialExpectations(Rows(directory, "expect-retired.jsonl"),
            Rows(directory, "expect-added.jsonl"), MaxDoubled: 0)).Value!.Expectations!;
        Assert.Equal(ReadingExpectationStates.DoubledOverCap, capped.State);
    }

    [Fact]
    public void ALostSiblingNotDeclaredRetiredWritesNoReceiptAndRemovesAnEarlierOne()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var forms = new[] { Form("gold", [1], [1]), Form("kuish", [2], []), Form("kuishi", [5], [5]) };
        var proposal = "";
        var stored = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, located) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, located, forms, siblings: ["kuish", "kuishi"]);
            ProposalCompareExports.RecordCurrentBaseline(database, located);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);
        var directory = Path.Combine(fixture.Root, "manifests");
        TrialReadingDelta.ReadAndWrite(request, directory);
        var path = Path.Combine(fixture.Root, "receipt.json");
        var declared = Declared(directory);
        Assert.NotNull(TrialReadingDelta.Check(request, declared, receiptPath: path).Value!.Receipt);

        var undeclared = new TrialExpectations([.. declared.RetiredRows!.Where(line => !line.Contains("\"kuish\""))], declared.AddedRows);
        var result = TrialReadingDelta.Check(request, undeclared, receiptPath: path);

        Assert.Equal(ReadingExpectationStates.Mismatch, result.Value!.Expectations!.State);
        Assert.Null(result.Value.Receipt);
        Assert.False(File.Exists(path));
    }

    private static string TrialWithSiblings(ReviewAcceptanceFixture fixture, string[] sampled, bool purgeList = false)
    {
        var proposal = "";
        var result = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, located) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, located, [Form("gold", [1], [1]), Form("kuish", [2], [2])], siblings: sampled);
            ProposalCompareExports.RecordCurrentBaseline(database, located);
            if (purgeList)
                foreach (var file in Directory.GetFiles(Path.GetFullPath(database.FullPath) + ".job-inputs")) File.Delete(file);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return proposal;
    }

    [Fact]
    public void ASampledSiblingTheTrialDidNotMeasureMakesTheExpectationsUnmeasuredNotGreen()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: TrialWithSiblings(fixture, ["kuish", "absent", "gone"]));

        var delta = TrialReadingDelta.Check(request, new TrialExpectations([], [])).Value!;

        Assert.Equal(ReadingExpectationStates.Unmeasured, delta.Expectations!.State);
        Assert.Contains("2 of 3 sampled siblings were not measured", delta.Expectations.Reason);
        Assert.Contains("siblings-unmeasured", delta.Limitations);
        Assert.Equal(1, delta.Siblings!.Measured);
    }

    [Fact]
    public void ASiblingRecordThatCannotBeReadIsReportedNotTakenForNoSiblings()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: TrialWithSiblings(fixture, ["kuish"], purgeList: true));

        var delta = TrialReadingDelta.Check(request, new TrialExpectations([], [])).Value!;

        Assert.Null(delta.Siblings);
        Assert.Contains("siblings-unreadable", delta.Limitations);
        Assert.Equal(ReadingExpectationStates.Unmeasured, delta.Expectations!.State);
        Assert.Contains("record could not be read", delta.Expectations.Reason);
    }

    [Fact]
    public void ATrialThatSampledNoSiblingsCarriesNoSiblingsBlock()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("lost", [1], []));

        Assert.Null(TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal)).Value!.Siblings);
    }

    [Fact]
    public void ManifestsMatchTheLaneFormatAndEnumerateTheWholeDelta()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("lost", [1], []), Form("changed", [3], [4]), Form("partial", [7, 7], [7]));
        var directory = Path.Combine(fixture.Root, "manifests");

        var result = TrialReadingDelta.ReadAndWrite(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal), directory);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var retired = File.ReadAllLines(Path.Combine(directory, "expect-retired.jsonl"));
        Assert.Equal(["changed", "lost", "partial"], retired.Select(line => line[10..line.IndexOf('"', 10)]));
        Assert.Equal(ReadingManifestFormat.Row("partial", Sig(7), 1), retired[2]);
        Assert.EndsWith("\"count\": 1}", retired[2]);
        Assert.Single(File.ReadAllLines(Path.Combine(directory, "expect-added.jsonl")));
        Assert.Equal(3, result.Value!.Manifests[0].Rows);
        Assert.Equal(1, result.Value.Manifests[1].Rows);
        Assert.All(retired, line => Assert.True(ReadingManifestFormat.TryParseRow(line, out _, out _, out _)));
    }

    [Fact]
    public void AReadingWithoutACompleteIdentityRefusesManifestsAndIsCounted()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var partial = new ProposalCompareExports.FormSpec("odd", [], []) { BeforeSignatures = [ProposalCompareExports.Signature(5)], AfterSignatures = [] };
        var proposal = Publish(fixture.Project, partial, Form("lost", [1], []));
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);

        Assert.Equal(1, TrialReadingDelta.Read(request).Value!.Counts.Unmeasured);
        var refused = TrialReadingDelta.ReadAndWrite(request, Path.Combine(fixture.Root, "none"));

        Assert.Equal(RefusalCodes.TrialDeltaIncomplete, refused.Refusal?.Code);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "none")));
    }

    private static string[] Rows(string directory, string file) => File.ReadAllLines(Path.Combine(directory, file));

    private static string ProposalWithAllClasses(ReviewAcceptanceFixture fixture) => Publish(fixture.Project,
        Form("lost", [1], []), Form("changed", [3], [4]), Form("doubled", [5], [5, 6]), Form("same", [8], [8]));

    [Fact]
    public void ManifestsWrittenFromATrialHoldThatTrialGreen()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = ProposalWithAllClasses(fixture);
        var directory = Path.Combine(fixture.Root, "manifests");
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);
        Assert.True(TrialReadingDelta.ReadAndWrite(request, directory).Succeeded);

        var result = TrialReadingDelta.Check(request, new TrialExpectations(Rows(directory, "expect-retired.jsonl"),
            Rows(directory, "expect-added.jsonl")));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var gate = result.Value!.Expectations!;
        Assert.Equal(ReadingExpectationStates.Green, gate.State);
        Assert.Null(gate.Reason);
        Assert.Equal(0, gate.MismatchedWords);
        Assert.Equal(1, gate.Doubled);
        Assert.Null(TrialReadingDelta.Read(request).Value!.Expectations);
    }

    [Fact]
    public void ADifferentManifestNamesTheUnexpectedAndMissingReadingsPerWord()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = ProposalWithAllClasses(fixture);
        var directory = Path.Combine(fixture.Root, "manifests");
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);
        TrialReadingDelta.ReadAndWrite(request, directory);
        // Drop one retirement, declare one never made, and miscount an addition.
        var retired = Rows(directory, "expect-retired.jsonl").Where(line => !line.Contains("\"lost\"")).Append(
            ReadingManifestFormat.Row("same", Sig(8), 1)).ToArray();
        var added = Rows(directory, "expect-added.jsonl").Select(line => line.Replace("\"count\": 1}", "\"count\": 2}")).ToArray();

        var gate = TrialReadingDelta.Check(request, new TrialExpectations(retired, added)).Value!.Expectations!;

        Assert.Equal(ReadingExpectationStates.Mismatch, gate.State);
        Assert.Equal(["added changed", "added doubled", "retired lost", "retired same"],
            gate.Mismatches.Select(item => item.Kind + " " + item.Word));
        Assert.Equal(gate.Mismatches.Count, gate.MismatchedWords);
        Assert.Single(gate.Mismatches.Single(item => item.Kind + item.Word == "retiredlost").Unexpected);
        Assert.Single(gate.Mismatches.Single(item => item.Kind + item.Word == "retiredsame").Missing);
        var wrongCount = gate.Mismatches.Single(item => item.Kind + item.Word == "addedchanged");
        Assert.Equal(1, wrongCount.Unexpected.Single().Count);
        Assert.Equal(2, wrongCount.Missing.Single().Count);
    }

    [Fact]
    public void ADoubledCapFailsEvenWhenTheAdditionIsDeclaredAndAFractionCapUsesTheParsedBeforeWords()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = ProposalWithAllClasses(fixture);
        var directory = Path.Combine(fixture.Root, "manifests");
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);
        TrialReadingDelta.ReadAndWrite(request, directory);
        var retired = Rows(directory, "expect-retired.jsonl");
        var added = Rows(directory, "expect-added.jsonl");

        TrialExpectations With(long? count, double? fraction) => new(retired, added, count, fraction);
        Assert.Equal(ReadingExpectationStates.Green, TrialReadingDelta.Check(request, With(1, 0.25)).Value!.Expectations!.State);
        var overCount = TrialReadingDelta.Check(request, With(0, null)).Value!.Expectations!;
        Assert.Equal(ReadingExpectationStates.DoubledOverCap, overCount.State);
        Assert.True(overCount.DoubledOverCap);
        Assert.Equal(["doubled"], overCount.DoubledSample);
        Assert.Equal("doubled 1 > cap --max-doubled 0", overCount.Reason);
        var overFraction = TrialReadingDelta.Check(request, With(null, 0.2)).Value!.Expectations!;
        Assert.Equal(4, overFraction.DoubledParsedBefore);
        Assert.Equal(ReadingExpectationStates.DoubledOverCap, overFraction.State);
        Assert.Equal("doubled 1 > cap --max-doubled-frac 0.2 x 4 = 0.8", overFraction.Reason);
        Assert.Equal(ReadingExpectationStates.Green, TrialReadingDelta.Check(request, With(null, 0.25)).Value!.Expectations!.State);
    }

    [Fact]
    public void CapsAloneNeedNoManifestAndAMismatchStillReportsTheCap()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: ProposalWithAllClasses(fixture));

        var capOnly = TrialReadingDelta.Check(request, new TrialExpectations(MaxDoubled: 5)).Value!.Expectations!;
        var both = TrialReadingDelta.Check(request, new TrialExpectations(RetiredRows: [], MaxDoubled: 0)).Value!.Expectations!;

        Assert.Equal(ReadingExpectationStates.Green, capOnly.State);
        Assert.Equal(ReadingExpectationStates.Mismatch, both.State);
        Assert.True(both.DoubledOverCap);
        Assert.Contains("--max-doubled 0", both.Reason);
    }

    [Fact]
    public void AKnownNonWordThatStoppedParsingNeedsNoRowAndAnUnmeasuredFormMakesTheStateUnmeasured()
    {
        var copy = new ReadingCopy(Sig(1).Id, Sig(1));
        var forms = ReadingDeltaBuilder.Compute([new ReadingDeltaInput("bad", "case/0", true, [copy], [])]);
        Assert.Contains(ReadingDeltaClasses.Rejected, forms.Single().Classes);
        var counts = new ReadingDeltaCounts(1, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0);
        Assert.Equal(ReadingExpectationStates.Green, TrialExpectationCheck.Evaluate(forms, counts, new TrialExpectations([], []),
            [], [], null, false).State);

        using var other = new ReviewAcceptanceFixture();
        var odd = new ProposalCompareExports.FormSpec("odd", [], []) { BeforeSignatures = [ProposalCompareExports.Signature(5)], AfterSignatures = [] };
        var unmeasured = Publish(other.Project, odd, Form("lost", [1], []));
        var held = TrialReadingDelta.Check(new ReadTrialDeltaRequest(other.Project, ProposalId: unmeasured), new TrialExpectations([], []));
        Assert.Equal(ReadingExpectationStates.Unmeasured, held.Value!.Expectations!.State);
    }

    [Fact]
    public void ManifestWordsTheTrialDidNotMeasureAreCountedAndIgnoredAndBadInputIsRefused()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: Publish(fixture.Project, Form("same", [8], [8])));

        var ignored = TrialReadingDelta.Check(request, new TrialExpectations([ReadingManifestFormat.Row("elsewhere", Sig(1), 1)], [])).Value!.Expectations!;
        Assert.Equal(ReadingExpectationStates.Green, ignored.State);
        Assert.Equal(1, ignored.ManifestWordsNotMeasured);
        var row = ReadingManifestFormat.Row("same", Sig(1), 1);
        foreach (var bad in new[] { new TrialExpectations(["not json"]), new TrialExpectations([row, row]),
                     new TrialExpectations(MaxDoubled: -1), new TrialExpectations(MaxDoubledFrac: 1.5) })
            Assert.Equal(RefusalCodes.TrialDeltaRequestInvalid, TrialReadingDelta.Check(request, bad).Refusal?.Code);
    }

    private static void Change(string project, Action<SIL.Motif.Host.Store.MotifDatabase, SIL.Motif.Contract.Projects.ProjectLocator> action)
    {
        var result = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, located) =>
        {
            action(database, located);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
    }

    private static ReadingExpectationResult Held(string project, string proposal) =>
        TrialReadingDelta.Check(new ReadTrialDeltaRequest(project, ProposalId: proposal), new TrialExpectations([], [])).Value!.Expectations!;

    [Fact]
    public void ATrialOfAnOlderProposalRevisionNeverReadsGreen()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("same", [8], [8]));
        Assert.Equal(ReadingExpectationStates.Green, Held(fixture.Project, proposal).State);

        Change(fixture.Project, (database, _) => ProposalCompareExports.SaveProposal(database, proposal,
            ProposalCompareExports.OtherProposalJson(proposal)));

        var gate = Held(fixture.Project, proposal);
        Assert.Equal(ReadingExpectationStates.Unmeasured, gate.State);
        Assert.Contains("measured Proposal revision", gate.Reason);
        Assert.Contains("the Proposal is now", gate.Reason);
    }

    [Fact]
    public void ATrialOnAnOlderBaselineOrWithNoCurrentBaselineNeverReadsGreen()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("same", [8], [8]));

        Change(fixture.Project, (database, located) => ProposalCompareExports.RecordCurrentBaseline(database, located, 'b'));

        var gate = Held(fixture.Project, proposal);
        Assert.Equal(ReadingExpectationStates.Unmeasured, gate.State);
        Assert.Contains(ProposalCompareExports.BaselineDigest('a'), gate.Reason);
        Assert.Contains(ProposalCompareExports.BaselineDigest('b'), gate.Reason);

        using var bare = new ReviewAcceptanceFixture();
        var id = "";
        Change(bare.Project, (database, located) => id = ProposalCompareExports.AddTrial(database, located, [Form("same", [8], [8])]));
        Assert.Equal(ReadingExpectationStates.Unmeasured, Held(bare.Project, id).State);
    }

    [Fact]
    public void ATrialThatMeasuredNoFormReadsUnmeasuredNotGreen()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project);

        var gate = Held(fixture.Project, proposal);

        Assert.Equal(ReadingExpectationStates.Unmeasured, gate.State);
        Assert.Contains("no form", gate.Reason);
    }

    [Fact]
    public void RequestsThatNameNoTrialOrAnUnknownClassAreRefused()
    {
        using var fixture = new ReviewAcceptanceFixture();
        Assert.Equal(RefusalCodes.TrialDeltaRequestInvalid, TrialReadingDelta.Read(new(fixture.Project)).Refusal?.Code);
        Assert.Equal(RefusalCodes.TrialDeltaRequestInvalid,
            TrialReadingDelta.Read(new(fixture.Project, JobId: "job/x", Classes: ["nope"])).Refusal?.Code);
    }

    private static string G(int number) => CanonicalId.FromGuid(Guid.Parse($"{number:x8}-0000-0000-0102-030405060708")).Value;

    private static ReadingMorpheme Morph(int entry, int sense, int msa, int allomorph, bool stem = true, int? inflection = null,
        (int, int)? derivation = null, ReadingFeatureValue[]? features = null, int[]? candidates = null) =>
        new(G(entry), candidates is null ? G(sense) : null, candidates is null ? [] : [.. candidates.Select(G)],
            candidates is null ? ReadingSenseStatus.Resolved : ReadingSenseStatus.Ambiguous, G(msa), G(allomorph),
            inflection is { } type ? G(type) : null, stem, derivation is { } pair ? new ReadingDerivation(G(pair.Item1), G(pair.Item2)) : null,
            [.. features ?? []]);

    private static ReadingFeatureValue Feature(ReadingFeatureRole role, int[] path, int[] values) =>
        new(role, [.. path.Select(G)], [.. values.Select(G)]);

    private static string Rig(string name)
    {
        using var stream = typeof(TrialReadingDeltaTests).Assembly.GetManifestResourceStream("RigManifests." + name)!;
        return new StreamReader(stream).ReadToEnd();
    }

    // Ids a run mints differ every time; the salt stands in for a second run's fresh GUIDs.
    private static string Salted(int number, int salt) =>
        CanonicalId.FromGuid(Guid.Parse($"{number:x8}-{salt:x4}-0000-0102-030405060708")).Value;

    private static string ManifestsOfOneRun(string root, string name, int salt, bool aliasCreated = true)
    {
        string G(int number) => number >= 500 ? Salted(number, salt) : Salted(number, 0);
        ReadingMorpheme Morph(int entry, int sense, int msa, int allomorph, int[]? candidates = null,
            ReadingFeatureValue[]? features = null) =>
            new(G(entry), candidates is null ? G(sense) : null, candidates is null ? [] : [.. candidates.Select(G)],
                candidates is null ? ReadingSenseStatus.Resolved : ReadingSenseStatus.Ambiguous, G(msa), G(allomorph),
                null, true, null, [.. features ?? []]);
        // Ids 1 to 3 belong to the project; 501 and up are what the Proposal creates.
        var withNewSense = new ReadingSignature([Morph(501, 504, 503, 502,
            features: [new(ReadingFeatureRole.Inflection, [G(508)], [G(509), G(1)])])]);
        var withAlternate = new ReadingSignature([Morph(501, 504, 503, 506)]);
        var onOldEntry = new ReadingSignature([Morph(1, 505, 2, 507)]);
        var ambiguous = new ReadingSignature([Morph(1, 0, 2, 3, candidates: [505, 1])]);
        (string, int)[] created =
        [
            ("lexical/lexDb/createEntries", 501), ("lexical/lexEntry/createLexemeForm", 502),
            ("lexical/lexEntry/createMorphoSyntaxAnalyses", 503), ("lexical/lexEntry/createSenses", 504),
            ("lexical/lexEntry/createSenses", 505), ("lexical/lexEntry/createAlternateForms", 506),
            ("lexical/lexEntry/createAlternateForms", 507), ("lists/cmPossibilityList/createPossibilities", 508),
            ("lists/cmPossibilityList/createPossibilities", 509),
        ];
        // A run may emit its operations in any spelling and with its own operation ids; only the entity ids matter.
        var operations = created.Select(item => (object)new
        {
            operationId = CanonicalId.Mint("operation/").Value, kind = item.Item1, entityId = G(item.Item2), after = new { },
        }).ToArray();
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = "";
        var published = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, located) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, located,
            [
                new("kata", [], []) { BeforeSignatures = [], AfterSignatures = [withNewSense, withAlternate] },
                new("pata", [], []) { BeforeSignatures = [onOldEntry], AfterSignatures = [ambiguous] },
            ], operations: operations);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(published.Succeeded, published.Refusal?.Message);
        var directory = Path.Combine(root, name);
        var result = TrialReadingDelta.ReadAndWrite(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal), directory,
            new ManifestOptions(AliasCreated: aliasCreated));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return File.ReadAllText(Path.Combine(directory, "expect-retired.jsonl")) + "\n" +
            File.ReadAllText(Path.Combine(directory, "expect-added.jsonl"));
    }

    [Fact]
    public void TheSameProposalRunTwiceWithFreshIdsWritesByteIdenticalManifests()
    {
        var root = Path.Combine(Path.GetTempPath(), "manifest-runs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = ManifestsOfOneRun(root, "one", salt: 0x1111);
            var second = ManifestsOfOneRun(root, "two", salt: 0x2222);

            Assert.Equal(first, second);
            // Every object the Proposal creates, in every field a row names it, is a label and never a minted id.
            foreach (var label in new[] { "created:0", "created:1", "created:2", "created:3", "created:4", "created:5",
                "created:6", "created:7", "created:8" })
                Assert.Contains(label, first);
            Assert.All(first.Split('\n', StringSplitOptions.RemoveEmptyEntries), line =>
                Assert.DoesNotContain(Salted(504, 0x1111), line));

            var rawOne = ManifestsOfOneRun(root, "rawone", salt: 0x1111, aliasCreated: false);
            var rawTwo = ManifestsOfOneRun(root, "rawtwo", salt: 0x2222, aliasCreated: false);
            Assert.NotEqual(rawOne, rawTwo);
            Assert.DoesNotContain("created:", rawOne);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // The fixtures come from the lane gate's local_ids and draft_manifests run on the same data.
    [Fact]
    public void ManifestsAreByteForByteWhatTheLaneGateWritesWhenTheProposalCreatesIds()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var r1 = new ReadingSignature([Morph(1, 21, 2, 3,
            features: [Feature(ReadingFeatureRole.Inflection, [31], [32, 33])])]);
        var r2 = new ReadingSignature([Morph(11, 0, 12, 13, inflection: 41, derivation: (51, 52), candidates: [22, 23],
            features: [Feature(ReadingFeatureRole.DerivationInput, [31, 32], [33, 32])])]);
        var r3 = new ReadingSignature([Morph(1, 21, 2, 3), Morph(14, 23, 12, 13, stem: false,
            features: [Feature(ReadingFeatureRole.Inflection, [31], [32])])]);
        (string, int)[] created =
        [
            ("lexical/lexDb/createEntries", 11), ("lexical/lexEntry/createMorphoSyntaxAnalyses", 12),
            ("lexical/lexEntry/createAlternateForms", 13), ("lexical/lexEntry/createSenses", 23),
            ("lists/cmPossibilityList/createPossibilities", 32), ("lists/cmPossibilityList/createPossibilities", 51), ("lexical/lexEntry/createAlternateForms", 14),
        ];
        var operations = created.Select(item => (object)new
        {
            operationId = CanonicalId.Mint("operation/").Value, kind = item.Item1, entityId = G(item.Item2), after = new { },
        }).ToArray();
        var proposal = "";
        var published = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, located) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, located,
            [
                new("Kata", [], []) { BeforeSignatures = [r1, r1], AfterSignatures = [r2] },
                new("Ma\u0301ta", [], []) { BeforeSignatures = [r1], AfterSignatures = [] },
                new("pata", [], []) { BeforeSignatures = [], AfterSignatures = [r3] },
            ], operations: operations);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(published.Succeeded, published.Refusal?.Message);
        var directory = Path.Combine(fixture.Root, "manifests");

        var result = TrialReadingDelta.ReadAndWrite(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal), directory);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(Rig("expect-retired.jsonl"), File.ReadAllText(Path.Combine(directory, "expect-retired.jsonl")));
        Assert.Equal(Rig("expect-added.jsonl"), File.ReadAllText(Path.Combine(directory, "expect-added.jsonl")));
        var real = TrialReadingDelta.ReadAndWrite(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal),
            Path.Combine(fixture.Root, "real"), new ManifestOptions(AliasCreated: false, KeepCase: true));
        Assert.True(real.Succeeded, real.Refusal?.Message);
        var added = File.ReadAllText(Path.Combine(fixture.Root, "real", "expect-added.jsonl"));
        Assert.DoesNotContain("created:", added);
        Assert.StartsWith("{\"word\": \"Kata\"", added);
    }

    private static TrialExpectations Declared(string directory) =>
        new(Rows(directory, "expect-retired.jsonl"), Rows(directory, "expect-added.jsonl"));

    private static Refusal? Verify(string project, string proposal, string receiptPath)
    {
        Refusal? refusal = null;
        Change(project, (database, located) => refusal = TrialReceiptFile.Verify(database, located,
            new SIL.Motif.Worker.Store.ProposalRepository(database).Get(CanonicalId.Parse(proposal)), receiptPath));
        return refusal;
    }

    [Fact]
    public void AGreenTrialWritesAReceiptThatApplyHonoursUntilTheRevisionOrBaselineMoves()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = ProposalWithAllClasses(fixture);
        var directory = Path.Combine(fixture.Root, "manifests");
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);
        Assert.True(TrialReadingDelta.ReadAndWrite(request, directory).Succeeded);
        var path = Path.Combine(fixture.Root, "receipt.json");

        var result = TrialReadingDelta.Check(request, Declared(directory), receiptPath: path);

        Assert.Equal(ReadingExpectationStates.Green, result.Value!.Expectations!.State);
        Assert.Equal(Path.GetFullPath(path), result.Value.Receipt!.Path);
        Assert.Equal(TrialReceiptFile.GateCodeDigest, result.Value.Receipt.GateCodeDigest);
        Assert.Null(Verify(fixture.Project, proposal, path));

        Change(fixture.Project, (database, located) => ProposalCompareExports.RecordCurrentBaseline(database, located, 'b'));
        Assert.Equal(RefusalCodes.ApplyReceiptStale, Verify(fixture.Project, proposal, path)?.Code);
    }

    [Fact]
    public void AReceiptIsRefusedAfterTheProposalChangesOrTheFileIsEditedOrAnotherBuildIssuedIt()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = ProposalWithAllClasses(fixture);
        var directory = Path.Combine(fixture.Root, "manifests");
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);
        Assert.True(TrialReadingDelta.ReadAndWrite(request, directory).Succeeded);
        var path = Path.Combine(fixture.Root, "receipt.json");
        Assert.True(TrialReadingDelta.Check(request, Declared(directory), receiptPath: path).Value!.Receipt is not null);
        var text = File.ReadAllText(path);

        Assert.Equal(RefusalCodes.ApplyReceiptInvalid, Verify(fixture.Project, proposal, Path.Combine(fixture.Root, "absent.json"))?.Code);
        File.WriteAllText(path, text.Replace("green", "mismatch"));
        Assert.Equal(RefusalCodes.ApplyReceiptInvalid, Verify(fixture.Project, proposal, path)?.Code);
        File.WriteAllText(path, "not json");
        Assert.Equal(RefusalCodes.ApplyReceiptInvalid, Verify(fixture.Project, proposal, path)?.Code);

        var receipt = System.Text.Json.JsonSerializer.Deserialize<TrialReceipt>(text, SIL.Motif.Contract.MotifJson.CreateOptions())!;
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
            TrialReceiptFile.Seal(receipt with { GateCodeDigest = "other-build" }), SIL.Motif.Contract.MotifJson.CreateOptions()));
        var otherBuild = Verify(fixture.Project, proposal, path);
        Assert.Equal(RefusalCodes.ApplyReceiptStale, otherBuild?.Code);
        Assert.Contains("other-build", otherBuild!.Message);

        File.WriteAllText(path, text);
        Assert.Null(Verify(fixture.Project, proposal, path));
        Change(fixture.Project, (database, _) => ProposalCompareExports.SaveProposal(database, proposal,
            ProposalCompareExports.OtherProposalJson(proposal)));
        Assert.Equal(RefusalCodes.ApplyReceiptStale, Verify(fixture.Project, proposal, path)?.Code);
    }

    [Fact]
    public void ATrialThatIsNotGreenWritesNoReceiptAndRemovesAnEarlierOne()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = ProposalWithAllClasses(fixture);
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);
        var path = Path.Combine(fixture.Root, "receipt.json");
        File.WriteAllText(path, "{}");

        var result = TrialReadingDelta.Check(request, new TrialExpectations([], []), receiptPath: path);

        Assert.Equal(ReadingExpectationStates.Mismatch, result.Value!.Expectations!.State);
        Assert.Null(result.Value.Receipt);
        Assert.False(File.Exists(path));
        Assert.Equal(RefusalCodes.TrialDeltaRequestInvalid,
            TrialReadingDelta.Check(request, new TrialExpectations(), receiptPath: path).Refusal?.Code);
    }

    [Fact]
    public void ApplyStopsOnAReceiptThatIsMissingBeforeItReadsAnythingElse()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = ProposalWithAllClasses(fixture);

        var applied = ProposalCommands.ApplyWithinLease(new ApplyRequest(fixture.Project, MotifProductVersion.CurrentText, proposal,
            Attribution: new ApplyAttribution(ReceiptPath: Path.Combine(fixture.Root, "absent.json"))));

        Assert.Equal(RefusalCodes.ApplyReceiptInvalid, applied.Refusal?.Code);
    }

    private sealed class FakeCauses(Dictionary<string, long> tokens, Dictionary<string, string> entries,
        Dictionary<string, (string Id, string Name)[]> slots, bool canNameSlots = true) : IReadingCauseSource
    {
        public bool HasTokens => tokens.Count > 0;
        public bool CanNameSlots => canNameSlots;
        public long Tokens(string word) => tokens.GetValueOrDefault(word.ToLowerInvariant());
        public string? EntryLabel(string entryId) => entries.GetValueOrDefault(entryId);
        public IReadOnlyList<(string Id, string? Name)>? Slots(string msaId) =>
            slots.TryGetValue(msaId, out var found) ? [.. found.Select(slot => (slot.Id, (string?)slot.Name))] : null;
    }

    private static FakeCauses Causes(Dictionary<string, long>? tokens = null, Dictionary<string, string>? entries = null,
        Dictionary<string, (string Id, string Name)[]>? slots = null, bool canNameSlots = true) =>
        new(tokens ?? [], entries ?? [], slots ?? [], canNameSlots);

    [Fact]
    public void GroupByEntryCountsRetiredAddedWordsTokensAndSamplesPerEntryMostFrequentFirst()
    {
        using var fixture = new ReviewAcceptanceFixture();
        // Entry 1 loses on a and b and gains on c; entry 2 changes on d; entry 3 is unchanged.
        var proposal = Publish(fixture.Project, Form("a", [1], []), Form("b", [1], []), Form("c", [], [1]),
            Form("d", [2], [3]), Form("same", [9], [9]));
        var causes = Causes(new() { ["a"] = 5, ["b"] = 7, ["c"] = 1, ["d"] = 20 },
            new() { [Id(1)] = "walk", [Id(2)] = "run" });

        var delta = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal, GroupBy: "entry"), causes).Value!;

        var groups = delta.Groups!;
        Assert.Equal("entry", groups.By);
        Assert.Equal(3, groups.Total);
        Assert.Equal([Id(2), Id(3), Id(1)], groups.Rows.Select(row => row.Key));
        var walk = groups.Rows.Single(row => row.Key == Id(1));
        Assert.Equal("walk", walk.Label);
        Assert.Equal((2, 1, 3, 13), (walk.Retired, walk.Added, walk.Words, walk.Tokens));
        Assert.Equal(["b", "a", "c"], walk.SampleWords);
        var run = groups.Rows[0];
        Assert.Equal((1, 0, 1, 20), (run.Retired, run.Added, run.Words, run.Tokens));
        Assert.Equal((0, 1, 1, 20), (groups.Rows[1].Retired, groups.Rows[1].Added, groups.Rows[1].Words, groups.Rows[1].Tokens));
        Assert.All(delta.Classes, group => Assert.Empty(group.Rows));
        Assert.Equal(2, delta.Classes.Single(item => item.Class == "lost").Total);
    }

    [Fact]
    public void GroupByEntryKeepsAtMostFiveSampleWordsAndTiesBreakByKey()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var words = "abcdefg".Select(letter => Form(letter.ToString(), [1], [])).Concat([Form("z", [2], [])]).ToArray();
        var proposal = Publish(fixture.Project, words);

        var delta = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal, GroupBy: "entry"), Causes()).Value!;

        Assert.Equal(["a", "b", "c", "d", "e"], delta.Groups!.Rows.Single(row => row.Key == Id(1)).SampleWords);
        Assert.Equal([Id(1), Id(2)], delta.Groups.Rows.Select(row => row.Key));
        Assert.Contains("tokens-unavailable", delta.Limitations);
    }

    [Fact]
    public void GroupByEntryHonoursClassesAndPaging()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("lost", [1], []), Form("gained", [], [2]), Form("changed", [3], [4]));
        var causes = Causes(new() { ["lost"] = 3, ["gained"] = 2, ["changed"] = 1 });

        var lostOnly = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal,
            Classes: ["lost"], GroupBy: "entry"), causes).Value!.Groups!;
        var paged = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal,
            Cursor: SIL.Motif.Contract.Responses.ResultPaging.Cursor(1), Limit: 2, GroupBy: "entry"), causes).Value!.Groups!;

        Assert.Equal([Id(1)], lostOnly.Rows.Select(row => row.Key));
        Assert.Equal(4, paged.Total);
        Assert.Equal([Id(2), Id(3)], paged.Rows.Select(row => row.Key));
    }

    [Fact]
    public void GroupBySlotUsesAffixMorphemesSlotsAndNoneForSlotlessAffixes()
    {
        using var fixture = new ReviewAcceptanceFixture();
        static ProposalCompareExports.FormSpec Affixed(string form, int[] before, int[] after) =>
            new(form, [], []) { BeforeSignatures = [.. before.Select(item => Sig(item, stem: false))], AfterSignatures = [.. after.Select(item => Sig(item, stem: false))] };
        var proposal = Publish(fixture.Project, Affixed("a", [1], []), Affixed("b", [2], []), Affixed("c", [], [3]), Form("stem", [4], []),
            Affixed("d", [5], []), Affixed("e", [6], []));
        var causes = Causes(new() { ["a"] = 4, ["b"] = 9, ["c"] = 1, ["d"] = 2, ["e"] = 3 }, slots: new()
        {
            [Id(101)] = [(Id(500), "Number")],
            [Id(102)] = [],
            [Id(103)] = [(Id(500), "Number"), (Id(501), "Case")],
            [Id(105)] = [("(derivational)", "")],
        });

        var groups = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal, GroupBy: "slot"), causes)
            .Value!.Groups!;

        Assert.Equal(["(no slot)", Id(500), "(unknown msa)", "(derivational)", Id(501)], groups.Rows.Select(row => row.Key));
        Assert.Equal((1, 0, 1, 9), (groups.Rows[0].Retired, groups.Rows[0].Added, groups.Rows[0].Words, groups.Rows[0].Tokens));
        var number = groups.Rows[1];
        Assert.Equal("Number", number.Label);
        Assert.Equal((1, 1, 2, 5), (number.Retired, number.Added, number.Words, number.Tokens));
        Assert.Equal((0, 1), (groups.Rows[4].Retired, groups.Rows[4].Added));
    }

    [Fact]
    public void GroupByEntryCountsAWordOnceWhenItsSpellingsDifferOnlyInCase()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("Walk", [1], []), Form("walk", [1], []));
        var causes = Causes(new() { ["walk"] = 6 });

        var group = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal, GroupBy: "entry"), causes)
            .Value!.Groups!.Rows.Single();

        Assert.Equal((2, 1, 6), (group.Retired, group.Words, group.Tokens));
        Assert.Single(group.SampleWords);
    }

    [Fact]
    public void GroupByRuleIsRefusedBecauseAReadingNamesNoRule()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("a", [1], []));

        var result = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal, GroupBy: "rule"));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.TrialDeltaGroupUnsupported, result.Refusal!.Code);
    }

    [Fact]
    public void GroupByAnythingElseIsAnInvalidRequest()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("a", [1], []));

        var result = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal, GroupBy: "word"));

        Assert.Equal(RefusalCodes.TrialDeltaRequestInvalid, result.Refusal!.Code);
    }

    [Fact]
    public void GroupBySlotIsRefusedWhenTheBaselineProjectCannotBeRead()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("a", [1], []));

        var result = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal, GroupBy: "slot"),
            Causes(canNameSlots: false));

        Assert.Equal(RefusalCodes.TrialDeltaGroupUnsupported, result.Refusal!.Code);
    }

    [Fact]
    public void GroupByEntryFromTheRealBaselineWorksWithoutNamesWhenTheBaselineHasNoProject()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("a", [1], []));

        var result = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal, GroupBy: "entry"));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var group = result.Value!.Groups!.Rows.Single();
        Assert.Equal((Id(1), null, 1, 1, 0), (group.Key, group.Label, group.Retired, group.Words, group.Tokens));
        Assert.Contains("tokens-unavailable", result.Value.Limitations);
    }
}
