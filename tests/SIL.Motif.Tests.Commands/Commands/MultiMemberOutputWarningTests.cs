using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class MultiMemberOutputWarningTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private static ComposeStepRequest Step(string verb, object intent, string label) =>
        new(verb, JsonSerializer.SerializeToElement(intent), label, null);

    private static object Rule(object output) => new
    {
        name = "l to output", direction = "simultaneous",
        input = new[] { new { phoneme = "@l:phonemes" } }, output = new[] { output },
        left = new[] { new { phoneme = "@a:phonemes" } }, right = new[] { new { phoneme = "@a:phonemes" } },
    };

    private ComposeStepRequest[] Steps(object rule) =>
    [
        Step("compose-author-phoneme", new { name = "l", representations = new[] { "l" } }, "l"),
        Step("compose-author-phoneme", new { name = "w", representations = new[] { "w" } }, "w"),
        Step("compose-author-phoneme", new { name = "a", representations = new[] { "a" } }, "a"),
        Step("compose-author-natural-class", new { name = "lw", abbreviation = "LW", members = new[] { "@l:phonemes", "@w:phonemes" } }, "lw"),
        Step("compose-author-natural-class", new { name = "just w", abbreviation = "JW", members = new[] { "@w:phonemes" } }, "jw"),
        Step("compose-author-phonological-rule", rule, "rule"),
    ];

    private ComposeResult Compose(object output)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "warn", "Warn")).Succeeded);
        var composed = ProposalCommands.Compose(new(path, Version, "warn", Steps(Rule(output)), null));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        return new(composed.Value!.Intents[^1].RelatedObjects ?? []);
    }

    private sealed record ComposeResult(IReadOnlyList<SIL.Motif.Contract.Responses.ComposedRelatedObject> Related);

    [Fact]
    public void AMultiMemberSegmentListOutputWarnsAndPointsToAlternateFormsOrAFeatureClass()
    {
        var warning = Assert.Single(Compose(new { naturalClass = "@lw:naturalclasses" }).Related);

        Assert.Equal("multi-member-output", warning.Kind);
        Assert.Contains("union of the members' features", warning.Name, StringComparison.Ordinal);
        Assert.Contains("alternate form", warning.Name, StringComparison.Ordinal);
        Assert.Contains("feature-class Output", warning.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleMemberClassAndALiteralPhonemeOutputDoNotWarn()
    {
        Assert.Empty(Compose(new { naturalClass = "@jw:naturalclasses" }).Related);
        Assert.Empty(Compose(new { phoneme = "@w:phonemes" }).Related);
    }

    [Fact]
    public void EditingASavedRuleToAMultiMemberSegmentListOutputWarns()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "seed", "Seed")).Succeeded);
        var seeded = ProposalCommands.Compose(new(path, Version, "seed", Steps(Rule(new { naturalClass = "@jw:naturalclasses" })), null));
        Assert.True(seeded.Succeeded, seeded.Refusal?.Message);
        string Created(string kind) => seeded.Value!.Intents.SelectMany(intent => intent.Operations)
            .First(operation => operation.Kind == kind).EntityId!;
        DraftRationale.Author(path, "seed", "seed", "Save a rule with a single-member class Output.");
        var finalized = ProposalCommands.Finalize(new(path, Version, "seed"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        Assert.True(ProposalCommands.Apply(new(path, Version, finalized.Value.ProposalId, Force: true)).Succeeded);

        Assert.True(ProposalCommands.New(new(path, Version, "edit", "Edit")).Succeeded);
        var edited = ProposalCommands.ComposeIntent(new(path, Version, "edit", "compose-edit-phonological-rule",
            JsonSerializer.Serialize(new
            {
                rule = Created("grammar/phPhonData/createPhonRules"),
                output = new[] { new { naturalClass = Created("grammar/phPhonData/createNaturalClasses") } },
            })));
        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        var warning = Assert.Single(edited.Value!.RelatedObjects ?? []);
        Assert.Equal("multi-member-output", warning.Kind);
        Assert.Contains("union of the members' features", warning.Name, StringComparison.Ordinal);
    }
}
