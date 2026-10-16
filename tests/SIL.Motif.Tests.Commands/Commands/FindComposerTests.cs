using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class FindComposerTests
{
    // One goal per Subject, in a linguist's words, with the composer that must rank in the top three.
    public static TheoryData<string, string, string> GoalsByExpectedVerb => new()
    {
        { "sound-inventory", "I need to list the consonants and vowels that this language contrasts", "compose-author-phoneme" },
        { "natural-classes", "Group the sounds that pattern together, such as all the nasals", "compose-author-natural-class" },
        { "phonological-rules", "A final vowel is deleted, so I need a sound change rule for it", "compose-author-phonological-rule" },
        { "conditioned-allomorphy", "The suffix surfaces in a different shape after a nasal", "compose-author-alternate-form" },
        { "affix-slots-and-templates", "Define the position where the tense prefix goes on verbs", "compose-author-affix-slot" },
        { "inflection-features", "Set up a tense feature with past and future values", "compose-author-inflection-feature" },
        { "noun-class-and-gender", "Nouns fall into gender classes that agreement depends on", "compose-author-inflection-class" },
        { "inflection-classes", "Assign this stem to its declension class", "compose-edit-stem-inflection-class" },
        { "stem-alternation", "A verb root has a special form used only in the plural", "compose-author-stem-name" },
        { "lexical-entries-and-glosses", "Enter a new root in the lexicon and gloss it", "compose-author-lexical-entry" },
        { "senses-and-definitions", "Write a definition for another sense of the word", "compose-author-lexical-entry" },
        { "texts-and-analyses", "Paste in a story so that I can check how its words are analysed", "compose-author-text" },
        { "parsimony-dispositions", "Mark this Parsimony finding as intentional", "compose-record-parsimony-disposition" },
    };

    private static FindComposerResponse Run(string? goal, string? subject, string detail = "concise")
    {
        var outcome = FindComposerCommand.Find(new FindComposerRequest(goal, subject, detail));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    [Theory]
    [MemberData(nameof(GoalsByExpectedVerb))]
    public void FindComposerFindsTheExpectedComposerForEachSubjectGoal(string subject, string goal, string expected)
    {
        var results = Run(goal, null).Results;

        Assert.InRange(results.Count, 1, 5);
        var rank = results.Select(result => result.Verb).ToList().IndexOf(expected);
        Assert.True(rank is >= 0 and < 3,
            $"{subject}: '{expected}' ranked {rank} in [{string.Join(", ", results.Select(r => r.Verb))}]");
    }

    [Fact]
    public void TheGoalTableCoversEverySubject()
    {
        Assert.Equal(ComposerSubjects.All.Order(StringComparer.Ordinal),
            GoalsByExpectedVerb.Select(row => (string)row[0]).Distinct().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    [InlineData("plural suffix", "sound-inventory")]
    public void AGoalAndASubjectTogetherOrNeitherAreRefused(string? goal, string? subject)
    {
        var outcome = FindComposerCommand.Find(new FindComposerRequest(goal, subject));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.ComposerGoalOrSubject, outcome.Refusal!.Code);
        Assert.True(RefusalCatalog.Specs.ContainsKey(outcome.Refusal.Code));
    }

    [Fact]
    public void AnUnknownSubjectAndAnUnknownDetailAreRefused()
    {
        Assert.Equal(RefusalCodes.ComposerUnknownSubject,
            FindComposerCommand.Find(new FindComposerRequest(null, "no-such-subject")).Refusal!.Code);
        Assert.Equal(RefusalCodes.ComposerInvalidDetail,
            FindComposerCommand.Find(new FindComposerRequest("plural", null, "verbose")).Refusal!.Code);
    }

    [Fact]
    public void ASubjectSearchListsEveryComposerThatServesItInVerbOrder()
    {
        foreach (var subject in ComposerSubjects.All)
        {
            var expected = ProposalCommands.DraftComposers.Where(pair => pair.Value.Subjects.Contains(subject))
                .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();

            var results = Run(null, subject).Results;

            Assert.Equal(expected, results.Select(result => result.Verb).ToArray());
            Assert.All(results, result => Assert.Contains(subject, result.Subjects));
        }
    }

    [Fact]
    public void ATypedComposerCarriesItsOwnSchemaAndALegacyOneSaysHelpDescribesIt()
    {
        var typed = Assert.Single(Run(null, "stem-alternation").Results, r => r.Verb == "compose-edit-stem-allomorph-stem-name");
        Assert.NotNull(typed.IntentSchema);
        Assert.Equal("shape", typed.SchemaSource);

        var legacy = Assert.Single(Run(null, "stem-alternation").Results, r => r.Verb == "compose-author-stem-name");
        Assert.Null(legacy.IntentSchema);
        Assert.Equal("help", legacy.SchemaSource);
    }

    [Fact]
    public void ConciseOmitsTheExampleAndSubjectPageThatDetailedAdds()
    {
        var concise = Run("add a phoneme", null).Results[0];
        Assert.Null(concise.Example);
        Assert.Null(concise.SubjectPage);

        var detailed = Run(null, "sound-inventory", "detailed").Results;
        Assert.All(detailed, result =>
        {
            Assert.NotNull(result.SubjectPage);
            Assert.Equal("sound-inventory", result.SubjectPage!.Id);
            Assert.False(string.IsNullOrWhiteSpace(result.SubjectPage.Excerpt));
        });
    }
}
