using SIL.Motif.Runner.Execution;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Receipts;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserSwahiliAffixFeatureProofTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    private const string ProductVersion = "0.1.0";

    [RealParserFact]
    public async Task AffixFeatureAuthoringKeepsSourceConstraintsAndParserControlsExact()
    {
        using var cache = pristine.NewScratch();
        var fixture = SeedBaseline(cache);
        AssertBaselineGraph(cache, fixture);
        var beforeDigest = BaselineSemanticDigest.Compute(cache);
        var projectPath = cache.ProjectId.Path;
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();

        var evidence = EvidenceArchive.Create(output);
        var baselineEvidenceProject = evidence.CopySavedProject(projectPath, "baseline");
        using (var baselineCopy = new FwDataProjectLoader().LoadScratchCache(baselineEvidenceProject))
            Assert.Equal(beforeDigest, BaselineSemanticDigest.Compute(baselineCopy));
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        await evidence.RecordImporterAsync(invoker, baselineEvidenceProject, "baseline");
        var baselineNoun = await CompletedTrace(tracer, baselineEvidenceProject, "kitabu");
        evidence.WriteTrace("baseline", baselineNoun);
        WriteWitness(output, "baseline kitabu", AssertReading(baselineNoun, fixture.NounKi, fixture.KitabuStem));
        foreach (var word in new[] { "kizuri", "mzuri", "kafini", "mafini" })
        {
            var trace = await CompletedTrace(tracer, baselineEvidenceProject, word);
            evidence.WriteTrace("baseline", trace);
            var reading = word switch
            {
                "kizuri" => AssertReading(trace, fixture.AdjectiveKi, fixture.Zuri),
                "mzuri" => AssertReading(trace, fixture.AdjectiveM, fixture.Zuri),
                "kafini" => AssertReading(trace, fixture.FixedKa, fixture.FixedStem),
                "mafini" => AssertReading(trace, fixture.FixedMa, fixture.FixedStem),
                _ => throw new InvalidOperationException($"Unexpected baseline control '{word}'.")
            };
            WriteWitness(output, $"baseline {word}", reading);
        }

        const string draftName = "swahili-affix-features-proof";
        Assert.True(ProposalCommands.New(new NewDraftRequest(projectPath, ProductVersion, draftName,
            "Swahili affix feature proof")).Succeeded);

        var featureIntent = ComposeOne(projectPath, draftName, "compose-author-inflection-feature",
            new
            {
                name = "nc",
                ws = analysisWs,
                values = new[] { new { name = "nc1" }, new { name = "nc7" }, new { name = "nc8" } },
            }, new Citation("P3 — Swahili morphotactics spec", "§3.1 F-nc"));
        var ncFeature = Created(featureIntent, FsFeatureSystemFeaturesOperationKinds.Create);
        var valuesByName = ReadCreatedFeatureValues(projectPath, draftName, featureIntent);
        var nc1 = valuesByName["nc1"];
        var nc7 = valuesByName["nc7"];
        var nc8 = valuesByName["nc8"];

        var assignments = new List<FeatureAssignment>();
        AddAssignment(projectPath, draftName, fixture.NounKi.Msa, ncFeature, nc7, "nc7",
            new Citation("P3 — Swahili morphotactics spec", "§3.2 G7/8; §4 NC-7-8"), assignments);
        AddAssignment(projectPath, draftName, fixture.KitabuStem.Msa, ncFeature, nc7, "nc7",
            new Citation("P3 — Swahili morphotactics spec", "§3.1 F-nc; §3.2 G7/8 example kitabu"), assignments);
        AddAssignment(projectPath, draftName, fixture.AdjectiveKi.Msa, ncFeature, nc7, "nc7",
            AcCitation, assignments);
        AddAssignment(projectPath, draftName, fixture.AdjectiveVi.Msa, ncFeature, nc8, "nc8",
            AcCitation, assignments);
        AddAssignment(projectPath, draftName, fixture.AdjectiveM.Msa, ncFeature, nc1, "nc1",
            AcCitation, assignments);
        AddAssignment(projectPath, draftName, fixture.FixedKa.Msa, ncFeature, nc7, "nc7", null, assignments);
        AddAssignment(projectPath, draftName, fixture.FixedMa.Msa, ncFeature, nc1, "nc1", null, assignments);
        AddAssignment(projectPath, draftName, fixture.FixedStem.Msa, ncFeature, nc7, "nc7", null, assignments);

        Assert.True(ProposalCommands.Label(new LabelRequest(projectPath, ProductVersion, draftName,
            "Swahili class features on affixes")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(projectPath, ProductVersion, draftName,
            "Add cited NC feature values to synthetic noun controls and existing adjective concord MSAs; " +
            "keep zuri unclassified and include one synthetic feature clash control.")).Succeeded);

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, ProductVersion, draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = CanonicalId.Parse(finalized.Value!.ProposalId);
        var loaded = ProjectStoreCommand.Run<ProposalRecord>(projectPath, ProductVersion, (database, _) =>
            CommandOutcome<ProposalRecord>.Success(new ProposalRepository(database).Get(proposalId)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = ProposalJsonParser.Parse(loaded.Value!.ProposalJson!);
        AssertProposalTargetsAndDependencies(proposal, assignments);
        AssertCitations(proposal, assignments.Where(assignment => assignment.Citation is not null));

        var reversed = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            proposal.Operations.Reverse().ToArray(), proposal.Extensions);
        var forwardReplay = ReplayFromSavedBaseline(evidence, projectPath, "forward", proposal, beforeDigest,
            fixture, assignments, ncFeature, [nc1, nc7, nc8]);
        var reversedReplay = ReplayFromSavedBaseline(evidence, projectPath, "reversed", reversed, beforeDigest,
            fixture, assignments, ncFeature, [nc1, nc7, nc8]);
        evidence.WriteReplayResults(forwardReplay, reversedReplay);

        ExercisePreDryRunOwnerMove(projectPath, fixture, proposal, assignments, ncFeature, [nc1, nc7, nc8]);

        var dryRun = DryRunJobRunner.Run(projectPath, ProductVersion, proposalId.Value);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        using (var unchanged = new FwDataProjectLoader().LoadCache(projectPath))
            Assert.Equal(beforeDigest, BaselineSemanticDigest.Compute(unchanged));

        AssertApplyRefusesOccupiedFeatureStructureDrift(projectPath, proposal, fixture.FixedKa.Msa, beforeDigest);

        var applied = ProposalCommands.Apply(new ApplyRequest(projectPath, ProductVersion, proposalId.Value,
            Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.NotEmpty(applied.Value!.Effects);

        Dictionary<Guid, NativeFeatureReadback> nativeFeatureReadbacks;
        NativeAssignmentReadback[] nativeAssignmentReadbacks;
        using (var reopened = new FwDataProjectLoader().LoadCache(projectPath))
        {
            AssertApplied(reopened, fixture, assignments, ncFeature, [nc1, nc7, nc8]);
            nativeAssignmentReadbacks = ReadNativeAssignmentReadbacks(reopened, assignments);
            nativeFeatureReadbacks = ReadNativeFeatureReadbacks(reopened, assignments);
        }
        var appliedEvidenceProject = evidence.CopySavedProject(projectPath, "applied");
        using (var archivedProject = new FwDataProjectLoader().LoadScratchCache(appliedEvidenceProject))
        {
            AssertApplied(archivedProject, fixture, assignments, ncFeature, [nc1, nc7, nc8]);
            var archivedNativeIds = ReadNativeAssignmentReadbacks(archivedProject, assignments);
            Assert.Equal(nativeAssignmentReadbacks, archivedNativeIds);
            nativeAssignmentReadbacks = archivedNativeIds;
            nativeFeatureReadbacks = ReadNativeFeatureReadbacks(archivedProject, assignments);
            evidence.WriteNativeReadbacks(nativeAssignmentReadbacks);
        }
        await evidence.RecordImporterAsync(invoker, appliedEvidenceProject, "applied");

        var kitabu = await CompletedTrace(tracer, appliedEvidenceProject, "kitabu");
        evidence.WriteTrace("applied", kitabu, assignments, nativeFeatureReadbacks);
        var kitabuReading = AssertReading(kitabu, fixture.NounKi, fixture.KitabuStem);
        AssertSourceFeature(kitabuReading, AssignmentFor(assignments, fixture.NounKi.Msa),
            nativeFeatureReadbacks[fixture.NounKi.Msa.ToGuid()]);
        AssertSourceFeature(kitabuReading, AssignmentFor(assignments, fixture.KitabuStem.Msa),
            nativeFeatureReadbacks[fixture.KitabuStem.Msa.ToGuid()]);
        WriteWitness(output, "kitabu", kitabuReading, assignments, nativeFeatureReadbacks);

        var kizuri = await CompletedTrace(tracer, appliedEvidenceProject, "kizuri");
        evidence.WriteTrace("applied", kizuri, assignments, nativeFeatureReadbacks);
        var kizuriReading = AssertReading(kizuri, fixture.AdjectiveKi, fixture.Zuri);
        AssertSourceFeature(kizuriReading, AssignmentFor(assignments, fixture.AdjectiveKi.Msa),
            nativeFeatureReadbacks[fixture.AdjectiveKi.Msa.ToGuid()]);
        WriteWitness(output, "kizuri", kizuriReading, assignments, nativeFeatureReadbacks);

        var mzuri = await CompletedTrace(tracer, appliedEvidenceProject, "mzuri");
        evidence.WriteTrace("applied", mzuri, assignments, nativeFeatureReadbacks);
        var mzuriReading = AssertReading(mzuri, fixture.AdjectiveM, fixture.Zuri);
        AssertSourceFeature(mzuriReading, AssignmentFor(assignments, fixture.AdjectiveM.Msa),
            nativeFeatureReadbacks[fixture.AdjectiveM.Msa.ToGuid()]);
        Assert.DoesNotContain(mzuri.Document!.Analyses.SelectMany(analysis => analysis.Morphs), morph =>
            ParserId(morph.FormId) == fixture.AdjectiveKi.Form.ToGuid() ||
            ParserId(morph.FormId) == fixture.AdjectiveVi.Form.ToGuid());
        WriteWitness(output, "mzuri", mzuriReading, assignments, nativeFeatureReadbacks);

        var matching = await CompletedTrace(tracer, appliedEvidenceProject, "kafini");
        evidence.WriteTrace("applied", matching, assignments, nativeFeatureReadbacks);
        var matchingReading = AssertReading(matching, fixture.FixedKa, fixture.FixedStem);
        AssertSourceFeature(matchingReading, AssignmentFor(assignments, fixture.FixedKa.Msa),
            nativeFeatureReadbacks[fixture.FixedKa.Msa.ToGuid()]);
        AssertSourceFeature(matchingReading, AssignmentFor(assignments, fixture.FixedStem.Msa),
            nativeFeatureReadbacks[fixture.FixedStem.Msa.ToGuid()]);
        WriteWitness(output, "synthetic matching control kafini", matchingReading, assignments,
            nativeFeatureReadbacks);

        var conflicting = await CompletedTrace(tracer, appliedEvidenceProject, "mafini");
        evidence.WriteTrace("applied", conflicting, assignments, nativeFeatureReadbacks);
        AssertNoAnalyses(conflicting);
        WriteNoAnalysisWitness(output, "synthetic conflicting control mafini", fixture.FixedMa.Msa,
            fixture.FixedStem.Msa, assignments, nativeFeatureReadbacks);
        output.WriteLine($"Evidence archive: {evidence.Root}");

        Assert.Equal(forwardReplay.DryRunEffectsJson, reversedReplay.DryRunEffectsJson);
        Assert.Equal(forwardReplay.DryRunEffectDigest, reversedReplay.DryRunEffectDigest);
        Assert.Equal(forwardReplay.ApplyEffectsJson, reversedReplay.ApplyEffectsJson);
        Assert.Equal(forwardReplay.ApplyEffectDigest, reversedReplay.ApplyEffectDigest);
        Assert.Equal(forwardReplay.DryRunEffectsJson, forwardReplay.ApplyEffectsJson);
        Assert.Equal(forwardReplay.DryRunEffectDigest, forwardReplay.ApplyEffectDigest);
        Assert.Equal(reversedReplay.DryRunEffectsJson, reversedReplay.ApplyEffectsJson);
        Assert.Equal(reversedReplay.DryRunEffectDigest, reversedReplay.ApplyEffectDigest);
        Assert.Equal(forwardReplay.NativeReadbacks, reversedReplay.NativeReadbacks);
    }

    private static readonly Citation AcCitation = new("ADJ-CONCORD; CONC-TABLE",
        "spec/P3-morphotactics.md §§4–5; trace/citations.tsv; NALRC2001 ch15 printed p116 / PDF page115 (1-based)");

    private static ComposedIntentResult ComposeOne(string projectPath, string draftName, string verb,
        object intent, Citation? citation)
    {
        var step = new Dictionary<string, object?> { ["verb"] = verb, ["intent"] = intent };
        if (citation is not null)
            step["citation"] = new { source = citation.Source, locator = citation.Locator };
        var outcome = ProposalCommands.ComposeBatch(new ComposeBatchRequest(projectPath, ProductVersion,
            draftName, JsonSerializer.Serialize(new { intents = new[] { step } })));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return Assert.Single(outcome.Value!.Intents);
    }

    private static CanonicalId Created(ComposedIntentResult result, string kind) =>
        CanonicalId.Parse(Assert.Single(result.CreatedObjects, item => item.Kind == kind).Id);

    private static Dictionary<string, CanonicalId> ReadCreatedFeatureValues(string projectPath, string draftName,
        ComposedIntentResult featureIntent)
    {
        var createdValues = featureIntent.CreatedObjects
            .Where(item => item.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(item => CanonicalId.Parse(item.Id)).ToHashSet();
        var draft = ProjectStoreCommand.Run<ProposalRecord>(projectPath, ProductVersion, (database, _) =>
            CommandOutcome<ProposalRecord>.Success(new ProposalRepository(database).GetDraft(draftName)));
        Assert.True(draft.Succeeded, draft.Refusal?.Message);
        var document = JsonSerializer.Deserialize<DraftDocument>(draft.Value!.ProposalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(document);
        return document.Operations
            .Where(operation => operation.Kind == FsSymFeatValNameOperationKinds.SetName &&
                                operation.Target is not null && createdValues.Contains(CanonicalId.Parse(operation.Target)))
            .ToDictionary(operation => operation.After["text"].GetString()!,
                operation => CanonicalId.Parse(operation.Target!), StringComparer.Ordinal);
    }

    private static void AddAssignment(string projectPath, string draftName, CanonicalId msa, CanonicalId feature,
        CanonicalId value, string valueName, Citation? citation, ICollection<FeatureAssignment> assignments)
    {
        var structure = ComposeOne(projectPath, draftName, "compose-author-feature-structure",
            new { msa = msa.Value }, citation);
        var structureId = CanonicalId.Parse(Assert.Single(structure.CreatedObjects).Id);
        var valueResult = ComposeOne(projectPath, draftName, "compose-author-feature-value",
            new { featStruc = structureId.Value, feature = feature.Value, value = value.Value }, citation);
        var specificationId = CanonicalId.Parse(Assert.Single(valueResult.CreatedObjects).Id);
        assignments.Add(new FeatureAssignment(msa, structureId, specificationId, feature, value, valueName, citation));
    }

    private static void AssertProposalTargetsAndDependencies(Proposal proposal,
        IReadOnlyCollection<FeatureAssignment> assignments)
    {
        foreach (var assignment in assignments)
        {
            var createStructure = Assert.Single(proposal.Operations,
                operation => operation.EntityId == assignment.Structure);
            Assert.Equal(assignment.Msa, createStructure.Target);
            var createSpecification = Assert.Single(proposal.Operations,
                operation => operation.EntityId == assignment.Specification);
            Assert.Equal(assignment.Structure, createSpecification.Target);
            Assert.Contains(createStructure.OperationId,
                createSpecification.DependsOn.Select(dependency => dependency.OperationId));
            Assert.Contains(proposal.Operations, operation => operation.Target == assignment.Specification &&
                operation.DependsOn.Any(dependency => dependency.OperationId == createSpecification.OperationId));
        }
    }

    private static void AssertCitations(Proposal proposal, IEnumerable<FeatureAssignment> citedAssignments)
    {
        foreach (var assignment in citedAssignments)
        {
            var targetOperations = proposal.Operations.Where(operation =>
                operation.Target == assignment.Msa || operation.EntityId == assignment.Structure ||
                operation.Target == assignment.Structure || operation.EntityId == assignment.Specification ||
                operation.Target == assignment.Specification);
            Assert.NotEmpty(targetOperations);
            Assert.All(targetOperations, operation =>
            {
                var citation = OperationCitation.FromProvenance(operation.Provenance);
                Assert.Equal(assignment.Citation!.Source, citation?.Source);
                Assert.Equal(assignment.Citation.Locator, citation?.Locator);
            });
        }
    }

    private static ReplayResult ReplayFromSavedBaseline(EvidenceArchive evidence, string projectPath,
        string replayName, Proposal proposal, string baselineDigest, FeatureFixture fixture,
        IReadOnlyCollection<FeatureAssignment> assignments, CanonicalId feature,
        IReadOnlyCollection<CanonicalId> values)
    {
        var scratchRoot = evidence.ReplayProjectRoot(replayName);
        string replayPath;
        SIL.Motif.Model.DryRun.DryRun dryRun;
        Receipt receipt;
        using (var replay = new ScratchCacheFactory().CreateFromFileCopy(projectPath, scratchRoot))
        {
            replayPath = replay.ProjectId.Path;
            Assert.NotEqual(Path.GetFullPath(projectPath), Path.GetFullPath(replayPath));
            Assert.Equal(baselineDigest, BaselineSemanticDigest.Compute(replay));
            dryRun = ScratchDryRun.Of(replay, proposal);
            receipt = ProposalApplier.Apply(replay, proposal, dryRun.Anchor,
                $"swahili-affix-features-{replayName}");
            Assert.False(receipt.AlreadyApplied);
            Assert.Equal(dryRun.EffectDigest, receipt.EffectDigest);
            new FwDataProjectLoader().Save(replay);
        }

        using var reopened = new FwDataProjectLoader().LoadScratchCache(replayPath);
        AssertApplied(reopened, fixture, assignments, feature, values);
        return new ReplayResult(
            ExpectedEffectSetJsonWriter.WriteJson(dryRun.ExpectedEffects),
            ExpectedEffectSetDigest.Compute(dryRun.ExpectedEffects),
            ExpectedEffectSetJsonWriter.WriteJson(receipt.ActualEffects),
            ExpectedEffectSetDigest.Compute(receipt.ActualEffects),
            replayPath,
            baselineDigest,
            ReadNativeAssignmentReadbacks(reopened, assignments));
    }

    private static void ExercisePreDryRunOwnerMove(string projectPath, FeatureFixture fixture, Proposal proposal,
        IReadOnlyCollection<FeatureAssignment> assignments, CanonicalId feature,
        IReadOnlyCollection<CanonicalId> values)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.SwahiliAffixOwnerMove",
            Guid.NewGuid().ToString("N"));
        try
        {
            string movedPath;
            using (var moved = new ScratchCacheFactory().CreateFromFileCopy(projectPath, scratchRoot))
            {
                movedPath = moved.ProjectId.Path;
                MoveMsaOwners(moved, fixture.FixedKa, fixture.FixedMa);
                Assert.Equal(fixture.FixedMa.Msa.ToGuid(),
                    Assert.Single(Get<ILexEntry>(moved, fixture.FixedKa.Entry).MorphoSyntaxAnalysesOC).Guid);
                Assert.Equal(fixture.FixedKa.Msa.ToGuid(),
                    Assert.Single(Get<ILexEntry>(moved, fixture.FixedMa.Entry).MorphoSyntaxAnalysesOC).Guid);
                var dryRun = ScratchDryRun.Of(moved, proposal);
                var receipt = ProposalApplier.Apply(moved, proposal, dryRun.Anchor,
                    "swahili-affix-features-owner-moved-before-dry-run");
                Assert.False(receipt.AlreadyApplied);
                Assert.Equal(dryRun.EffectDigest, receipt.EffectDigest);
                new FwDataProjectLoader().Save(moved);
            }

            using var reopened = new FwDataProjectLoader().LoadScratchCache(movedPath);
            AssertOwnerMoveApplied(reopened, fixture, assignments, feature, values);
        }
        finally
        {
            if (Directory.Exists(scratchRoot))
                Directory.Delete(scratchRoot, recursive: true);
        }
    }

    private static void AssertApplyRefusesOccupiedFeatureStructureDrift(string projectPath, Proposal proposal,
        CanonicalId msaId, string originalDigest)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.SwahiliAffixFeatureDrift",
            Guid.NewGuid().ToString("N"));
        try
        {
            using (var drifted = new ScratchCacheFactory().CreateFromFileCopy(projectPath, scratchRoot))
            {
                Assert.NotEqual(Path.GetFullPath(projectPath), Path.GetFullPath(drifted.ProjectId.Path));
                var msa = Get<IMoInflAffMsa>(drifted, msaId);
                Assert.Null(msa.InflFeatsOA);
                var anchor = ScratchDryRun.Of(drifted, proposal).Anchor;
                IFsFeatStruc? occupied = null;
                NonUndoableUnitOfWorkHelper.Do(drifted.ActionHandlerAccessor, () =>
                {
                    occupied = drifted.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
                    msa.InflFeatsOA = occupied;
                });
                new FwDataProjectLoader().Save(drifted);

                var driftedDigest = BaselineSemanticDigest.Compute(drifted);
                Assert.NotEqual(originalDigest, driftedDigest);
                var exception = Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(drifted,
                    proposal, anchor, "swahili-affix-features-occupied-slot-drift"));
                Assert.Contains("Footprint drift detected", exception.Message, StringComparison.Ordinal);
                Assert.Same(occupied, msa.InflFeatsOA);
                Assert.Equal(driftedDigest, BaselineSemanticDigest.Compute(drifted));
                Assert.DoesNotContain(proposal.ProposalId.ToGuid(),
                    ProjectAppliedLog.ReadAll(drifted).Select(entry => entry.ProposalId));
            }

            using var original = new FwDataProjectLoader().LoadScratchCache(projectPath);
            Assert.Equal(originalDigest, BaselineSemanticDigest.Compute(original));
            Assert.DoesNotContain(proposal.ProposalId.ToGuid(),
                ProjectAppliedLog.ReadAll(original).Select(entry => entry.ProposalId));
            Assert.Equal(0L, ReadReceiptCount(projectPath, proposal.ProposalId));
        }
        finally
        {
            if (Directory.Exists(scratchRoot))
                Directory.Delete(scratchRoot, recursive: true);
        }
    }

    private static void MoveMsaOwners(LcmCache cache, Morph first, Morph second)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var firstEntry = Get<ILexEntry>(cache, first.Entry);
            var secondEntry = Get<ILexEntry>(cache, second.Entry);
            var firstMsa = Get<IMoInflAffMsa>(cache, first.Msa);
            var secondMsa = Get<IMoInflAffMsa>(cache, second.Msa);
            firstEntry.MorphoSyntaxAnalysesOC.Add(secondMsa);
            secondEntry.MorphoSyntaxAnalysesOC.Add(firstMsa);
        });
    }

    private static void AssertOwnerMoveApplied(LcmCache cache, FeatureFixture fixture,
        IReadOnlyCollection<FeatureAssignment> assignments, CanonicalId feature,
        IReadOnlyCollection<CanonicalId> values)
    {
        Assert.Equal(fixture.FixedMa.Msa.ToGuid(),
            Assert.Single(Get<ILexEntry>(cache, fixture.FixedKa.Entry).MorphoSyntaxAnalysesOC).Guid);
        Assert.Equal(fixture.FixedKa.Msa.ToGuid(),
            Assert.Single(Get<ILexEntry>(cache, fixture.FixedMa.Entry).MorphoSyntaxAnalysesOC).Guid);
        Assert.Equal(feature.ToGuid(), Get<IFsClosedFeature>(cache, feature).Guid);
        Assert.Equal(values.Select(value => value.ToGuid()).OrderBy(value => value),
            Get<IFsClosedFeature>(cache, feature).ValuesOC.Select(value => value.Guid).OrderBy(value => value));
        foreach (var assignment in assignments)
        {
            var structure = MsaFeatureStructure(Get<ICmObject>(cache, assignment.Msa));
            Assert.NotNull(structure);
            Assert.Equal(assignment.Structure.ToGuid(), structure.Guid);
            var specification = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(structure.FeatureSpecsOC));
            Assert.Equal(assignment.Specification.ToGuid(), specification.Guid);
            Assert.Equal(assignment.Feature.ToGuid(), specification.FeatureRA?.Guid);
            Assert.Equal(assignment.Value.ToGuid(), specification.ValueRA?.Guid);
        }
    }

    private static void AssertApplied(LcmCache cache, FeatureFixture fixture,
        IReadOnlyCollection<FeatureAssignment> assignments, CanonicalId feature,
        IReadOnlyCollection<CanonicalId> values)
    {
        var root = Get<IPartOfSpeech>(cache, fixture.Root);
        var adjective = Get<IPartOfSpeech>(cache, fixture.Adjective);
        var noun = Get<IPartOfSpeech>(cache, fixture.Noun);
        var fixedControl = Get<IPartOfSpeech>(cache, fixture.FixedControl);
        Assert.Equal([fixture.Adjective.ToGuid(), fixture.Noun.ToGuid(), fixture.FixedControl.ToGuid()],
            root.SubPossibilitiesOS.Select(child => child.Guid));
        Assert.Equal([fixture.AdjectiveSlot.ToGuid()], adjective.AffixSlotsOC.Select(slot => slot.Guid));
        Assert.Equal([fixture.NounSlot.ToGuid()], noun.AffixSlotsOC.Select(slot => slot.Guid));
        Assert.Equal([fixture.ControlSlot.ToGuid()], fixedControl.AffixSlotsOC.Select(slot => slot.Guid));
        Assert.Equal([fixture.AdjectiveTemplate.ToGuid()], adjective.AffixTemplatesOS.Select(item => item.Guid));
        Assert.Equal([fixture.NounTemplate.ToGuid()], noun.AffixTemplatesOS.Select(item => item.Guid));
        Assert.Equal([fixture.ControlTemplate.ToGuid()], fixedControl.AffixTemplatesOS.Select(item => item.Guid));
        Assert.Equal([fixture.AdjectiveSlot.ToGuid()], Get<IMoInflAffixTemplate>(cache, fixture.AdjectiveTemplate)
            .PrefixSlotsRS.Select(slot => slot.Guid));
        Assert.Equal([fixture.NounSlot.ToGuid()], Get<IMoInflAffixTemplate>(cache, fixture.NounTemplate)
            .PrefixSlotsRS.Select(slot => slot.Guid));
        Assert.Equal([fixture.ControlSlot.ToGuid()], Get<IMoInflAffixTemplate>(cache, fixture.ControlTemplate)
            .PrefixSlotsRS.Select(slot => slot.Guid));
        Assert.Equal(feature.ToGuid(), Get<IFsClosedFeature>(cache, feature).Guid);
        Assert.Equal(values.Select(value => value.ToGuid()).OrderBy(value => value),
            Get<IFsClosedFeature>(cache, feature).ValuesOC.Select(value => value.Guid).OrderBy(value => value));

        foreach (var assignment in assignments)
        {
            var structure = MsaFeatureStructure(Get<ICmObject>(cache, assignment.Msa));
            Assert.NotNull(structure);
            Assert.Equal(assignment.Structure.ToGuid(), structure.Guid);
            var specification = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(structure.FeatureSpecsOC));
            Assert.Equal(assignment.Specification.ToGuid(), specification.Guid);
            Assert.Equal(assignment.Feature.ToGuid(), specification.FeatureRA?.Guid);
            Assert.Equal(assignment.Value.ToGuid(), specification.ValueRA?.Guid);
        }

        Assert.Null(Get<IMoStemMsa>(cache, fixture.Zuri.Msa).MsFeaturesOA);
        AssertMorphIdentity(cache, fixture.NounKi);
        AssertMorphIdentity(cache, fixture.KitabuStem);
        AssertMorphIdentity(cache, fixture.AdjectiveKi);
        AssertMorphIdentity(cache, fixture.AdjectiveVi);
        AssertMorphIdentity(cache, fixture.AdjectiveM);
        AssertMorphIdentity(cache, fixture.Zuri);
        AssertMorphIdentity(cache, fixture.FixedKa);
        AssertMorphIdentity(cache, fixture.FixedMa);
        AssertMorphIdentity(cache, fixture.FixedStem);
        AssertSlot(cache, fixture.NounKi, fixture.NounSlot);
        AssertSlot(cache, fixture.AdjectiveKi, fixture.AdjectiveSlot);
        AssertSlot(cache, fixture.AdjectiveVi, fixture.AdjectiveSlot);
        AssertSlot(cache, fixture.AdjectiveM, fixture.AdjectiveSlot);
        AssertSlot(cache, fixture.FixedKa, fixture.ControlSlot);
        AssertSlot(cache, fixture.FixedMa, fixture.ControlSlot);
    }

    private static void AssertMorphIdentity(LcmCache cache, Morph morph)
    {
        var entry = Get<ILexEntry>(cache, morph.Entry);
        Assert.Equal(morph.Form.ToGuid(), entry.LexemeFormOA?.Guid);
        var msa = Assert.Single(entry.MorphoSyntaxAnalysesOC, item => item.Guid == morph.Msa.ToGuid());
        Assert.Equal(morph.Category.ToGuid(), (msa as IMoInflAffMsa)?.PartOfSpeechRA?.Guid ??
            (msa as IMoStemMsa)?.PartOfSpeechRA?.Guid);
    }

    private static void AssertSlot(LcmCache cache, Morph morph, CanonicalId slot)
    {
        var msa = Get<IMoInflAffMsa>(cache, morph.Msa);
        Assert.Equal([slot.ToGuid()], msa.SlotsRC.Select(item => item.Guid));
    }

    private static IFsFeatStruc? MsaFeatureStructure(ICmObject msa) => msa switch
    {
        IMoStemMsa stem => stem.MsFeaturesOA,
        IMoInflAffMsa affix => affix.InflFeatsOA,
        _ => throw new InvalidOperationException($"Unexpected MSA type {msa.ClassName}.")
    };

    private static void AssertBaselineGraph(LcmCache cache, FeatureFixture fixture)
    {
        Assert.Null(Get<IMoStemMsa>(cache, fixture.Zuri.Msa).MsFeaturesOA);
        var root = Get<IPartOfSpeech>(cache, fixture.Root);
        Assert.Equal([fixture.Adjective.ToGuid(), fixture.Noun.ToGuid(), fixture.FixedControl.ToGuid()],
            root.SubPossibilitiesOS.Select(child => child.Guid));
        Assert.True(Get<IMoInflAffixTemplate>(cache, fixture.AdjectiveTemplate).Final);
        Assert.True(Get<IMoInflAffixTemplate>(cache, fixture.NounTemplate).Final);
        Assert.True(Get<IMoInflAffixTemplate>(cache, fixture.ControlTemplate).Final);
    }

    private static PanGlossTraceAnalysis AssertReading(PanGlossTraceOutcome.Completed trace, Morph prefix, Morph stem)
    {
        var analysis = Assert.Single(trace.Document!.Analyses);
        Assert.Equal([prefix.Form.ToGuid(), stem.Form.ToGuid()],
            analysis.Morphs.Select(morph => ParserId(morph.FormId)));
        Assert.Equal([prefix.Msa.ToGuid(), stem.Msa.ToGuid()],
            analysis.Morphs.Select(morph => ParserId(morph.MsaId)));
        return analysis;
    }

    private static void AssertSourceFeature(PanGlossTraceAnalysis analysis, FeatureAssignment assignment,
        NativeFeatureReadback nativeReadback)
    {
        var morph = Assert.Single(analysis.Morphs, item => ParserId(item.MsaId) == assignment.Msa.ToGuid());
        var traceFeature = SourceFeatureIds(morph);
        Assert.Equal(assignment.Feature.ToGuid(), nativeReadback.FeatureId);
        Assert.Equal(assignment.Value.ToGuid(), nativeReadback.ValueId);
        Assert.Equal(nativeReadback.FeatureId, traceFeature.FeatureId);
        Assert.Equal(nativeReadback.ValueId, traceFeature.ValueId);
    }

    private static void WriteWitness(ITestOutputHelper output, string word, PanGlossTraceAnalysis analysis,
        IReadOnlyCollection<FeatureAssignment>? assignments = null,
        IReadOnlyDictionary<Guid, NativeFeatureReadback>? nativeReadbacks = null)
    {
        var witnesses = analysis.Morphs.Select(morph =>
        {
            var msa = ParserId(morph.MsaId);
            var sourceAssignment = assignments?.SingleOrDefault(assignment => assignment.Msa.ToGuid() == msa);
            var featureEvidence = string.Empty;
            if (sourceAssignment is not null)
            {
                var traceFeature = SourceFeatureIds(morph);
                var native = nativeReadbacks![msa];
                featureEvidence = $";trace-source-feature={CanonicalId.FromGuid(traceFeature.FeatureId).Value}" +
                    $";trace-source-value={CanonicalId.FromGuid(traceFeature.ValueId).Value}" +
                    $";native-readback-feature={CanonicalId.FromGuid(native.FeatureId).Value}" +
                    $";native-readback-value={CanonicalId.FromGuid(native.ValueId).Value}";
            }
            return $"form={CanonicalId.FromGuid(ParserId(morph.FormId)).Value};" +
                   $"msa={CanonicalId.FromGuid(msa).Value}{featureEvidence}";
        });
        output.WriteLine($"{word}: complete uncapped source-MSA trace; {string.Join(" + ", witnesses)}");
    }

    private static void WriteNoAnalysisWitness(ITestOutputHelper output, string word, CanonicalId prefixMsa,
        CanonicalId stemMsa, IReadOnlyCollection<FeatureAssignment> assignments,
        IReadOnlyDictionary<Guid, NativeFeatureReadback> nativeReadbacks)
    {
        var prefix = Assert.Single(assignments, assignment => assignment.Msa == prefixMsa);
        var stem = Assert.Single(assignments, assignment => assignment.Msa == stemMsa);
        var prefixNative = nativeReadbacks[prefixMsa.ToGuid()];
        var stemNative = nativeReadbacks[stemMsa.ToGuid()];
        output.WriteLine($"{word}: complete uncapped search; no analyses; " +
            $"prefix MSA={prefix.Msa.Value}/native-readback-feature={CanonicalId.FromGuid(prefixNative.FeatureId).Value}" +
            $"/native-readback-value={CanonicalId.FromGuid(prefixNative.ValueId).Value}; " +
            $"stem MSA={stem.Msa.Value}/native-readback-feature={CanonicalId.FromGuid(stemNative.FeatureId).Value}" +
            $"/native-readback-value={CanonicalId.FromGuid(stemNative.ValueId).Value}");
    }

    private static FeatureAssignment AssignmentFor(IReadOnlyCollection<FeatureAssignment> assignments,
        CanonicalId msa) => Assert.Single(assignments, assignment => assignment.Msa == msa);

    private static (Guid FeatureId, Guid ValueId) SourceFeatureIds(PanGlossTraceMorph morph)
    {
        var features = morph.Raw.GetProperty("features");
        Assert.Equal("captured", features.GetProperty("status").GetString());
        var source = features.GetProperty("source");
        var featureValue = Assert.Single(source.GetProperty("values").EnumerateArray());
        var featureId = ParserId(featureValue.GetProperty("feature").GetString());
        var value = featureValue.GetProperty("value");
        Assert.Equal("closed", value.GetProperty("kind").GetString());
        var valueId = ParserId(value.GetProperty("value").GetString());
        return (featureId, valueId);
    }

    private static NativeAssignmentReadback[] ReadNativeAssignmentReadbacks(LcmCache cache,
        IReadOnlyCollection<FeatureAssignment> assignments) => assignments.Select(assignment =>
    {
        var structure = MsaFeatureStructure(Get<ICmObject>(cache, assignment.Msa));
        Assert.NotNull(structure);
        var specification = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(structure.FeatureSpecsOC));
        Assert.NotNull(specification.FeatureRA);
        Assert.NotNull(specification.ValueRA);
        return new NativeAssignmentReadback(Get<ICmObject>(cache, assignment.Msa).Guid, structure.Guid,
            specification.Guid, specification.FeatureRA!.Guid, specification.ValueRA!.Guid);
    }).OrderBy(item => item.MsaId).ToArray();

    private static Dictionary<Guid, NativeFeatureReadback> ReadNativeFeatureReadbacks(LcmCache cache,
        IReadOnlyCollection<FeatureAssignment> assignments) => ReadNativeAssignmentReadbacks(cache, assignments)
        .ToDictionary(item => item.MsaId, item => new NativeFeatureReadback(item.FeatureId, item.ValueId));

    private static long ReadReceiptCount(string projectPath, CanonicalId proposalId)
    {
        var outcome = ProjectStoreCommand.Run<ReceiptCount>(projectPath, ProductVersion, (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
            command.Parameters.AddWithValue("$proposal", proposalId.Value);
            return CommandOutcome<ReceiptCount>.Success(new ReceiptCount((long)command.ExecuteScalar()!));
        });
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!.Count;
    }

    private static Guid ParserId(string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Guid.TryParse(value, out var parsed) ? parsed : CanonicalId.Parse(value).ToGuid();
    }

    private static void AssertNoAnalyses(PanGlossTraceOutcome.Completed trace)
    {
        Assert.Equal("-", trace.Summary.Signature);
        Assert.Empty(trace.Document!.Analyses);
    }

    private static async Task<PanGlossTraceOutcome.Completed> CompletedTrace(PanGlossTracer tracer,
        string projectPath, string word)
    {
        var outcome = await tracer.TraceAsync(projectPath, word, CancellationToken.None);
        var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(outcome);
        Assert.True(completed.Summary.Completed);
        Assert.NotNull(completed.Details);
        Assert.True(completed.Details!.SearchCompleted, $"{word}: {completed.Details}");
        Assert.False(completed.Details.Capped, word);
        Assert.False(completed.Details.TimedOut, word);
        Assert.False(completed.Details.InvalidShape, word);
        Assert.NotNull(completed.Document);
        return completed;
    }

    private sealed class EvidenceArchive
    {
        private EvidenceArchive(string root, string sourceSha)
        {
            Root = root;
            SourceSha = sourceSha;
        }

        public string Root { get; }

        private string SourceSha { get; }

        public string ReplayProjectRoot(string replayName) => Path.Combine(Root, "replays", replayName);

        public static EvidenceArchive Create(ITestOutputHelper output)
        {
            var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            var git = new ProcessStartInfo("git") { WorkingDirectory = sourceRoot };
            git.ArgumentList.Add("rev-parse");
            git.ArgumentList.Add("HEAD");
            var source = ToolProcess.Run(git);
            Assert.True(source.ExitCode == 0, $"git rev-parse failed: {source.Error}");
            var sourceSha = source.Output.Trim();
            Assert.Matches("^[0-9a-f]{40}$", sourceSha);
            var status = new ProcessStartInfo("git") { WorkingDirectory = sourceRoot };
            status.ArgumentList.Add("status");
            status.ArgumentList.Add("--porcelain");
            status.ArgumentList.Add("--untracked-files=all");
            var sourceStatus = ToolProcess.Run(status);
            Assert.True(sourceStatus.ExitCode == 0, $"git status failed: {sourceStatus.Error}");
            Assert.True(string.IsNullOrWhiteSpace(sourceStatus.Output),
                "The evidence source must match a clean checked-out commit.");

            var runId = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}";
            var root = Path.Combine(BuildOutput.ProductDirectory, "test-results",
                "SIL.Motif.Tests.LibLcm.SwahiliAffixFeatureProof", sourceSha, runId);
            Directory.CreateDirectory(root);

            var parserPath = PanGlossExecutable.TryLocate();
            var parserSha256 = parserPath is null || !File.Exists(parserPath)
                ? null
                : HashFile(parserPath);
            File.WriteAllText(Path.Combine(root, "run.json"), JsonSerializer.Serialize(new
            {
                sourceSha,
                runId,
                test = nameof(AffixFeatureAuthoringKeepsSourceConstraintsAndParserControlsExact),
                parserPath,
                parserSha256,
                startedUtc = DateTime.UtcNow,
                evidenceScope = "Raw parser traces, importer streams, saved synthetic projects, and native IDs."
            }, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"Evidence source SHA: {sourceSha}");
            return new EvidenceArchive(root, sourceSha);
        }

        public string CopySavedProject(string sourceProjectPath, string phase)
        {
            var root = Path.Combine(Root, phase, "saved-project");
            using var copy = new ScratchCacheFactory().CreateFromFileCopy(sourceProjectPath, root);
            return copy.ProjectId.Path;
        }

        public async Task RecordImporterAsync(PanGlossInvoker invoker, string projectPath, string phase)
        {
            var phaseRoot = Path.Combine(Root, phase);
            Directory.CreateDirectory(phaseRoot);
            var importedGrammar = Path.Combine(phaseRoot, "imported-grammar.json");
            var outcome = await invoker.RunAsync(new PanGlossRequest.Import(projectPath, importedGrammar),
                $"swahili-affix-proof-import-{phase}", CancellationToken.None);
            var standardError = outcome switch
            {
                PanGlossOutcome.Completed completed => completed.StandardError,
                PanGlossOutcome.Refused refused => refused.StandardError,
                PanGlossOutcome.Incomplete incomplete => incomplete.StandardError,
                _ => string.Empty,
            };
            var standardOutput = outcome switch
            {
                PanGlossOutcome.Completed completed => completed.Output,
                PanGlossOutcome.Refused refused => refused.StandardOutput,
                _ => string.Empty,
            };
            File.WriteAllText(Path.Combine(phaseRoot, "importer-standard-error.txt"), standardError);
            File.WriteAllText(Path.Combine(phaseRoot, "importer-standard-output.txt"), standardOutput);
            File.WriteAllText(Path.Combine(phaseRoot, "importer-outcome.json"), JsonSerializer.Serialize(new
            {
                sourceSha = SourceSha,
                phase,
                outcome = outcome.GetType().Name,
                message = outcome.Message,
                importerWarningStreamFile = "importer-standard-error.txt"
            }, new JsonSerializerOptions { WriteIndented = true }));
            Assert.IsType<PanGlossOutcome.Completed>(outcome);
        }

        public void WriteTrace(string phase, PanGlossTraceOutcome.Completed trace,
            IReadOnlyCollection<FeatureAssignment>? assignments = null,
            IReadOnlyDictionary<Guid, NativeFeatureReadback>? nativeReadbacks = null)
        {
            var document = trace.Document ?? throw new InvalidOperationException("A completed trace needs its document.");
            var details = trace.Details ?? throw new InvalidOperationException("A completed trace needs its search details.");
            var tracesRoot = Path.Combine(Root, phase, "traces");
            Directory.CreateDirectory(tracesRoot);
            var rawTracePath = Path.Combine(tracesRoot, $"{trace.Word}.raw.json");
            File.WriteAllText(rawTracePath, document.RawJson);
            var morphEvidence = document.Analyses.SelectMany(analysis => analysis.Morphs.Select(morph =>
            {
                var msaGuid = morph.MsaId is null ? (Guid?)null : ParserId(morph.MsaId);
                var assignment = msaGuid is null
                    ? null
                    : assignments?.SingleOrDefault(item => item.Msa.ToGuid() == msaGuid.Value);
                var sourceIds = assignment is null ? ((Guid FeatureId, Guid ValueId)?)null : SourceFeatureIds(morph);
                var native = msaGuid is not null && nativeReadbacks is not null &&
                    nativeReadbacks.TryGetValue(msaGuid.Value, out var readback) ? readback : null;
                return new ArchivedMorphEvidence(
                    morph.FormId,
                    morph.MsaId,
                    sourceIds is null ? null : CanonicalId.FromGuid(sourceIds.Value.FeatureId).Value,
                    sourceIds is null ? null : CanonicalId.FromGuid(sourceIds.Value.ValueId).Value,
                    native is null ? null : CanonicalId.FromGuid(native.FeatureId).Value,
                    native is null ? null : CanonicalId.FromGuid(native.ValueId).Value);
            })).ToArray();
            File.WriteAllText(Path.Combine(tracesRoot, $"{trace.Word}.evidence.json"),
                JsonSerializer.Serialize(new
                {
                    sourceSha = SourceSha,
                    phase,
                    trace.Word,
                    trace.Summary.Signature,
                    searchCompleted = details.SearchCompleted,
                    capped = details.Capped,
                    timedOut = details.TimedOut,
                    invalidShape = details.InvalidShape,
                    traceCount = document.Analyses.Count,
                    rawTrace = Path.GetFileName(rawTracePath),
                    morphs = morphEvidence
                }, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void WriteNativeReadbacks(IReadOnlyCollection<NativeAssignmentReadback> nativeReadbacks)
        {
            File.WriteAllText(Path.Combine(Root, "native-readbacks.json"), JsonSerializer.Serialize(new
            {
                sourceSha = SourceSha,
                nativeIds = ToCanonicalNativeIds(nativeReadbacks)
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void WriteReplayResults(ReplayResult forward, ReplayResult reversed)
        {
            File.WriteAllText(Path.Combine(Root, "replays.json"), JsonSerializer.Serialize(new
            {
                sourceSha = SourceSha,
                forward = ReplayEvidence(forward),
                reversed = ReplayEvidence(reversed)
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        private object ReplayEvidence(ReplayResult replay) => new
        {
            projectFile = Path.GetRelativePath(Root, replay.ProjectPath),
            projectFileSha256 = HashFile(replay.ProjectPath),
            baselineSemanticDigest = replay.BaselineDigest,
            dryRunEffectsJson = replay.DryRunEffectsJson,
            dryRunEffectDigest = replay.DryRunEffectDigest,
            applyEffectsJson = replay.ApplyEffectsJson,
            applyEffectDigest = replay.ApplyEffectDigest,
            nativeIds = ToCanonicalNativeIds(replay.NativeReadbacks)
        };

        private static IEnumerable<object> ToCanonicalNativeIds(IEnumerable<NativeAssignmentReadback> readbacks) =>
            readbacks.Select(item => new
            {
                msaId = CanonicalId.FromGuid(item.MsaId).Value,
                featureStructureId = CanonicalId.FromGuid(item.FeatureStructureId).Value,
                specificationId = CanonicalId.FromGuid(item.SpecificationId).Value,
                featureId = CanonicalId.FromGuid(item.FeatureId).Value,
                valueId = CanonicalId.FromGuid(item.ValueId).Value
            });

        private static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }

    private static FeatureFixture SeedBaseline(LcmCache cache)
    {
        RealParserProject.PrepareForParsing(cache,
            "k", "i", "v", "t", "a", "b", "u", "z", "r", "m", "f", "n");
        IPartOfSpeech? root = null;
        IPartOfSpeech? adjective = null;
        IPartOfSpeech? noun = null;
        IPartOfSpeech? fixedControl = null;
        IMoInflAffixSlot? adjectiveSlot = null;
        IMoInflAffixSlot? nounSlot = null;
        IMoInflAffixSlot? controlSlot = null;
        IMoInflAffixTemplate? adjectiveTemplate = null;
        IMoInflAffixTemplate? nounTemplate = null;
        IMoInflAffixTemplate? controlTemplate = null;
        Morph? nounKi = null;
        Morph? kitabuStem = null;
        Morph? adjectiveKi = null;
        Morph? adjectiveVi = null;
        Morph? adjectiveM = null;
        Morph? zuri = null;
        Morph? fixedKa = null;
        Morph? fixedMa = null;
        Morph? fixedStem = null;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            root = CreateCategory(cache, null, "Synthetic Swahili");
            adjective = CreateCategory(cache, root, "Adjective");
            noun = CreateCategory(cache, root, "Noun");
            fixedControl = CreateCategory(cache, root, "Synthetic fixed-feature control");
            adjectiveSlot = CreateSlot(cache, adjective, "S-AC");
            nounSlot = CreateSlot(cache, noun, "S-NC");
            controlSlot = CreateSlot(cache, fixedControl, "S-FIXED");
            adjectiveTemplate = CreateTemplate(cache, adjective, "T-ADJ", adjectiveSlot);
            nounTemplate = CreateTemplate(cache, noun, "T-N", nounSlot);
            controlTemplate = CreateTemplate(cache, fixedControl, "T-FIXED", controlSlot);
            nounKi = CreateMorph(cache, "ki", "NC7 noun prefix control", noun, nounSlot, prefix: true);
            kitabuStem = CreateMorph(cache, "tabu", "NC7 synthetic noun control", noun, null, prefix: false);
            adjectiveKi = CreateMorph(cache, "ki", "AC7 concord", adjective, adjectiveSlot, prefix: true);
            adjectiveVi = CreateMorph(cache, "vi", "AC8 concord", adjective, adjectiveSlot, prefix: true);
            adjectiveM = CreateMorph(cache, "m", "AC1 concord", adjective, adjectiveSlot, prefix: true);
            zuri = CreateMorph(cache, "zuri", "good", adjective, null, prefix: false);
            fixedKa = CreateMorph(cache, "ka", "synthetic matching prefix", fixedControl, controlSlot, prefix: true);
            fixedMa = CreateMorph(cache, "ma", "synthetic conflicting prefix", fixedControl, controlSlot, prefix: true);
            fixedStem = CreateMorph(cache, "fini", "synthetic fixed NC7 stem", fixedControl, null, prefix: false);
        });

        return new FeatureFixture(CanonicalId.FromGuid(root!.Guid), CanonicalId.FromGuid(adjective!.Guid),
            CanonicalId.FromGuid(noun!.Guid), CanonicalId.FromGuid(fixedControl!.Guid),
            CanonicalId.FromGuid(adjectiveSlot!.Guid), CanonicalId.FromGuid(nounSlot!.Guid),
            CanonicalId.FromGuid(controlSlot!.Guid), CanonicalId.FromGuid(adjectiveTemplate!.Guid),
            CanonicalId.FromGuid(nounTemplate!.Guid), CanonicalId.FromGuid(controlTemplate!.Guid),
            nounKi!, kitabuStem!, adjectiveKi!, adjectiveVi!, adjectiveM!, zuri!, fixedKa!, fixedMa!, fixedStem!);
    }

    private static IPartOfSpeech CreateCategory(LcmCache cache, IPartOfSpeech? parent, string name)
    {
        var category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
        if (parent is null)
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
        else
            parent.SubPossibilitiesOS.Add(category);
        category.Name.set_String(cache.DefaultAnalWs, name);
        return category;
    }

    private static IMoInflAffixSlot CreateSlot(LcmCache cache, IPartOfSpeech category, string name)
    {
        var slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
        category.AffixSlotsOC.Add(slot);
        slot.Name.set_String(cache.DefaultAnalWs, name);
        return slot;
    }

    private static IMoInflAffixTemplate CreateTemplate(LcmCache cache, IPartOfSpeech category, string name,
        IMoInflAffixSlot slot)
    {
        var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
        category.AffixTemplatesOS.Add(template);
        template.Name.set_String(cache.DefaultAnalWs, name);
        template.Final = true;
        template.PrefixSlotsRS.Add(slot);
        return template;
    }

    private static Morph CreateMorph(LcmCache cache, string form, string gloss, IPartOfSpeech category,
        IMoInflAffixSlot? slot, bool prefix)
    {
        var morphTypeId = prefix ? MoMorphTypeTags.kguidMorphPrefix : MoMorphTypeTags.kguidMorphStem;
        var type = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(morphTypeId);
        var msaType = prefix ? MsaType.kInfl : MsaType.kStem;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(type,
            TsStringUtils.MakeString(form, cache.DefaultVernWs), gloss,
            SandboxGenericMSA.Create(msaType, category));
        var msa = entry.MorphoSyntaxAnalysesOC.Single();
        if (slot is not null)
            Assert.IsAssignableFrom<IMoInflAffMsa>(msa).SlotsRC.Add(slot);
        return new Morph(CanonicalId.FromGuid(entry.Guid), CanonicalId.FromGuid(entry.LexemeFormOA!.Guid),
            CanonicalId.FromGuid(msa.Guid), CanonicalId.FromGuid(category.Guid));
    }

    private static T Get<T>(LcmCache cache, CanonicalId id) where T : class, ICmObject =>
        (T)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid());

    private sealed record Citation(string Source, string Locator);

    private sealed record Morph(CanonicalId Entry, CanonicalId Form, CanonicalId Msa, CanonicalId Category);

    private sealed record FeatureAssignment(CanonicalId Msa, CanonicalId Structure, CanonicalId Specification,
        CanonicalId Feature, CanonicalId Value, string ValueName, Citation? Citation);

    private sealed record NativeFeatureReadback(Guid FeatureId, Guid ValueId);

    private sealed record NativeAssignmentReadback(Guid MsaId, Guid FeatureStructureId, Guid SpecificationId,
        Guid FeatureId, Guid ValueId);

    private sealed record ReplayResult(string DryRunEffectsJson, string DryRunEffectDigest,
        string ApplyEffectsJson, string ApplyEffectDigest, string ProjectPath, string BaselineDigest,
        IReadOnlyList<NativeAssignmentReadback> NativeReadbacks);

    private sealed record ArchivedMorphEvidence(string? FormId, string? MsaId,
        string? TraceFeatureId, string? TraceValueId,
        string? NativeReadbackFeatureId, string? NativeReadbackValueId);

    private sealed record ReceiptCount(long Count);

    private sealed record FeatureFixture(CanonicalId Root, CanonicalId Adjective, CanonicalId Noun,
        CanonicalId FixedControl, CanonicalId AdjectiveSlot, CanonicalId NounSlot,
        CanonicalId ControlSlot, CanonicalId AdjectiveTemplate, CanonicalId NounTemplate,
        CanonicalId ControlTemplate, Morph NounKi, Morph KitabuStem, Morph AdjectiveKi,
        Morph AdjectiveVi, Morph AdjectiveM, Morph Zuri, Morph FixedKa, Morph FixedMa, Morph FixedStem);
}
