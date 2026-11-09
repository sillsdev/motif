using System;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.TextEditing.Markdown;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class ProjectPathResolverTests
    {
        static ILexEntry AddEntry(ProjectFixture project, string form, string gloss)
        {
            ILexEntry? entry = null;
            project.Change(() =>
            {
                entry = project.Cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                var allomorph = project.Cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = allomorph;
                allomorph.Form.set_String(project.Cache.DefaultVernWs, form);
                var sense = project.Cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(project.Cache.DefaultAnalWs, gloss);
            });
            return entry!;
        }

        static ICmPossibility AddItem(ProjectFixture project, string listName, string itemName, string description)
        {
            ICmPossibility? item = null;
            project.Change(() =>
            {
                var list = project.Cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
                project.Cache.LangProject.CheckListsOC.Add(list);
                list.Name.set_String(project.Cache.DefaultAnalWs, listName);
                item = project.Cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                list.PossibilitiesOS.Add(item);
                item.Name.set_String(project.Cache.DefaultAnalWs, itemName);
                item.Description.set_String(project.Cache.DefaultAnalWs, TsStringUtils.MakeString(description, project.Cache.DefaultAnalWs));
            });
            return item!;
        }

        [Fact]
        public void An_entry_and_a_list_item_resolve_by_their_paths_and_come_back_to_them()
        {
            using (var project = new ProjectFixture())
            {
                var entry = AddEntry(project, "entry-1", "gloss-1");
                var item = AddItem(project, "Notes", "N-1", "first note");
                var resolver = new ProjectPathResolver(project.Cache);

                Assert.Equal(entry.Guid, resolver.Resolve(ProjectPath.Parse("/lexicon/entry-1")!, out _));
                Assert.Equal(item.Guid, resolver.Resolve(ProjectPath.Parse("/lists/Notes/N-1")!, out _));
                Assert.Equal("/lexicon/entry-1", resolver.PathOf(entry.Guid)!.ToString());
                Assert.Equal("/lists/Notes/N-1", resolver.PathOf(item.Guid)!.ToString());
                Assert.Equal("lexiconEdit", resolver.ToolOf(entry.Guid));
            }
        }

        [Fact]
        public void A_path_that_names_nothing_resolves_to_no_object_and_no_candidates()
        {
            using (var project = new ProjectFixture())
            {
                AddEntry(project, "entry-1", "gloss-1");
                var resolver = new ProjectPathResolver(project.Cache);

                Assert.Null(resolver.Resolve(ProjectPath.Parse("/lexicon/absent")!, out var candidates));
                Assert.Empty(candidates);
                Assert.Null(resolver.Resolve(ProjectPath.ForObject(Guid.NewGuid()), out _));
                Assert.Null(resolver.PathOf(Guid.NewGuid()));
                Assert.Null(resolver.Describe(Guid.NewGuid()));
            }
        }

        [Fact]
        public void Two_entries_with_one_name_are_candidates_and_neither_is_chosen()
        {
            using (var project = new ProjectFixture())
            {
                var first = AddEntry(project, "entry-1", "gloss-1");
                var second = AddEntry(project, "entry-1", "gloss-2");
                var resolver = new ProjectPathResolver(project.Cache);

                Assert.Null(resolver.Resolve(ProjectPath.Parse("/lexicon/entry-1")!, out var candidates));
                Assert.Equal(new[] { first.Guid, second.Guid }.OrderBy(guid => guid), candidates.OrderBy(guid => guid));
            }
        }

        [Fact]
        public void An_object_with_no_path_of_its_own_is_still_found_by_its_guid()
        {
            using (var project = new ProjectFixture())
            {
                var other = project.Cache.LangProject;
                var resolver = new ProjectPathResolver(project.Cache);

                Assert.Equal(ProjectPath.ForObject(other.Guid).ToString(), resolver.PathOf(other.Guid)!.ToString());
                Assert.Equal(other.Guid, resolver.Resolve(ProjectPath.ForObject(other.Guid), out _));
            }
        }

        [Fact]
        public void Describe_says_what_an_entry_and_a_list_item_are()
        {
            using (var project = new ProjectFixture())
            {
                var entry = AddEntry(project, "entry-1", "gloss-1");
                var item = AddItem(project, "Notes", "N-1", "first note");
                var resolver = new ProjectPathResolver(project.Cache);

                var described = resolver.Describe(entry.Guid)!;
                Assert.Equal(("Lexical entry", "entry-1", "gloss-1"), (described.Kind, described.Name, described.Description));
                var listed = resolver.Describe(item.Guid)!;
                Assert.Equal(("List item", "N-1", "first note"), (listed.Kind, listed.Name, listed.Description));
            }
        }

        [Theory]
        [InlineData("A", "[entry-1](/lexicon/entry-1){.name guid=@}")]
        [InlineData("B", "[entry-1](../../lexicon/entry-1.fw.md){.name guid=@}")]
        [InlineData("C", "[entry-1](silfw://localhost/link?database%3dthis%24%26tool%3dlexiconEdit%26guid%3d@%26tag%3d){.name}")]
        [InlineData("D", "[entry-1](motif://projects/Sena3/lexicon/entry-1?guid=@){.name}")]
        [InlineData("E", "[entry-1](/objects/@){.name}")]
        [InlineData("path only", "[entry-1](/lexicon/entry-1)")]
        public void Every_spelling_of_a_link_resolves_to_the_entry(string form, string markdown)
        {
            using (var project = new ProjectFixture())
            {
                var entry = AddEntry(project, "entry-1", "gloss-1");
                var options = new MarkdownOptions
                {
                    Paths = new ProjectPathResolver(project.Cache),
                    LinkBase = "/lists/GDR/GDR-47.fw.md",
                    ProjectName = "Sena3",
                };

                var parsed = MarkdownConverter.ParseInline(markdown.Replace("@", entry.Guid.ToString("D")), options);

                Assert.True(parsed.Diagnostics.Count == 0, form + ": " + string.Join("; ", parsed.Diagnostics.Select(d => d.Id + " " + d.Message)));
                var link = Assert.Single(parsed.Objects);
                Assert.Equal(MarkdownObjectKind.InternalLink, link.Kind);
                Assert.Equal(entry.Guid, link.Guid);
                Assert.False(link.Broken);
            }
        }

        [Fact]
        public void A_link_to_a_deleted_target_is_reported_broken_and_keeps_its_text()
        {
            using (var project = new ProjectFixture())
            {
                var options = new MarkdownOptions { Paths = new ProjectPathResolver(project.Cache) };
                var missing = Guid.NewGuid();

                var parsed = MarkdownConverter.ParseInline("[entry-1](/lexicon/entry-1){.name guid=" + missing.ToString("D") + "}", options);

                Assert.Contains(parsed.Diagnostics, d => d.Id == MarkdownDiagnosticIds.LinkBroken);
                Assert.True(Assert.Single(parsed.Objects).Broken);
                Assert.Equal("entry-1", string.Concat(parsed.Runs.Select(run => run.Text)));
            }
        }
    }
}
