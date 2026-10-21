using SIL.Motif.Commands;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class InflectionClassEditorDispatchTests
{
    [Theory]
    [InlineData("compose-edit-part-of-speech-inflection-class-default")]
    [InlineData("compose-edit-affix-inflection-classes")]
    [InlineData("compose-edit-stem-allomorph-stem-name")]
    [InlineData("compose-edit-stem-inflection-class")]
    [InlineData("compose-edit-compound-rule")]
    [InlineData("compose-set-no-default-compounding")]
    [InlineData("compose-rewrite-lexeme-form")]
    public void DraftComposerRegistryContainsTheInflectionClassEditors(string verb)
    {
        Assert.Contains(verb, ProposalCommands.DraftComposers.Keys);
    }
}
