using System.Globalization;
using SIL.Motif.Commands;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class ComposerSubjectsTests
{
    private static readonly string[] LanguageNames =
    [
        "Swahili", "Tamil", "Tagalog", "English", "Indonesian", "Maasai", "Spanish", "French", "German", "Arabic",
        "Hindi", "Turkish", "Russian", "Chinese", "Japanese", "Korean", "Quechua", "Navajo", "Finnish", "Hebrew",
        "Portuguese", "Italian", "Latin", "Greek", "Bantu", "Kiswahili", "Hausa", "Yoruba", "Zulu", "Malay",
    ];

    private static HelpCatalog Catalog() => HelpCatalog.Load(CultureInfo.GetCultureInfo("en"));

    [Fact]
    public void EveryRegistryVerbServesAtLeastOneListedSubject()
    {
        foreach (var (verb, definition) in ProposalCommands.DraftComposers)
        {
            Assert.NotEmpty(definition.Subjects);
            Assert.All(definition.Subjects, subject => Assert.Contains(subject, ComposerSubjects.All));
            Assert.Equal(definition.Subjects.Distinct(), definition.Subjects);
            Assert.False(string.IsNullOrEmpty(verb));
        }
    }

    [Fact]
    public void EveryListedSubjectHasAComposerAndAHelpTitle()
    {
        var served = ProposalCommands.DraftComposers.Values.SelectMany(definition => definition.Subjects).ToHashSet();
        var help = Catalog().Subjects.ToDictionary(subject => subject.Id);
        foreach (var subject in ComposerSubjects.All)
        {
            Assert.Contains(subject, served);
            Assert.True(help.TryGetValue(subject, out var entry), subject);
            Assert.False(string.IsNullOrWhiteSpace(entry!.Title), subject);
            Assert.False(string.IsNullOrWhiteSpace(entry.Description), subject);
        }

        Assert.Equal(ComposerSubjects.All.Order(StringComparer.Ordinal), help.Keys.Order(StringComparer.Ordinal));
        Assert.All(ComposerSubjects.All, subject =>
            Assert.Matches("^[a-z]+(-[a-z]+)*$", subject));
    }

    [Fact]
    public void EveryRegistryVerbHasTwoToFourGoalsThatNameNoLanguage()
    {
        var catalog = Catalog();
        foreach (var verb in ProposalCommands.DraftComposers.Keys)
        {
            var entry = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Command, verb));
            var goals = Assert.IsAssignableFrom<IReadOnlyList<string>>(entry.Goals);
            Assert.InRange(goals.Count, 2, 4);
            foreach (var goal in goals)
            {
                Assert.False(string.IsNullOrWhiteSpace(goal), verb);
                foreach (var language in LanguageNames)
                    Assert.DoesNotContain(language, goal, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
