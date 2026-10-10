using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RetirementReviewQueryTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(RetirementReviewQueryTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void OrdinaryDraftIsReportedAsNotApplicable()
    {
        Directory.CreateDirectory(_root);
        var projectPath = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(projectPath, "project marker");
        var project = ProjectStoreCommand.Locate(projectPath);
        var draftId = CanonicalId.Mint("proposal/");
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            new ProposalRepository(database).CreateDraft("ordinary", draftId, "{\"proposalId\":\"" +
                draftId.Value + "\",\"operations\":[],\"contractVersions\":{},\"requires\":[]}");
        }

        var result = ParsimonyCommands.ReadRetirementReview(new ReadRetirementReviewRequest(
            projectPath, "1.0", draftId.Value));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.False(result.Value!.Applicable);
        Assert.Null(result.Value.Review);
    }

    [Fact]
    public void LatestDryRunForAnEarlierDraftRevisionIsRefused()
    {
        OperationRegistryBootstrap.Initialize();
        Directory.CreateDirectory(_root);
        var projectPath = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(projectPath, "project marker");
        var project = ProjectStoreCommand.Locate(projectPath);
        var draftId = CanonicalId.Mint("proposal/");
        var intentJson = AllomorphRetirementCodec.ToJson(RetirementIntent());
        var intent = JsonDocument.Parse(intentJson).RootElement.Clone();
        var current = Draft(draftId, intent);
        var previousJson = DraftProposalJson(draftId, intent, "earlier gloss");
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            new ProposalRepository(database).CreateDraft("retirement", draftId,
                JsonSerializer.Serialize(current, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var input = new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion,
                FrozenProposalRevision.Create(previousJson), [], null);
            new JobRepository(database).Create(CanonicalId.Mint("job/").Value,
                ProjectWorkspaceKey.Compute(project), JobCommands.DryRunKind,
                JsonSerializer.Serialize(input, MotifJson.CreateOptions()), "2026-10-09T00:00:00Z");
        }

        var result = ParsimonyCommands.ReadRetirementReview(new ReadRetirementReviewRequest(
            projectPath, "1.0", draftId.Value));

        Assert.False(result.Succeeded);
        Assert.Equal("retirement-review.stale-dry-run", result.Refusal!.Code);
    }

    [Fact]
    public void RetirementDraftWithoutPairedReportsReturnsItsPendingEvidence()
    {
        Directory.CreateDirectory(_root);
        var projectPath = pristine.CopyProjectFile();
        var project = ProjectStoreCommand.Locate(projectPath);
        var draftId = CanonicalId.Mint("proposal/");
        string entryId;
        string retiredFormId;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(projectPath))
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                .GetObject(pristine.Seed.FirstEntryId);
            IMoStemAllomorph alternate = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                alternate = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.AlternateFormsOS.Add(alternate);
                alternate.Form.set_String(cache.DefaultVernWs, "alternate");
            });
            new FwDataProjectLoader().Save(cache);
            entryId = CanonicalId.FromGuid(entry.Guid).Value;
            retiredFormId = CanonicalId.FromGuid(alternate.Guid).Value;
        }
        var intent = JsonDocument.Parse(AllomorphRetirementCodec.ToJson(RetirementIntent(entryId,
            retiredFormId))).RootElement.Clone();
        var draft = Draft(draftId, intent);
        var proposalJson = ProposalCommands.BuildProposalJson(draft);
        var proposal = ProposalJsonParser.Parse(proposalJson);
        var intentDigest = IntentDigest.Compute(proposal);
        var effectDigest = ExpectedEffectSetDigest.Compute([]);
        var dryRun = new SIL.Motif.Model.DryRun.DryRun(intentDigest, "Seeded source", [], effectDigest,
            new BoundDryRunAnchor(intentDigest, Digest, effectDigest, "1.0", "1.0", "1",
                "20261009T000000Z"));
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            new ProposalRepository(database).CreateDraft("retirement", draftId,
                JsonSerializer.Serialize(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var projectKey = ProjectWorkspaceKey.Compute(project);
            var now = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            var jobs = new JobRepository(database);
            var job = jobs.Create(CanonicalId.Mint("job/").Value, projectKey, JobCommands.DryRunKind,
                JsonSerializer.Serialize(new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion,
                    FrozenProposalRevision.Create(proposalJson), [], null), MotifJson.CreateOptions()), now);
            var claims = new JobClaims(database);
            var claim = Assert.IsType<JobRecord>(claims.Claim(projectKey, "retirement-review-query", now,
                TimeSpan.FromMinutes(2)));
            var publishedDryRunJson = JsonSerializer.Serialize(new
            {
                intentDigest,
                baselineNote = dryRun.BaselineNote,
                expectedEffects = Array.Empty<object>(),
                effectDigest,
                anchor = dryRun.Anchor,
            }, MotifJson.CreateOptions());
            jobs.PublishDryRun(job.JobId, publishedDryRunJson, claim.Version);
            Assert.True(claims.Finish(job.JobId, claim.ClaimToken!, JobStatus.CompletedDryRunOnly,
                JobFailureCategory.None, "{}"));
        }

        var result = ParsimonyCommands.ReadRetirementReview(new ReadRetirementReviewRequest(
            projectPath, "1.0", draftId.Value));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.True(result.Value!.Applicable);
        Assert.Equal("waiting-for-evidence", result.Value.State);
        Assert.NotNull(result.Value.DryRun);
        Assert.Equal(proposal.Operations.Count, result.Value.DryRun.Operations.Count);
        Assert.Null(result.Value.Review);
        Assert.Contains(result.Value.Unavailable, item => item.Contains("Before-and-after parser results"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Id(int value) => CanonicalId.FromGuid(Guid.Parse(
        $"00000000-0000-0000-0000-{value:000000000000}")).Value;

    private static DraftDocument Draft(CanonicalId draftId, JsonElement retirementIntent, string text = "current gloss")
    {
        var bindings = AllomorphRetirementCodec.Parse(retirementIntent.GetRawText()).Operations;
        return new DraftDocument
        {
            ProposalId = draftId.Value,
            ContractVersions = new Dictionary<string, string> { ["lexical"] = "1.0" },
            Operations = bindings.Select((binding, index) => new DraftOperation
            {
                OperationId = binding.OperationId,
                Kind = LexicalSenseOperationKinds.SetGloss,
                Target = Id(100 + index),
                DependsOn = binding.DependsOn.ToList(),
                After = new Dictionary<string, JsonElement>
                {
                    ["ws"] = JsonSerializer.SerializeToElement("en"),
                    ["text"] = JsonSerializer.SerializeToElement(text),
                },
            }).ToList(),
            ComposerProvenance = [JsonSerializer.SerializeToElement(new
            {
                composer = "RetireAllomorph",
                input = retirementIntent,
            })],
        };
    }

    private static string DraftProposalJson(CanonicalId draftId, JsonElement retirementIntent, string text) =>
        JsonSerializer.Serialize(new
        {
            contractVersions = new Dictionary<string, string> { ["lexical"] = "1.0" },
            proposalId = draftId.Value,
            requires = Array.Empty<string>(),
            operations = AllomorphRetirementCodec.Parse(retirementIntent.GetRawText()).Operations
                .Select((binding, index) => new
                {
                    operationId = binding.OperationId,
                    kind = LexicalSenseOperationKinds.SetGloss,
                    target = Id(100 + index),
                    dependsOn = binding.DependsOn,
                    after = new { ws = "en", text },
                }),
            extensions = new
            {
                composers = new[] { new { composer = "RetireAllomorph", input = retirementIntent } },
            },
        }, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

    private static ReplaceListedAllomorphsWithRuleIntent RetirementIntent(string? existingEntryId = null,
        string? existingFormId = null)
    {
        var retiredId = existingFormId ?? Id(2);
        var entryId = existingEntryId ?? Id(1);
        var scope = existingFormId is null ? AllomorphRetirementScope.Affix : AllomorphRetirementScope.Stem;
        var retired = new AllomorphIdentity(retiredId, entryId, scope == AllomorphRetirementScope.Affix ?
            "MoAffixAllomorph" : "MoStemAllomorph", "alternate",
            scope == AllomorphRetirementScope.Affix ? "suffix" : "stem", Digest, null);
        var replacement = retired with { Id = Id(3), Location = "lexeme" };
        var operations = new List<RetirementOperationBinding>
        {
            new(Id(20), "class-create", Id(4), null, []),
            new(Id(21), "class-members", Id(4), null, [Id(20)]),
        };
        var ruleSlots = new[] { "rule-create", "rule-input", "rule-output", "rule-left", "rule-right", "rule-placement", "rule-enabled" };
        for (var index = 0; index < ruleSlots.Length; index++)
            operations.Add(new RetirementOperationBinding(Id(30 + index), ruleSlots[index], Id(5), null,
                index == 0 ? [Id(21)] : [Id(30), Id(21)]));
        var ruleOperations = Enumerable.Range(30, 7).Select(Id).ToArray();
        operations.Add(new RetirementOperationBinding(Id(40), "bundle-morph", Id(6), null, ruleOperations));
        operations.Add(new RetirementOperationBinding(Id(41), "adhoc-rest", Id(7), null, ruleOperations));
        operations.Add(new RetirementOperationBinding(Id(42), "alternate-delete", entryId, retiredId,
            [Id(40), Id(41), .. ruleOperations]));
        return new ReplaceListedAllomorphsWithRuleIntent(
            new RetirementNaturalClass("create", Id(4), "Non-front vowels", "NFV", [Id(8), Id(9)], null),
            new RetirementRule(Id(5), "l to r after non-front vowels", [Id(10)], [Id(11)],
                [new RetirementContextAtom("natural-class", Id(4))], [],
                new RetirementRulePlacement("last", null), true),
            [new RetireAllomorphIntent(entryId, scope, [retired],
                [new AllomorphRoleReplacement(retiredId, Id(12), null, "whole", replacement)],
                [new RetirementBundle(Id(6), Id(13), Id(14), retiredId, Id(12), null, "whole")],
                [new AdhocOccurrenceReplacement(Id(7), "RestOfAllos", 0, retiredId, replacement)])],
            operations,
            new RetirementDisplay("Listed l/r alternation", "Replace one listed suffix with a sound rule."));
    }
}
