using SIL.Motif.Commands;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class ComposerRegistryTests
{
    [Fact]
    public void ATypedDefinitionAndTheLegacyTableShareOneVerbSpace()
    {
        var verbs = ProposalCommands.DraftComposers.Keys;
        Assert.Contains("compose-author-feature-value", verbs);
        Assert.Contains("compose-author-phoneme", verbs);
        Assert.Equal("AuthorFeatureValue", ProposalCommands.DraftComposers["compose-author-feature-value"].Composer);
        Assert.Equal(verbs.Count(), verbs.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AVerbDeclaredByATypedDefinitionAndALegacyComposerThrowsAtConstruction()
    {
        var legacy = new DraftComposer("compose-author-feature-value", "Other", (_, _, _) => []);
        var failure = Assert.Throws<InvalidOperationException>(() =>
            ComposerRegistry.Build(ComposerDefinitions.All, [legacy]));
        Assert.Contains("compose-author-feature-value", failure.Message);
    }

    [Fact]
    public void TwoTypedDefinitionsOfOneVerbThrowAtConstruction()
    {
        var definition = new AuthorFeatureValueDefinition();
        Assert.Throws<InvalidOperationException>(() => ComposerRegistry.Build([definition, definition], []));
    }

    [Theory]
    [InlineData("compose-author-feature-value", "motif_add_feature_value")]
    [InlineData("compose-edit-natural-class", "motif_edit_natural_class")]
    [InlineData("compose-author-affix-slot", "motif_add_affix_slot")]
    public void TheToolNameFollowsTheVerb(string verb, string tool) =>
        Assert.Equal(tool, SIL.Motif.Commands.Catalog.ComposerDescriptor.ToolNameFor(verb));

    [Fact]
    public void AVerbOutsideTheNamingRuleNeedsAnExplicitToolName() =>
        Assert.Throws<InvalidOperationException>(() =>
            SIL.Motif.Commands.Catalog.ComposerDescriptor.ToolNameFor("order-allomorphs"));
}
