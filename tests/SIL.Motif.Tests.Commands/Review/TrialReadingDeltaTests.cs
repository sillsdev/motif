using System.Collections.Immutable;
using SIL.Motif.Commands;
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
        Assert.Equal(new ReadingDeltaCounts(6, 5, 1, 1, 1, 1, 0, 3, 3, 0), delta.Counts);
        Assert.Equal(["lost"], delta.Classes.Single(item => item.Class == "lost").Rows.Select(row => row.Word));
        Assert.Equal(["doubled"], delta.Classes.Single(item => item.Class == "doubled").Rows.Select(row => row.Word));
        var changed = delta.Classes.Single(item => item.Class == "changed").Rows.Single();
        Assert.Equal(Sig(3).Id, changed.Retired.Single().Id);
        Assert.Equal(Sig(4).Id, changed.Added.Single().Id);
        Assert.StartsWith("case/", changed.CaseKey);
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
}
