using SIL.Motif.Host.Assess;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Cli;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Model.AppliedLog;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Proof, on a real project: drives the CLI command handlers directly (never shells out to
/// the built executable) through the full <c>new -&gt; add-set-gloss -&gt; finalize -&gt; dry-run -&gt;
/// apply -&gt; log</c> loop against a real sense's <c>CanonicalId.FromGuid(sense.Guid)</c>, proving
/// the paired project database and the thin CLI drive the real Contract/Runner/Host.
/// </summary>
/// <remarks>
/// A workflow test, not an end-to-end one: no process boundary is crossed here. Whether the shipped
/// executables do this is <c>RunnerSpineTests</c>' subject. <c>dry-run</c> is a job (ADR 0041 decision 7),
/// so <see cref="RunDryRun"/> stands in for the real runner: it records a Baseline pointing at this
/// project's own saved file and drains exactly one queued job through the real <see cref="DryRunJobHandler"/>.
/// </remarks>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class ProposalWorkflowTests : IDisposable
{
    private const string ProductVersion = "1.0";

    private readonly SeededProject _seed;
    private readonly string _fwDataPath;
    private readonly string _workerRoot = Path.Combine(Path.GetTempPath(),
        "motif-proposal-workflow-worker-" + Guid.NewGuid().ToString("N"));

    public ProposalWorkflowTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_workerRoot);
        _seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_workerRoot, recursive: true); }
        catch { }
    }

    [Fact]
    public void AdvancedAiCliCompletesDraftAuthoringAndEvaluationWithoutDeveloperOptIn()
    {
        const string draft = "advanced-ai-cli-draft";
        var intentsPath = Path.Combine(_workerRoot, "intents.json");
        File.WriteAllText(intentsPath, JsonSerializer.Serialize(new
        {
            intents = new[]
            {
                new
                {
                    verb = "compose-author-phoneme",
                    intent = new { name = "motifDiscoveryProbe", representations = new[] { "ɮ" } },
                },
            },
        }));

        var preferencePath = Path.Combine(_workerRoot, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath,
            [RunnerOptions.RootVariable] = _workerRoot,
        };
        Assert.False(environment.ContainsKey(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable));

        (int ExitCode, string Output, string Error) Run(params string[] args)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exitCode = global::SIL.Motif.Cli.Program.Run(
                args, output, error, environment, static (_, _) => { });
            return (exitCode, output.ToString(), error.ToString());
        }

        var created = Run("new", "--project", _fwDataPath, "--draft", draft);
        Assert.True(created.ExitCode == 0, created.Error + created.Output);
        const string proposalMarker = "proposalId: ";
        var proposalStart = created.Output.IndexOf(proposalMarker, StringComparison.Ordinal);
        Assert.True(proposalStart >= 0, "Could not find '" + proposalMarker + "' in new's output: " + created.Output);
        var proposalId = created.Output.Substring(proposalStart + proposalMarker.Length).TrimEnd('\r', '\n');

        var composed = Run("compose-batch", "--project", _fwDataPath, "--draft", draft,
            "--intents", intentsPath, "--json");
        Assert.True(composed.ExitCode == 0, composed.Error + composed.Output);

        var labeled = Run("label", "--project", _fwDataPath, "--draft", draft, "CLI authored");
        Assert.True(labeled.ExitCode == 0, labeled.Error + labeled.Output);
        var commented = Run("proposal", "rationale", proposalId, "--project", _fwDataPath, "--text",
            "Reviewed by a person");
        Assert.True(commented.ExitCode == 0, commented.Error + commented.Output);

        var dryRun = Run("dry-run", "--project", _fwDataPath, proposalId, "--json");
        Assert.True(dryRun.ExitCode == 0, dryRun.Error + dryRun.Output);
        var trial = Run("trial", "--project", _fwDataPath, proposalId, "--json");
        Assert.True(trial.ExitCode == 0, trial.Error + trial.Output);

        var finalized = Run("finalize", "--project", _fwDataPath, "--draft", draft, "--json");
        Assert.True(finalized.ExitCode == 0, finalized.Error + finalized.Output);
    }

    [Fact]
    public void ListShowsOpenProposalsUnlessAskedForClosedOnes()
    {
        string Finalized(string name)
        {
            Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, name, name)).Succeeded);
            Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(_fwDataPath, ProductVersion, name,
                CanonicalId.Mint().Value, "en", name)).Succeeded);
            DraftRationale.Author(_fwDataPath, name, name, "Why " + name + " is right.");
            return ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, name)).Value!.ProposalId;
        }
        var open = Finalized("open-one");
        var declined = Finalized("declined-one");
        Assert.True(ProposalCommands.Reject(new RejectRequest(_fwDataPath, ProductVersion, declined)).Succeeded);

        IReadOnlyList<string> Listed(bool all = false, string? status = null) => ProposalCommands.List(
            new ListProposalsRequest(_fwDataPath, ProductVersion, all, status)).Value!.Proposals
            .Select(item => item.ProposalId).ToArray();

        Assert.Equal([open], Listed());
        Assert.Equal(new[] { open, declined }.Order(StringComparer.Ordinal), Listed(all: true));
        Assert.Equal([declined], Listed(status: "rejected"));
        Assert.Equal("proposal.unknown-status", ProposalCommands.List(
            new ListProposalsRequest(_fwDataPath, ProductVersion, Status: "declined")).Refusal?.Code);
    }

    [Fact]
    public void ProposalsRendersTheTypedStandingsInTextAndKeepsTheirJsonShape()
    {
        var empty = ProposalStandingsCommand.Read(new ProposalStandingsRequest(_fwDataPath));
        Assert.True(empty.Succeeded, empty.Refusal?.Message);
        Assert.Empty(empty.Value!.Proposals);
        Assert.Equal(0, empty.Value.SetAsideCount);
        var emptyText = RunCli("proposals", "--project", _fwDataPath);
        Assert.True(emptyText.ExitCode == 0, emptyText.Error + emptyText.Output);
        Assert.Contains("No Proposals yet.", emptyText.Output, StringComparison.Ordinal);

        const string draft = "standings-render";
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draft, "Rendered proposal"))
            .Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(_fwDataPath, ProductVersion, draft,
            CanonicalId.FromGuid(_seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag, "rendered gloss"))
            .Succeeded);
        DraftRationale.Author(_fwDataPath, draft, "Rendered proposal", "Why this Proposal is right.");
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draft)).Succeeded);

        var typed = ProposalStandingsCommand.Read(new ProposalStandingsRequest(_fwDataPath));
        Assert.True(typed.Succeeded, typed.Refusal?.Message);
        var card = Assert.Single(typed.Value!.Proposals);
        Assert.Equal("Rendered proposal", card.Label);
        Assert.Equal("proposed", card.Status);
        Assert.Equal(ProposalGroup.NotTried, card.Group);

        var text = RunCli("proposals", "--project", _fwDataPath);
        Assert.True(text.ExitCode == 0, text.Error + text.Output);
        Assert.Contains("Not tried yet", text.Output, StringComparison.Ordinal);
        Assert.Contains("Rendered proposal", text.Output, StringComparison.Ordinal);
        Assert.Contains(card.ProposalId, text.Output, StringComparison.Ordinal);
        Assert.Contains("proposed", text.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("ready", text.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pending", text.Output, StringComparison.OrdinalIgnoreCase);

        var json = RunCli("proposals", "--project", _fwDataPath, "--json");
        Assert.True(json.ExitCode == 0, json.Error + json.Output);
        var rendered = ProjectionJson.Deserialize<ProposalStandings>(json.Output)!;
        var jsonCard = Assert.Single(rendered.Proposals);
        Assert.Equal((card.Label, card.Status, card.Group), (jsonCard.Label, jsonCard.Status, jsonCard.Group));

        (int ExitCode, string Output, string Error) RunCli(params string[] arguments)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = Path.Combine(_workerRoot, "advanced-ai.json"),
                [RunnerOptions.RootVariable] = _workerRoot,
            };
            var exitCode = global::SIL.Motif.Cli.Program.Run(arguments, output, error, environment,
                static (_, _) => { });
            return (exitCode, output.ToString(), error.ToString());
        }
    }

    [Fact]
    public void Citation_TravelsFromAuthoringThroughFinalizeShowDryRunAndReopen_WithoutMovingTheDigest()
    {
        var target = CanonicalId.FromGuid(_seed.FirstSenseId).Value;
        var ws = NewLangProjFixture.AnalysisTag;
        string Authored(string name, OperationCitation? first, OperationCitation? second)
        {
            Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, name, name)).Succeeded);
            Assert.True(ProposalCommands.AddSetGloss(
                new AddSetGlossRequest(_fwDataPath, ProductVersion, name, target, ws, "draft gloss"), first).Succeeded);
            var rewritten = ProposalCommands.AddSetGloss(
                new AddSetGlossRequest(_fwDataPath, ProductVersion, name, target, ws, "cited gloss"), second);
            Assert.True(rewritten.Value!.ReplacedPriorValue);
            DraftRationale.Author(_fwDataPath, name, name, "Why " + name + " is right.");
            return ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, name)).Value!.ProposalId;
        }

        var cited = Authored("cited", OperationCitation.Create("Ashton 1944"),
            OperationCitation.Create("Steere 1870", "p. 142"));
        var uncited = Authored("uncited", OperationCitation.Create("Ashton 1944"), null);

        var shown = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, cited)).Value!;
        Assert.Equal(new CitationView("Steere 1870", "p. 142"), Assert.Single(shown.Operations).Citation);
        Assert.Contains("cites:     Steere 1870, p. 142", SIL.Motif.Projection.Rendering.CommandTextRenderer.Render(shown));
        var json = JsonNode.Parse(ProjectionJson.Serialize(shown))!["operations"]![0]!["citation"]!;
        Assert.Equal(("Steere 1870", "p. 142"), (json["source"]!.GetValue<string>(), json["locator"]!.GetValue<string>()));
        var unshown = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, uncited)).Value!;
        Assert.Null(Assert.Single(unshown.Operations).Citation);

        var dryRun = RunDryRun(cited);
        Assert.True(dryRun.Succeeded);
        Assert.Equal("Steere 1870", Assert.Single(dryRun.Value!.Operations).Citation?.Source);

        Assert.True(ProposalCommands.Reopen(new ReopenRequest(_fwDataPath, ProductVersion, "cited-again", cited)).Succeeded);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var reopened = JsonSerializer.Deserialize<SIL.Motif.Commands.Store.DraftDocument>(
            new ProposalRepository(database).GetDraft("cited-again").ProposalJson!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var citation = OperationCitation.FromProvenance(Assert.Single(reopened.Operations).Provenance);
        Assert.Equal("Steere 1870, p. 142", citation?.ToString());

        var digest = GetRecord(cited).IntentDigest;
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(_fwDataPath, ProductVersion, "cited-again", target,
            ws, "cited gloss"), OperationCitation.Create("Ashton 1944", "§ 12")).Value!.ReplacedPriorValue);
        var refinalized = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, "cited-again"));
        Assert.True(refinalized.Succeeded, refinalized.Refusal?.Code + ": " + refinalized.Refusal?.Message);
        Assert.Equal(digest, GetRecord(cited).IntentDigest);
        var recited = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, cited)).Value!;
        Assert.Equal(new CitationView("Ashton 1944", "§ 12"), Assert.Single(recited.Operations).Citation);
    }

    [Fact]
    public void ShowReadsADraftByNameOrProposalIdWithoutChangingItAndPreservesFinalizedDetails()
    {
        const string draftName = "show-before-finalize";
        var newDraft = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName,
            "A visible Draft"));
        Assert.True(newDraft.Succeeded, newDraft.Refusal?.Message);
        var created = ProposalCommands.ComposeAuthorPhoneme(new ComposeAuthorPhonemeRequest(
            _fwDataPath, ProductVersion, draftName,
            JsonSerializer.Serialize(new { name = "x", representations = new[] { "x" } })));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(DraftRationale.Write(_fwDataPath, ProductVersion, draftName,
            "Inspect this before Finalize.").Succeeded);
        var draftId = newDraft.Value!.ProposalId;
        var beforeShow = ReadDraftJson(draftName);
        var byName = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, draftName));
        var byId = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, draftId));

        Assert.True(byName.Succeeded, byName.Refusal?.Message);
        Assert.True(byId.Succeeded, byId.Refusal?.Message);
        Assert.Equal("draft", byName.Value!.Status);
        Assert.Null(byName.Value.CurrentIntentDigest);
        Assert.Equal(draftName, byName.Value.DraftName);
        Assert.Equal(DraftRevision.Compute(beforeShow), byName.Value.DraftRevision);
        Assert.Equal("A visible Draft", byName.Value.Label);
        Assert.Equal("Inspect this before Finalize.", byName.Value.Rationale!.Markdown);
        Assert.Equal(ProjectionJson.Serialize(byName.Value), ProjectionJson.Serialize(byId.Value));
        Assert.Equal(created.Value!.CreatedObjects.Select(item => item.Id),
            byName.Value.Operations.Where(operation => operation.EntityId is not null)
                .Select(operation => operation.EntityId));
        Assert.Equal(beforeShow, ReadDraftJson(draftName));

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var finalizedShow = ProposalCommands.Show(new ShowProposalRequest(
            _fwDataPath, ProductVersion, finalized.Value!.ProposalId));

        Assert.True(finalizedShow.Succeeded, finalizedShow.Refusal?.Message);
        Assert.Equal("proposed", finalizedShow.Value!.Status);
        Assert.Equal(finalized.Value.IntentDigest, finalizedShow.Value.CurrentIntentDigest);
        Assert.Null(finalizedShow.Value.DraftName);
        Assert.Null(finalizedShow.Value.DraftRevision);
        Assert.Equal("A visible Draft", finalizedShow.Value.Label);
        Assert.Equal("Inspect this before Finalize.", finalizedShow.Value.Rationale!.Markdown);
        Assert.Equal(byName.Value.Operations.Select(operation => (operation.Kind, operation.Target,
                operation.EntityId, operation.Citation)),
            finalizedShow.Value.Operations.Select(operation => (operation.Kind, operation.Target,
                operation.EntityId, operation.Citation)));
    }

    private string ReadDraftJson(string draftName)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).GetDraft(draftName).ProposalJson!;
    }

    [Fact]
    public void Citations_ListsEveryAppliedOperationWithItsSource_AndCanListOnlyTheUncitedOnes()
    {
        var first = CanonicalId.FromGuid(_seed.FirstSenseId).Value;
        var second = CanonicalId.FromGuid(_seed.SecondSenseId).Value;
        var ws = NewLangProjFixture.AnalysisTag;
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "unapplied", null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(_fwDataPath, ProductVersion, "unapplied", first,
            ws, "never applied"), OperationCitation.Create("Unapplied 2000")).Succeeded);
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "glosses", null)).Succeeded);
        var cited = ProposalCommands.AddSetGloss(new AddSetGlossRequest(_fwDataPath, ProductVersion, "glosses", first,
            ws, "cited gloss"), OperationCitation.Create("Steere 1870", "p. 142")).Value!.OperationId;
        var uncited = ProposalCommands.AddSetGloss(new AddSetGlossRequest(_fwDataPath, ProductVersion, "glosses", second,
            ws, "uncited gloss")).Value!.OperationId;
        DraftRationale.Author(_fwDataPath, "glosses", "Two glosses", "One cited, one not.");
        var id = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, "glosses")).Value!.ProposalId;
        Assert.Empty(ProposalCommands.Citations(new CitationsRequest(_fwDataPath, ProductVersion)).Value!.Rows);
        Assert.True(RunDryRun(id).Succeeded);
        var apply = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, id, Force: true));
        Assert.True(apply.Succeeded, apply.Refusal?.Message);
        var receiptCitation = Assert.Single(apply.Value!.Citations!);
        Assert.Equal((id, cited, "Steere 1870", "p. 142"),
            (receiptCitation.ProposalId, receiptCitation.OperationId, receiptCitation.Citation!.Source,
                receiptCitation.Citation.Locator));

        var all = ProposalCommands.Citations(new CitationsRequest(_fwDataPath, ProductVersion)).Value!;
        Assert.Equal((1, 2, 1), (all.AppliedProposals, all.Operations, all.Cited));
        var citedRow = Assert.Single(all.Rows, row => row.OperationId == cited);
        Assert.Equal((id, "Two glosses", "lexical/lexSense/setGloss", first),
            (citedRow.ProposalId, citedRow.Label, citedRow.Kind, citedRow.Target));
        Assert.Equal(new CitationView("Steere 1870", "p. 142"), citedRow.Citation);
        var onlyUncited = ProposalCommands.Citations(new CitationsRequest(_fwDataPath, ProductVersion, UncitedOnly: true)).Value!;
        Assert.Equal(uncited, Assert.Single(onlyUncited.Rows).OperationId);
        Assert.Equal(1, onlyUncited.Cited);
    }

    [Fact]
    public void ComposeCliFlagsRecordCitationOnEveryLoweredOperation()
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "cli-cited", null)).Succeeded);
        var intent = JsonSerializer.Serialize(new
        {
            entry = CanonicalId.FromGuid(_seed.FirstEntryId).Value,
            morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
            ws = "fr",
            text = "zzCitedForm",
            sense = CanonicalId.FromGuid(_seed.FirstSenseId).Value,
            glossWs = "en",
            glossText = "a cited gloss",
        });

        var response = CliInProcess.Run(_workerRoot, null, developerCommands: true,
            "compose-author-lexeme-form", "--project", _fwDataPath, "--draft", "cli-cited", "--intent", intent,
            "--cite-source", "Grammar 2026",
            "--cite-locator", "§ 3", "--json");

        Assert.Equal(0, response.ExitCode);
        var citations = ReadDraft("cli-cited").Operations
            .Select(operation => OperationCitation.FromProvenance(operation.Provenance))
            .OfType<OperationCitation>().ToArray();
        Assert.Equal(2, citations.Length);
        Assert.All(citations, citation => Assert.Equal(("Grammar 2026", "§ 3"),
            (citation.Source, citation.Locator)));
    }

    [Fact]
    public void TypedComposeVerbRunsEndToEndAndItsCitationFlagsReachEveryComposedOperation()
    {
        var (featStruc, feature, value) = SeedFeatureStructureWithClosedFeature();
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "typed-plain", null)).Succeeded);
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "typed-cited", null)).Succeeded);
        var intent = JsonSerializer.Serialize(new { featStruc, feature, value });

        var plain = CliInProcess.Run(_workerRoot, null, developerCommands: true,
            "compose-author-feature-value", "--project", _fwDataPath, "--draft", "typed-plain", "--intent", intent);
        var cited = CliInProcess.Run(_workerRoot, null, developerCommands: true,
            "compose-author-feature-value", "--project", _fwDataPath, "--draft", "typed-cited", "--intent", intent,
            "--cite-source", "Grammar 2026", "--cite-locator", "§ 7", "--json");

        Assert.True(plain.ExitCode == 0, plain.Output + plain.Error);
        Assert.Contains("3 operation", plain.Output, StringComparison.Ordinal);
        Assert.Contains("grammar/fsFeatStruc/createFeatureSpecs", plain.Output, StringComparison.Ordinal);
        Assert.Contains("grammar/fsClosedValue/setValue", plain.Output, StringComparison.Ordinal);
        Assert.True(cited.ExitCode == 0, cited.Output + cited.Error);
        var operations = ReadDraft("typed-cited").Operations;
        Assert.Equal(3, operations.Count);
        var citations = operations.Select(operation => OperationCitation.FromProvenance(operation.Provenance))
            .OfType<OperationCitation>().ToArray();
        Assert.Equal(3, citations.Length);
        Assert.All(citations, citation => Assert.Equal(("Grammar 2026", "§ 7"), (citation.Source, citation.Locator)));
        Assert.All(ReadDraft("typed-plain").Operations,
            operation => Assert.Null(OperationCitation.FromProvenance(operation.Provenance)));
    }

    private (string FeatStruc, string Feature, string Value) SeedFeatureStructureWithClosedFeature()
    {
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(_fwDataPath);
        var msa = (IMoStemMsa)cache.ServiceLocator.GetInstance<ILexSenseRepository>()
            .GetObject(_seed.FirstSenseId).MorphoSyntaxAnalysisRA;
        var featStrucGuid = Guid.NewGuid();
        Guid featureGuid = default, valueGuid = default;
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", cache.ServiceLocator.GetInstance<SIL.LCModel.Core.KernelInterfaces.IActionHandler>(), () =>
        {
            SIL.Motif.Runner.Operations.MoStemMsaMsFeaturesCreateLowering.Apply(cache, msa, featStrucGuid);
            var closed = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(closed);
            var symbolic = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            closed.ValuesOC.Add(symbolic);
            featureGuid = closed.Guid;
            valueGuid = symbolic.Guid;
        });
        loader.Save(cache);
        return (CanonicalId.FromGuid(featStrucGuid).Value, CanonicalId.FromGuid(featureGuid).Value,
            CanonicalId.FromGuid(valueGuid).Value);
    }

    [Fact]
    public void GlossAndDeleteCliFlagsRecordTheirSeparateCitations()
    {
        var glossDraft = "cli-gloss-cited";
        var deleteDraft = "cli-delete-cited";
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, glossDraft, null)).Succeeded);
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, deleteDraft, null)).Succeeded);

        var gloss = CliInProcess.Run(_workerRoot, null, developerCommands: true, "add-set-gloss", "--project",
            _fwDataPath, "--draft", glossDraft, "--target", CanonicalId.FromGuid(_seed.FirstSenseId).Value,
            "--ws", NewLangProjFixture.AnalysisTag, "--text", "cited gloss", "--cite-source", "Grammar 2026",
            "--cite-locator", "§ 4", "--json");
        var delete = CliInProcess.Run(_workerRoot, null, developerCommands: true, "add-delete-lexeme-form", "--project",
            _fwDataPath, "--draft", deleteDraft, "--target", CanonicalId.FromGuid(_seed.FirstEntryId).Value,
            "--cite-source", "Grammar 2026", "--cite-locator", "§ 5", "--json");

        Assert.Equal(0, gloss.ExitCode);
        Assert.Equal(0, delete.ExitCode);
        var glossCitation = OperationCitation.FromProvenance(Assert.Single(ReadDraft(glossDraft).Operations).Provenance);
        var deleteCitation = OperationCitation.FromProvenance(Assert.Single(ReadDraft(deleteDraft).Operations).Provenance);
        Assert.Equal(new CitationView("Grammar 2026", "§ 4"),
            new CitationView(glossCitation!.Source, glossCitation.Locator));
        Assert.Equal(new CitationView("Grammar 2026", "§ 5"),
            new CitationView(deleteCitation!.Source, deleteCitation.Locator));
    }

    private SIL.Motif.Commands.Store.DraftDocument ReadDraft(string draftName)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return JsonSerializer.Deserialize<SIL.Motif.Commands.Store.DraftDocument>(
            new ProposalRepository(database).GetDraft(draftName).ProposalJson!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public void FullLoop_New_AddSetGloss_Finalize_DryRun_Apply_Log_DrivesRealProjectEndToEnd()
    {
        var senseGuid = _seed.FirstSenseId;
        var wsTag = NewLangProjFixture.AnalysisTag;
        var originalGloss = SeededProject.FirstGloss;
        var canonicalId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(senseGuid);
        var newGloss = originalGloss + " (revised sense, Stage E CLI)";
        const string draftName = "stage-e-demo";
        const string label = "Stage E end-to-end demo";
        const string shortDescription = "Clarify the first sense gloss";
        const string extendedExplanation = "Replace the ambiguous gloss with the intended analysis.";

        // --- new ---
        var newResult = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, label));
        Assert.True(newResult.Succeeded);
        Assert.Equal(draftName, newResult.Value!.DraftName);
        Assert.Equal(label, newResult.Value.Label);
        var proposalId = newResult.Value.ProposalId;
        Assert.True(DraftExists(draftName));

        // --- add-set-gloss ---
        var addResult = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, canonicalId.Value, wsTag, newGloss));
        Assert.True(addResult.Succeeded);
        Assert.Equal(draftName, addResult.Value!.DraftName);
        Assert.Equal(canonicalId.Value, addResult.Value.Target);
        Assert.Equal(newGloss, addResult.Value.Text);
        Assert.Equal(1, addResult.Value.OperationCount);
        DraftRationale.Author(_fwDataPath, draftName, shortDescription, extendedExplanation);

        // --- finalize ---
        var finalizeResult = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalizeResult.Succeeded);
        Assert.False(DraftExists(draftName)); // draft cleared on finalize

        Assert.Equal(proposalId, finalizeResult.Value!.ProposalId);
        var intentDigest = finalizeResult.Value.IntentDigest;
        var committed = GetRecord(proposalId);
        Assert.Equal(intentDigest, committed.IntentDigest);
        Assert.Equal("proposed", committed.Status);

        // --- list ---
        var listResult = ProposalCommands.List(new ListProposalsRequest(_fwDataPath, ProductVersion));
        Assert.True(listResult.Succeeded);
        var listed = Assert.Single(listResult.Value!.Proposals);
        Assert.Equal(proposalId, listed.ProposalId);
        Assert.Equal("proposed", listed.Status);

        // --- show ---
        var showResult = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, proposalId));
        Assert.True(showResult.Succeeded);
        Assert.Equal(proposalId, showResult.Value!.ProposalId);
        Assert.Equal(shortDescription, showResult.Value.Label);
        Assert.Equal(extendedExplanation, showResult.Value.Rationale!.Markdown);
        var operation = Assert.Single(showResult.Value.Operations);
        Assert.Equal(canonicalId.Value, operation.Target);
        Assert.Equal("lexical/lexSense/setGloss", operation.Kind);
        var showJsonResult = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, proposalId));
        Assert.True(showJsonResult.Succeeded);
        Assert.Equal(shortDescription, showJsonResult.Value!.Label);
        Assert.Equal(extendedExplanation, showJsonResult.Value.Rationale!.Markdown);

        // --- dry-run: real before/after from LibLCM, non-mutating ---
        var dryRunResult = RunDryRun(proposalId);
        Assert.True(dryRunResult.Succeeded);
        var dryRunEffect = Assert.Single(dryRunResult.Value!.Effects);
        Assert.Equal(canonicalId.Value, dryRunEffect.CanonicalId);
        Assert.Equal("lexical/sense/gloss", dryRunEffect.Field);
        var dryRunChange = Assert.Single(dryRunEffect.Changes);
        Assert.Equal(originalGloss, dryRunChange.Before);
        Assert.Equal(newGloss, dryRunChange.After);
        Assert.StartsWith("sha256:", dryRunResult.Value.EffectDigest, StringComparison.Ordinal);

        // The dry run must not have mutated the project: gloss unchanged when re-read from disk.
        AssertGlossOnDisk(senseGuid, wsTag, originalGloss);

        // --- apply: real commit + save ---
        var applyResult = ProposalCommands.Apply(
            new ApplyRequest(_fwDataPath, ProductVersion, proposalId, Force: true));
        Assert.True(applyResult.Succeeded);
        Assert.False(applyResult.Value!.AlreadyApplied);
        Assert.Equal(proposalId, applyResult.Value.ProposalId);
        Assert.Null(applyResult.Value.Citations);
        Assert.Empty(applyResult.Value.AppliedLogEntry.User);
        var appliedEffect = Assert.Single(applyResult.Value.Effects);
        Assert.Equal(canonicalId.Value, appliedEffect.CanonicalId);
        var appliedChange = Assert.Single(appliedEffect.Changes);
        Assert.Equal(originalGloss, appliedChange.Before);
        Assert.Equal(newGloss, appliedChange.After);
        var appliedRecord = GetRecord(proposalId);
        Assert.Equal("applied", appliedRecord.Status);
        Assert.Equal(shortDescription, appliedRecord.Label);
        Assert.Equal(extendedExplanation, appliedRecord.Rationale);
        AssertReceiptCount(proposalId, intentDigest, 1);

        // Apply persists: re-open the saved project from disk and check the gloss + one applied-log entry.
        AssertGlossOnDisk(senseGuid, wsTag, newGloss);
        AssertAppliedLogEntryCount(1);
        AssertReceiptCount(proposalId, intentDigest, 1);

        // --- log ---
        var logResult = ProposalCommands.Log(new LogRequest(_fwDataPath));
        Assert.True(logResult.Succeeded);
        Assert.Equal(1, logResult.Value!.EntryCount);
        Assert.Empty(Assert.Single(logResult.Value.Entries).User);

        // --- apply again: idempotent, no duplicate log entry, no re-mutation ---
        var secondApplyResult = ProposalCommands.Apply(
            new ApplyRequest(_fwDataPath, ProductVersion, proposalId, Force: true));
        Assert.True(secondApplyResult.Succeeded);
        Assert.True(secondApplyResult.Value!.AlreadyApplied);

        AssertGlossOnDisk(senseGuid, wsTag, newGloss);
        AssertAppliedLogEntryCount(1);
    }

    [Fact]
    public void FileDryRun_PreparesOneScratchWithPrerequisites_AndReportsOnlyTheDependentEffect()
    {
        var target = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(_seed.FirstSenseId);
        var intermediateGloss = SeededProject.FirstGloss + " prerequisite file";
        var finalGloss = SeededProject.FirstGloss + " dependent file";
        var prerequisiteId = FinalizeSetGloss("file-prerequisite", target.Value, intermediateGloss);
        var dependentId = FinalizeSetGloss(
            "file-dependent", target.Value, finalGloss, prerequisiteId);

        var result = RunDryRun(dependentId);

        Assert.True(result.Succeeded);
        var effect = Assert.Single(result.Value!.Effects);
        Assert.Equal(intermediateGloss, Assert.Single(effect.Changes).Before);
        Assert.Equal(finalGloss, effect.Changes[0].After);
        AssertGlossOnDisk(_seed.FirstSenseId, NewLangProjFixture.AnalysisTag, SeededProject.FirstGloss);
    }

    [Fact]
    public async Task ApplyReconciliationNeeded_ReportsStoreInconsistentExitCode()
    {
        var senseGuid = _seed.FirstSenseId;
        var wsTag = NewLangProjFixture.AnalysisTag;
        var originalGloss = SeededProject.FirstGloss;
        var canonicalId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(senseGuid);
        var newGloss = originalGloss + " (receipt boundary test)";
        const string draftName = "receipt-boundary-demo";

        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, canonicalId.Value, wsTag, newGloss)).Succeeded);
        DraftRationale.Author(
            _fwDataPath, draftName, "Clarify the first sense gloss", "Record the intended analysis before applying the proposal.");
        var finalizeResult = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalizeResult.Succeeded);
        var proposalId = finalizeResult.Value!.ProposalId;

        Assert.True(RunDryRun(proposalId).Succeeded);

        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
                 { "apply", proposalId, "--project", _fwDataPath, "--force", "--json" })
            start.ArgumentList.Add(argument);
        var runner = IsolatedRunner.Options(Path.Combine(Path.GetTempPath(),
            "motif-reconciliation-worker-" + Guid.NewGuid().ToString("N")));
        start.Environment[RunnerOptions.RootVariable] = runner.Root;
        start.Environment[ProcessRunnerLauncher.ExecutableVariable] = runner.WorkerExecutable;
        start.Environment[RunnerOptions.NamespaceVariable] = runner.OwnerNamespace;
        start.Environment[RunnerOptions.IdleVariable] = "1";
        start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
        start.Environment["MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR"] = _fwDataPath;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60000));

        Assert.Equal(4, process.ExitCode);
        Assert.Empty(await output);
        var errorText = await error;
        using var failure = JsonDocument.Parse(errorText);
        Assert.Equal("apply.reconciliation-needed", failure.RootElement.GetProperty("code").GetString());
        Assert.Equal("StoreInconsistent", failure.RootElement.GetProperty("reason").GetString());

        // The load-bearing proof: the mutation genuinely committed and saved despite the report above.
        AssertGlossOnDisk(senseGuid, wsTag, newGloss);
        AssertAppliedLogEntryCount(1);

        // The store itself was left exactly as dry-run wrote it -- never touched by the failed write.
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }

    [Fact]
    public void UnknownCommands_And_MissingArguments_ReturnNonZeroWithClearErrors()
    {
        var missingDraft = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, "does-not-exist", "agent_AAECAwQFBgcICQoLDA0ODw", "en", "x"));
        Assert.False(missingDraft.Succeeded);
        Assert.Equal("draft.not-found", missingDraft.Refusal!.Code);
        Assert.Equal("does-not-exist", missingDraft.Refusal.Facts["draftName"]);

        var badTarget = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "bad-target-draft", null));
        Assert.True(badTarget.Succeeded);
        var invalidTarget = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, "bad-target-draft", "not-a-canonical-id", "en", "x"));
        Assert.False(invalidTarget.Succeeded);
        Assert.Equal("operation.invalid-target", invalidTarget.Refusal!.Code);
        Assert.Equal("not-a-canonical-id", invalidTarget.Refusal.Facts["target"]);
        DraftRationale.Author(
            _fwDataPath, "bad-target-draft", "Test an empty proposal", "Keep the no-operations refusal independently observable.");

        var emptyFinalize = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, "bad-target-draft"));
        Assert.False(emptyFinalize.Succeeded);
        Assert.Equal("draft.invalid", emptyFinalize.Refusal!.Code);
        Assert.Contains("no operations", emptyFinalize.Refusal.Message, StringComparison.Ordinal);

        var missingProposal = ProposalCommands.Show(new ShowProposalRequest(
            _fwDataPath, ProductVersion, "agent_AAECAwQFBgcICQoLDA0ODw"));
        Assert.False(missingProposal.Succeeded);
        Assert.Equal("proposal.not-found", missingProposal.Refusal!.Code);
        Assert.Equal(FailureReason.NotFound, missingProposal.Refusal.Reason);

        var missingProjectPath = Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "does-not-exist.fwdata");
        var missingProject = ProposalCommands.Log(new LogRequest(missingProjectPath));
        Assert.False(missingProject.Succeeded);
        Assert.Equal("project.not-found", missingProject.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, missingProject.Refusal.Reason);
    }

    [Fact]
    public void Apply_RecordsPersonModeWithTheLabelAsMessage_ByDefault_AndShowsThemInTheLog()
    {
        var id = FinalizeSetGloss("person-default", CanonicalId.FromGuid(_seed.FirstSenseId).Value, "person gloss");
        Assert.True(RunDryRun(id).Succeeded);

        var applied = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, id, Force: true));

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.Equal(("person", "Prepare a dependent gloss"),
            (applied.Value!.AppliedLogEntry.Mode, applied.Value.AppliedLogEntry.Message));
        var log = ProposalCommands.Log(new LogRequest(_fwDataPath)).Value!;
        Assert.Equal("person", Assert.Single(log.Entries).Mode);
        Assert.Contains("mode=person", SIL.Motif.Projection.Rendering.CommandTextRenderer.Render(log), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_InAutonomousModeRefusesWithoutAMessage_AndNeverForces()
    {
        var id = FinalizeSetGloss("autonomous", CanonicalId.FromGuid(_seed.FirstSenseId).Value, "agent gloss");
        Assert.True(RunDryRun(id).Succeeded);

        var noMessage = PendingChangesWorkflow.Apply(new ApplyRequest(_fwDataPath, ProductVersion, id, false,
            new ApplyAttribution(ApplyModes.Autonomous)));
        Assert.Equal("apply.message-required", noMessage.Refusal!.Code);

        var forced = PendingChangesWorkflow.Apply(new ApplyRequest(_fwDataPath, ProductVersion, id, true,
            new ApplyAttribution(ApplyModes.Autonomous, "Glossed from the corpus")));
        Assert.Equal("apply.autonomous-force", forced.Refusal!.Code);
        AssertAppliedLogEntryCount(0);
    }

    [Fact]
    public void ApplyCli_TheFlagAndTheEnvironmentMarkerDeclareAutonomous_EachNeedsAMessage_AndNeitherForces()
    {
        var first = FinalizeSetGloss("cli-flag", CanonicalId.FromGuid(_seed.FirstSenseId).Value, "flag gloss");
        Assert.True(RunDryRun(first).Succeeded);

        var noMessage = RunCli(null, "apply", first, "--project", _fwDataPath, "--autonomous");
        Assert.NotEqual(0, noMessage.ExitCode);
        Assert.Contains("autonomous Apply needs a --message", noMessage.Output + noMessage.Error, StringComparison.Ordinal);
        var envNoMessage = RunCli("1", "apply", first, "--project", _fwDataPath);
        Assert.Contains("autonomous Apply needs a --message", envNoMessage.Output + envNoMessage.Error, StringComparison.Ordinal);

        foreach (var (marker, extra) in new[] { ((string?)null, "--autonomous"), ("1", "--json") })
        {
            var forced = RunCli(marker, "apply", first, "--project", _fwDataPath, "--force", "--message", "m", extra);
            Assert.NotEqual(0, forced.ExitCode);
            Assert.Contains("never forces", forced.Output + forced.Error, StringComparison.Ordinal);
        }
        AssertAppliedLogEntryCount(0);
    }

    [Fact]
    public void ApplyCli_InPersonModeTheMessageOverridesTheLabelAndTheNoteIsOptIn()
    {
        var id = FinalizeSetGloss("cli-person", CanonicalId.FromGuid(_seed.SecondSenseId).Value, "person gloss");
        Assert.True(RunDryRun(id).Succeeded);

        var person = RunCli(null, "apply", id, "--project", _fwDataPath, "--force", "--message", "My words", "--note");
        Assert.True(person.ExitCode == 0, person.Error + person.Output);

        var entry = Assert.Single(ProposalCommands.Log(new LogRequest(_fwDataPath)).Value!.Entries);
        Assert.Equal(("person", "My words"), (entry.Mode, entry.Description));
        Assert.Contains("Message: My words", Assert.Single(NotebookNotes()));
    }

    private (int ExitCode, string Output, string Error) RunCli(string? autonomousEnv, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = Path.Combine(_workerRoot, "advanced-ai.json"),
            [RunnerOptions.RootVariable] = _workerRoot,
        };
        if (autonomousEnv is not null) environment[ApplyModes.EnvironmentVariable] = autonomousEnv;
        var exitCode = global::SIL.Motif.Cli.Program.Run(arguments, output, error, environment,
            static (_, _) => { });
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public void Apply_WithANoteWritesOneNotebookRecordOfTheMotifChangeType()
    {
        var target = CanonicalId.FromGuid(_seed.FirstSenseId).Value;
        var id = FinalizeSetGloss("note-one", target, "first noted gloss");
        Assert.True(RunDryRun(id).Succeeded);
        var noted = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, id, true,
            new ApplyAttribution(ApplyModes.Person, "kept it short", WriteNote: true)));
        Assert.True(noted.Succeeded, noted.Refusal?.Message);

        var note = Assert.Single(NotebookNotes());
        Assert.Contains("Message: kept it short", note);
        Assert.Contains("Receipt: Proposal " + id, note);
        Assert.Contains("Establish the lexical state required by later proposals.", note);
    }

    private List<string> NotebookNotes()
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(_fwDataPath);
        return cache.LangProject.ResearchNotebookOA.RecordsOC
            .Where(r => r.TypeRA?.Name.AnalysisDefaultWritingSystem.Text == "Motif change")
            .Select(r => string.Join("\n", r.DescriptionOA.ParagraphsOS.Cast<IStTxtPara>().Select(p => p.Contents.Text)))
            .ToList();
    }

    private CommandOutcome<DryRunProjection> RunDryRun(string proposalId) =>
        DryRunJobRunner.Run(_fwDataPath, ProductVersion, proposalId);

    private void AssertGlossOnDisk(Guid senseGuid, string wsTag, string expectedGloss)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(_fwDataPath);
        var wsHandle = cache.WritingSystemFactory.GetWsFromStr(wsTag);
        var senseRepo = cache.ServiceLocator.GetInstance<ILexSenseRepository>();
        Assert.Equal(expectedGloss, senseRepo.GetObject(senseGuid).Gloss.get_String(wsHandle).Text);
    }

    private void AssertAppliedLogEntryCount(int expectedCount)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(_fwDataPath);
        Assert.Equal(expectedCount, ProjectAppliedLog.ReadAll(cache).Count);
    }

    private string FinalizeSetGloss(
        string draftName, string targetId, string text, params string[] prerequisiteIds)
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, targetId, NewLangProjFixture.AnalysisTag, text)).Succeeded);
        if (prerequisiteIds.Length > 0)
        {
            using var database = ProjectMotifDatabase.Open(_fwDataPath);
            var repository = new ProposalRepository(database);
            var draftJson = repository.GetDraft(draftName).ProposalJson!;
            var draft = JsonNode.Parse(draftJson)!.AsObject();
            draft["requires"] = new JsonArray(
                prerequisiteIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
            repository.SaveDraft(draftName, draft.ToJsonString());
        }

        DraftRationale.Author(
            _fwDataPath, draftName, "Prepare a dependent gloss", "Establish the lexical state required by later proposals.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalized.Succeeded);
        return finalized.Value!.ProposalId;
    }

    private bool DraftExists(string draftName)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).DraftNameExists(draftName);
    }

    private ProposalRecord GetRecord(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
    }

    private string PairedDatabasePath()
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return database.FullPath;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingChangeFingerprintBlocksApplyEvenWithForce(bool removeExtensions)
    {
        var loader = new FwDataProjectLoader();
        Guid wordformGuid;
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            IWfiWordform wordform = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("unverified-word", cache.DefaultVernWs)));
            wordformGuid = wordform.Guid;
            loader.Save(cache);
        }

        const string draftName = "missing-change-fit";
        var created = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null));
        Assert.True(created.Succeeded);
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var repository = new ProposalRepository(database);
            var draft = JsonNode.Parse(repository.GetDraft(draftName).ProposalJson!)!.AsObject();
            draft["contractVersions"]!["analysis"] = "1.0";
            var operationId = CanonicalId.Mint().Value;
            var changeId = CanonicalId.Mint().Value;
            draft["operations"]!.AsArray().Add(new JsonObject
            {
                ["operationId"] = operationId,
                ["kind"] = "analysis/wfiWordform/setSpellingStatus",
                ["target"] = CanonicalId.FromGuid(wordformGuid).Value,
                ["after"] = new JsonObject { ["value"] = 2 },
                ["extensions"] = removeExtensions ? null : new JsonObject
                {
                    ["changeId"] = changeId,
                    ["changeFit"] = null,
                },
            });
            draft["composerProvenance"]!.AsArray().Add(new JsonObject
            {
                ["changeId"] = changeId,
                ["operationIds"] = new JsonArray(operationId),
            });
            repository.SaveDraft(draftName, draft.ToJsonString());
        }
        DraftRationale.Author(_fwDataPath, draftName, "Mark spelling incorrect", "The word is misspelled.");
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName)).Succeeded);
        Assert.True(RunDryRun(created.Value!.ProposalId).Succeeded);

        var preflight = ProposalCommands.Preflight(new PreflightRequest(
            _fwDataPath, ProductVersion, created.Value.ProposalId));
        Assert.True(preflight.Succeeded);
        var fit = Assert.Single(preflight.Value!.Changes);
        Assert.False(fit.StillFits);
        Assert.Contains("fingerprint", fit.Reason, StringComparison.OrdinalIgnoreCase);
        var apply = ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, created.Value.ProposalId, Force: true));
        Assert.Equal("apply.change-no-longer-fits", apply.Refusal?.Code);
        var bytes = File.ReadAllBytes(_fwDataPath);
        var cli = await CliInProcess.RunAsync(_workerRoot, null, developerCommands: false,
            "apply", created.Value.ProposalId, "--project", _fwDataPath, "--force", "--json");
        Assert.Equal(2, cli.ExitCode);
        Assert.Empty(cli.Output);
        Assert.Equal("apply.change-no-longer-fits", ProjectionJson.Deserialize<FailureEnvelope>(cli.Error)!.Code);
        Assert.Equal(bytes, File.ReadAllBytes(_fwDataPath));
    }

    [Fact]
    public void DeletedWordform_IsNoLongerFitAndBlocksApplyEvenWithForce()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformGuid;
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            IWfiWordform wordform = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("fit-word", cache.DefaultVernWs)));
            wordformGuid = wordform.Guid;
            loader.Save(cache);
        }

        const string draftName = "fit-deleted-word";
        var created = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null));
        Assert.True(created.Succeeded);
        var operationId = CanonicalId.Mint();
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var repository = new ProposalRepository(database);
            var draft = JsonNode.Parse(repository.GetDraft(draftName).ProposalJson!)!.AsObject();
            draft["contractVersions"]!["analysis"] = "1.0";
            draft["operations"]!.AsArray().Add(new JsonObject
            {
                ["operationId"] = operationId.Value,
                ["kind"] = "analysis/wfiWordform/setSpellingStatus",
                ["target"] = CanonicalId.FromGuid(wordformGuid).Value,
                ["after"] = new JsonObject { ["value"] = 2 },
                ["extensions"] = new JsonObject { ["changeFit"] = new JsonObject
                {
                    ["wordformId"] = CanonicalId.FromGuid(wordformGuid).Value,
                    ["wordformForm"] = "fit-word",
                    ["baselineToken"] = "captured-baseline",
                } },
            });
            repository.SaveDraft(draftName, draft.ToJsonString());
        }
        DraftRationale.Author(_fwDataPath, draftName, "Mark spelling incorrect", "The selected word is misspelled.");
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName)).Succeeded);
        Assert.True(RunDryRun(created.Value!.ProposalId).Succeeded);

        using (var cache = loader.LoadCache(_fwDataPath))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformGuid);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => wordform.Delete());
            loader.Save(cache);
        }

        var preflight = ProposalCommands.Preflight(new PreflightRequest(
            _fwDataPath, ProductVersion, created.Value.ProposalId));
        Assert.True(preflight.Succeeded);
        var fit = Assert.Single(preflight.Value!.Changes);
        Assert.False(fit.StillFits);
        Assert.Contains("deleted", fit.Reason, StringComparison.OrdinalIgnoreCase);
        var textPreflight = RunPreflightCli(created.Value.ProposalId, false);
        Assert.Contains("no longer fits", textPreflight, StringComparison.Ordinal);
        var jsonPreflight = JsonNode.Parse(RunPreflightCli(created.Value.ProposalId, true))!;
        Assert.False(jsonPreflight["changes"]![0]!["stillFits"]!.GetValue<bool>());
        var apply = ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, created.Value.ProposalId, Force: true));
        Assert.False(apply.Succeeded);
        Assert.Equal("apply.change-no-longer-fits", apply.Refusal!.Code);
    }

    [Fact]
    public void HeldProject_ApplyIsBusyAndProposalRemainsPendingForRetry()
    {
        var proposalId = FinalizeSetGloss("held-project",
            CanonicalId.FromGuid(_seed.FirstSenseId).Value, "gloss after release");
        Assert.True(RunDryRun(proposalId).Succeeded);

        using (var held = new FileStream(_fwDataPath + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None))
        {
            var refused = ProposalCommands.Apply(new ApplyRequest(
                _fwDataPath, ProductVersion, proposalId, Force: true));
            Assert.False(refused.Succeeded);
            Assert.Equal("apply.project-in-use", refused.Refusal!.Code);
            Assert.Equal(FailureReason.Busy, refused.Refusal.Reason);
            Assert.Equal("proposed", GetRecord(proposalId).Status);
        }

        var retried = ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, proposalId, Force: true));
        Assert.True(retried.Succeeded);
        Assert.Equal("applied", GetRecord(proposalId).Status);
    }

    [Fact]
    public async Task ApplyRefusesAnIdLoggedWithDifferentContentWithoutClaimingRollback()
    {
        var proposalId = FinalizeSetGloss("logged-elsewhere",
            CanonicalId.FromGuid(_seed.FirstSenseId).Value, "new gloss");
        Assert.True(RunDryRun(proposalId).Succeeded);
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                ProjectAppliedLog.WriteEntry(cache, new AppliedLogEntry(
                    CanonicalId.Parse(proposalId).ToGuid(), 1, "20260925T000000Z",
                    "other", new string('a', 64), "prior intent")));
            loader.Save(cache);
        }

        var refused = ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, proposalId, Force: true));

        Assert.False(refused.Succeeded);
        Assert.Equal("apply.applied-content-mismatch", refused.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, refused.Refusal.Reason);
        Assert.Contains("different content", refused.Refusal.Message);
        Assert.DoesNotContain("rollback", refused.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no longer trustworthy", refused.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        AssertAppliedLogEntryCount(1);
        AssertReceiptCount(proposalId, GetRecord(proposalId).IntentDigest!, 0);

        var start = new ProcessStartInfo(BuildOutput.Cli);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        var applied = CliInProcess.Run(start, "apply", proposalId, "--project", _fwDataPath, "--force", "--json");
        Assert.Equal(2, applied.ExitCode);
        Assert.Empty(applied.Output);
        var failure = JsonNode.Parse(applied.Error)!;
        Assert.Equal("apply.applied-content-mismatch", failure["code"]?.GetValue<string>());
        Assert.DoesNotContain("rollback", failure["message"]?.GetValue<string>() ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingChangesKeepTheChosenReadingAndDistinctWordformSlots()
    {
        var loader = new FwDataProjectLoader();
        const string word = "parser-change";
        Guid wordformGuid;
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            IWfiWordform wordform = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
                var stored = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(stored);
                for (var index = 0; index < 2; index++)
                {
                    var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                    stored.MorphBundlesOS.Add(bundle);
                    bundle.MorphRA = entry.LexemeFormOA;
                    bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
                }
            });
            wordformGuid = wordform.Guid;
            loader.Save(cache);
        }
        var managedRoot = Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "managed-parser-change");
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_fwDataPath), managedRoot);
        Assert.True(captured.Succeeded);
        string formGuid;
        string msaGuid;
        using (var cache = loader.LoadScratchCache(_fwDataPath))
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
            formGuid = entry.LexemeFormOA!.Guid.ToString("D");
            msaGuid = entry.MorphoSyntaxAnalysesOC.First().Guid.ToString("D");
        }
        var assessmentId = CanonicalId.Mint().Value;
        var baselineToken = JsonSerializer.Serialize(captured.Value!.Token);
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: null,
            ProposalIntentDigest: null,
            Assessor: "test",
            Kind: "ParseTime",
            ScopeJson: "{}",
            ScopeDigest: "sha256:scope",
            Subject: new AssessmentSubject.Words(Selection.Create("parser-change", [word]), "whitespace-and-punctuation", "1"),
            BaselineToken: baselineToken,
            OutcomeDigest: "sha256:outcome",
            SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar",
            ModelFingerprint: "model",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words([new AssessedWord(word, "parsed", [], 0)
                {
                    Morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, word, 0,
                        false, false, false,
                        [new ParseAnalysis([new ParseMorph(formGuid, msaGuid, null, null)]),
                         new ParseAnalysis([new ParseMorph(formGuid, msaGuid, null, "chosen-second")])], []),
                }], null, null, [])
        ));
        }
        var pending = PendingChanges.Load(new PendingChangesRequest(_fwDataPath, ProductVersion));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var secondReading = new ParseAnalysis([new ParseMorph(formGuid, msaGuid, null, "chosen-second")]);
        var prefixedWordformId = "wf-" + CanonicalId.FromGuid(wordformGuid).Value;
        var wrongIndex = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            pending.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "approve",
                CanonicalId.FromGuid(wordformGuid).Value, word, assessmentId, secondReading,
                ReadingIndex: 0)));
        Assert.Equal("change.reading-missing", wrongIndex.Refusal?.Code);
        var candidateChange = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            pending.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "add-candidate",
                prefixedWordformId, word, assessmentId, secondReading)));
        Assert.True(candidateChange.Succeeded, candidateChange.Refusal?.Message);
        var review = Assert.Single(candidateChange.Value!.Changes);
        Assert.Equal(3, review.Analyses.Count);
        Assert.True(review.Analyses[1].Touched);
        Assert.False(review.Analyses[1].Stored);
        Assert.NotEmpty(review.Analyses[1].Reading.Morphs[0].Form);
        Assert.True(review.Analyses[2].Stored);
        Assert.Equal("candidate", review.Analyses[2].Opinion);
        Assert.Equal(2, review.Analyses[2].Reading.Morphs.Count);
        var sameReadingOpinion = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            candidateChange.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "approve",
                prefixedWordformId, word, assessmentId, secondReading,
                ReadingIndex: 1)));
        Assert.True(sameReadingOpinion.Succeeded, sameReadingOpinion.Refusal?.Message);
        Assert.Single(sameReadingOpinion.Value!.Changes);
        Assert.NotEqual(candidateChange.Value.Revision, sameReadingOpinion.Value.Revision);
        var replacementJson = JsonNode.Parse(SIL.Motif.Cli.Rendering.ProposalCommandRenderer
            .Render(sameReadingOpinion, asJson: true).Output)!;
        Assert.Equal(candidateChange.Value.Changes[0].ChangeId,
            replacementJson["replacedChangeId"]?.GetValue<string>());
        Assert.Contains("Replaced pending change", SIL.Motif.Cli.Rendering.ProposalCommandRenderer
            .Render(sameReadingOpinion, asJson: false).Output);
        var bulk = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            sameReadingOpinion.Value.Revision, new ChangeIntent(CanonicalId.Mint().Value, "add-candidate",
                CanonicalId.FromGuid(wordformGuid).Value, word, assessmentId, secondReading)));
        Assert.True(bulk.Succeeded, bulk.Refusal?.Message);
        Assert.Equal(sameReadingOpinion.Value.Revision, bulk.Value!.Revision);
        Assert.Equal("approve", Assert.Single(bulk.Value.Changes).Kind);
        Assert.Equal(word, bulk.Value.SkippedWord);
        Assert.Contains("Skipped", SIL.Motif.Cli.Rendering.ProposalCommandRenderer
            .Render(bulk, asJson: false).Output);
        var returned = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            bulk.Value.Revision, new ChangeIntent(CanonicalId.Mint().Value, "candidate",
                CanonicalId.FromGuid(wordformGuid).Value, word, assessmentId, secondReading,
                ReadingIndex: 1)));
        Assert.True(returned.Succeeded, returned.Refusal?.Message);
        Assert.Empty(returned.Value!.Changes);
        Assert.NotEqual(bulk.Value.Revision, returned.Value.Revision);
        Assert.Equal(sameReadingOpinion.Value.Changes[0].ChangeId, returned.Value.CancelledChangeId);
        Assert.Null(returned.Value.ReplacedChangeId);
        var cancellationJson = JsonNode.Parse(SIL.Motif.Cli.Rendering.ProposalCommandRenderer
            .Render(returned, asJson: true).Output)!;
        Assert.Equal(returned.Value.CancelledChangeId,
            cancellationJson["cancelledChangeId"]?.GetValue<string>());
        Assert.Contains("Cancelled pending change", SIL.Motif.Cli.Rendering.ProposalCommandRenderer
            .Render(returned, asJson: false).Output);
        var afterReturn = returned.Value;
        foreach (var kind in new[] { "reject", "add-candidate" })
        {
            var selectedId = CanonicalId.Mint().Value;
            var selected = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
                afterReturn.Revision, new ChangeIntent(selectedId, kind,
                    CanonicalId.FromGuid(wordformGuid).Value, word, assessmentId, secondReading,
                    ReadingIndex: 1)));
            Assert.True(selected.Succeeded, selected.Refusal?.Message);
            Assert.Single(selected.Value!.Changes);
            var cancelled = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
                selected.Value.Revision, new ChangeIntent(CanonicalId.Mint().Value, "candidate",
                    CanonicalId.FromGuid(wordformGuid).Value, word, assessmentId, secondReading,
                    ReadingIndex: 1)));
            Assert.True(cancelled.Succeeded, cancelled.Refusal?.Message);
            Assert.Empty(cancelled.Value!.Changes);
            Assert.Equal(selectedId, cancelled.Value.CancelledChangeId);
            afterReturn = cancelled.Value;
        }
        var priorCandidateId = CanonicalId.Mint().Value;
        var priorCandidate = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            afterReturn.Revision, new ChangeIntent(priorCandidateId, "add-candidate",
                CanonicalId.FromGuid(wordformGuid).Value, word, assessmentId, secondReading,
                ReadingIndex: 1)));
        Assert.True(priorCandidate.Succeeded, priorCandidate.Refusal?.Message);
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var repository = new ProposalRepository(database);
            var draft = JsonNode.Parse(repository.GetDraft(PendingChanges.DraftName).ProposalJson!)!;
            draft["composerProvenance"]![0]!["kind"] = "candidate";
            repository.SaveDraft(PendingChanges.DraftName, draft.ToJsonString());
        }
        var withPriorCandidate = PendingChanges.Load(new PendingChangesRequest(_fwDataPath, ProductVersion));
        var cancelledCandidate = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            withPriorCandidate.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "candidate",
                CanonicalId.FromGuid(wordformGuid).Value, word, assessmentId, secondReading,
                ReadingIndex: 1)));
        Assert.True(cancelledCandidate.Succeeded, cancelledCandidate.Refusal?.Message);
        Assert.Empty(cancelledCandidate.Value!.Changes);
        Assert.Equal(priorCandidateId, cancelledCandidate.Value.CancelledChangeId);
        afterReturn = cancelledCandidate.Value;
        var spellingChange = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            afterReturn.Revision, new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformGuid).Value, word)));
        Assert.True(spellingChange.Succeeded, spellingChange.Refusal?.Message);
        Assert.Single(spellingChange.Value!.Changes);
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var pendingDraft = JsonNode.Parse(new ProposalRepository(database)
                .GetDraft(PendingChanges.DraftName).ProposalJson!)!;
            Assert.Single(pendingDraft["operations"]!.AsArray());
            Assert.Equal("analysis/wfiWordform/setSpellingStatus",
                pendingDraft["operations"]![0]!["kind"]!.GetValue<string>());
        }
    }

    private void AssertReceiptCount(string proposalId, string intentDigest, long expectedCount)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $id AND IntentDigest = $digest;";
        command.Parameters.AddWithValue("$id", proposalId);
        command.Parameters.AddWithValue("$digest", intentDigest);
        Assert.Equal(expectedCount, (long)command.ExecuteScalar()!);
    }

    private string RunPreflightCli(string proposalId, bool json)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        var result = CliInProcess.RunCommandLine(start,
            $"preflight --project \"{_fwDataPath}\" {proposalId}" + (json ? " --json" : ""));
        Assert.Equal(0, result.ExitCode);
        return result.Output;
    }

}
