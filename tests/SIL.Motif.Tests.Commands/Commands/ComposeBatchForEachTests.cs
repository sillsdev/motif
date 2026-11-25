using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A compose-batch intent with forEach expands to one intent per target inside one Draft.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ComposeBatchForEachTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private string FirstEntry => CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value;
    private string SecondEntry => CanonicalId.FromGuid(pristine.Seed.SecondEntryId).Value;

    private (string Path, string Vernacular, string Analysis) NewProject(string draft)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, draft, "forEach")).Succeeded);
        return (path, vernacular, analysis);
    }

    // The Tamil lane's "add a Verb MSA to N entries": a new category, then one add-MSA intent per entry.
    private static string AddMsaBatch(string analysis, string targets) => $$$"""
        {"intents":[
          {"verb":"compose-author-part-of-speech","as":"verb","intent":{"name":"Verb","abbreviation":"v","ws":"{{{analysis}}}"}},
          {"verb":"compose-add-morphosyntax-analysis",
           "forEach":{ {{{targets}}} },
           "intent":{"entry":"{target.id}","msa":{"kind":"stem","category":"@verb"},"gloss":"verb use of {target.form}","analysisWs":"{{{analysis}}}"},
           "citation":{"source":"Tamil grammar","locator":"{target}"}}
        ]}
        """;

    [Fact]
    public void ForEachExpandsInTargetOrderAndRecordsWhereEachIntentCameFrom()
    {
        var (path, _, analysis) = NewProject("msa");
        var batch = ProposalCommands.ComposeBatch(new(path, Version, "msa", AddMsaBatch(analysis,
            $"\"targets\":[\"{SecondEntry}\",\"{SeededProject.FirstForm}\"]")));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        Assert.Equal(3, batch.Value!.Intents.Count);
        var draft = ReadDraft(path, "msa");
        var expanded = draft.ComposerProvenance.Where(item => item.TryGetProperty("expandedFrom", out _)).ToArray();
        Assert.Equal([SecondEntry, SeededProject.FirstForm], expanded.Select(item => item.GetProperty("target").GetString()));
        Assert.All(expanded, item => Assert.Equal(1, item.GetProperty("expandedFrom").GetInt32()));
        Assert.Equal([SecondEntry, FirstEntry],
            expanded.Select(item => item.GetProperty("input").GetProperty("entry").GetString()));
        Assert.Contains("verb use of motifb", JsonSerializer.Serialize(expanded[0].GetProperty("input")));
        Assert.Contains("verb use of motifa", JsonSerializer.Serialize(expanded[1].GetProperty("input")));
        var cited = draft.Operations.Select(operation => OperationCitation.FromProvenance(operation.Provenance))
            .OfType<OperationCitation>().Select(citation => citation.Locator).Distinct().Order().ToArray();
        Assert.Equal(new[] { SecondEntry, SeededProject.FirstForm }.Order(StringComparer.Ordinal), cited.Order(StringComparer.Ordinal));
    }

    // The Tamil lane's "same environment on N allomorphs": one environment, one alternate form per entry.
    [Fact]
    public void OneEnvironmentCanBePutOnManyAllomorphs()
    {
        var (path, vernacular, _) = NewProject("env");
        var stem = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value;
        var targets = Path.Combine(Path.GetTempPath(), "forEach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targets);
        try
        {
            File.WriteAllText(Path.Combine(targets, "entries.txt"), $"\n{FirstEntry}\n\"{SecondEntry}\"\n\n");
            var batch = ProposalCommands.ComposeBatch(new(path, Version, "env", $$$"""
                {"intents":[
                  {"verb":"compose-author-phoneme","intent":{"name":"a","representations":["zz"]},"as":"ph"},
                  {"verb":"compose-author-environment","as":"env","intent":{"name":"after zz","left":[{"phoneme":"@ph:phonemes"}],"right":[]}},
                  {"verb":"compose-author-alternate-form","forEach":{"targetsFile":"entries.txt"},
                   "intent":{"entry":"{target}","form":"x","formWs":"{{{vernacular}}}","morphType":"{{{stem}}}","environments":["@env"]}}
                ]}
                """, targets));

            Assert.True(batch.Succeeded, batch.Refusal?.Message);
            var forms = ReadDraft(path, "env").Operations.Count(operation => operation.Kind.EndsWith("/createAlternateForms"));
            Assert.Equal(2, forms);
        }
        finally
        {
            Directory.Delete(targets, true);
        }
    }

    [Fact]
    public void AnUnresolvedTargetIsRefusedByItselfAndNamesTheOthersAsFine()
    {
        var (path, _, analysis) = NewProject("unresolved");
        var missing = CanonicalId.FromGuid(Guid.NewGuid()).Value;

        var batch = ProposalCommands.ComposeBatch(new(path, Version, "unresolved", AddMsaBatch(analysis,
            $"\"targets\":[\"{FirstEntry}\",\"{missing}\",\"no such form\",\"{SecondEntry}\"]")));

        Assert.False(batch.Succeeded);
        Assert.Equal(RefusalCodes.DraftForEachTargetUnresolved, batch.Refusal!.Code);
        Assert.Equal("2", batch.Refusal.Facts["problemCount"]);
        Assert.Equal("2,3", batch.Refusal.Facts["problemIntents"]);
        Assert.Contains($"target '{missing}' of forEach intent 1", batch.Refusal.Message);
        Assert.Contains("target 'no such form' of forEach intent 1", batch.Refusal.Message);
        Assert.DoesNotContain(SecondEntry, batch.Refusal.Message);
        Assert.Contains("discarded", batch.Refusal.Message);
    }

    [Fact]
    public void ARefusedIntentDoesNotHideTheOtherTargetsRefusals()
    {
        var (path, _, analysis) = NewProject("composer");
        var bad = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;

        var batch = ProposalCommands.ComposeBatch(new(path, Version, "composer", $$$"""
            {"intents":[
              {"verb":"compose-author-part-of-speech","as":"verb","intent":{"name":"Verb","abbreviation":"v","ws":"{{{analysis}}}"}},
              {"verb":"compose-add-morphosyntax-analysis","forEach":{"targets":["{{{bad}}}","{{{FirstEntry}}}","{{{bad}}}"]},
               "intent":{"entry":"{target}","msa":{"kind":"stem","category":"@verb"},"gloss":"g","analysisWs":"{{{analysis}}}"}}
            ]}
            """));

        Assert.False(batch.Succeeded);
        Assert.Equal("draft.invalid", batch.Refusal!.Code);
        Assert.Equal("2", batch.Refusal.Facts["problemCount"]);
        Assert.Equal("1,3", batch.Refusal.Facts["problemIntents"]);
        Assert.Contains("Intent 1 (compose-add-morphosyntax-analysis, target", batch.Refusal.Message);
        Assert.Contains("Intent 3 (compose-add-morphosyntax-analysis, target", batch.Refusal.Message);
        Assert.DoesNotContain("Intent 2", batch.Refusal.Message);
        Assert.Contains("Nothing in this batch was added", batch.Refusal.Message);
    }

    [Theory]
    [InlineData("""{"intents":[{"verb":"compose-author-phoneme","forEach":{"targets":[]},"intent":{"name":"{target}","representations":["a"]}}]}""",
        RefusalCodes.DraftForEachEmpty)]
    [InlineData("""{"intents":[{"verb":"compose-author-phoneme","forEach":{"targets":["a"]},"intent":{"name":"{item}","representations":["a"]}}]}""",
        RefusalCodes.DraftForEachUnknownPlaceholder)]
    [InlineData("""{"intents":[{"verb":"compose-author-phoneme","forEach":{"targets":["a"]},"intent":{"name":"{target.gloss}","representations":["a"]}}]}""",
        RefusalCodes.DraftForEachUnknownPlaceholder)]
    [InlineData("""{"intents":[{"verb":"compose-author-phoneme","intent":{"name":"{target}","representations":["a"]}}]}""",
        RefusalCodes.DraftForEachPlaceholderWithoutForEach)]
    [InlineData("""{"intents":[{"verb":"compose-author-phoneme","forEach":{"targets":["a"]},"intent":{"name":"{target}","representations":["a"],"forEach":{"targets":["b"]}}}]}""",
        RefusalCodes.DraftForEachNested)]
    [InlineData("""{"intents":[{"verb":"compose-author-phoneme","forEach":{"targets":["a"],"targetsFile":"x"},"intent":{"name":"{target}","representations":["a"]}}]}""",
        RefusalCodes.DraftInvalid)]
    [InlineData("""{"intents":[{"verb":"compose-author-phoneme","forEach":{"targetsFile":"missing-targets.txt"},"intent":{"name":"{target}","representations":["a"]}}]}""",
        RefusalCodes.DraftInvalid)]
    [InlineData("""{"intents":[{"verb":"compose-author-phoneme","as":"p","forEach":{"targets":["a"]},"intent":{"name":"{target}","representations":["a"]}}]}""",
        RefusalCodes.DraftInvalid)]
    public void ForEachMisuseIsRefusedWithItsOwnCodeAndStagesNothing(string json, string code)
    {
        var (path, _, _) = NewProject("misuse");
        var refused = ProposalCommands.ComposeBatch(new(path, Version, "misuse", json));

        Assert.False(refused.Succeeded);
        Assert.Equal(code, refused.Refusal!.Code);
        Assert.Equal("0", refused.Refusal.Facts["intentIndex"]);
    }

    [Fact]
    public void AnIntentWithoutForEachIsUntouchedByExpansion()
    {
        var (path, _, _) = NewProject("plain");
        var batch = ProposalCommands.ComposeBatch(new(path, Version, "plain", """
            {"intents":[{"verb":"compose-author-phoneme","intent":{"name":"{brace}","representations":["q"]}}]}
            """));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        Assert.DoesNotContain(ReadDraft(path, "plain").ComposerProvenance, item => item.TryGetProperty("expandedFrom", out _));
    }

    private static DraftDocument ReadDraft(string path, string draft)
    {
        var loaded = ProjectStoreCommand.Run(path, Version, (database, _) =>
            CommandOutcome<DraftDocument>.Success(ProposalCommands.DeserializeDraft(
                new ProposalRepository(database).GetDraft(draft).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        return loaded.Value!;
    }
}

