using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown;
using SIL.WritingSystems;
using Xunit;

namespace SIL.TextEditing.WritingSystems.Tests
{
    public class RegistryWritingSystemsTests
    {
        static readonly string[] Tags = { "en", "ceb", "ceb-x-old", "ar" };

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void KnownAndUnknownTagsAreCheckedThroughMarkdown(bool repositoryBacked)
        {
            var checker = repositoryBacked
                ? new RegistryWritingSystems(new Inventory(Tags), "en", "ceb")
                : new RegistryWritingSystems(Tags, "en", "ceb");
            var options = new MarkdownOptions { WritingSystems = checker };
            Assert.Empty(MarkdownConverter.Parse("[known]{ws=ceb}", options).Diagnostics);
            var finding = Assert.Single(MarkdownConverter.Parse("[unknown]{ws=ceb-x-oldd}", options).Diagnostics);
            Assert.Equal("fwmd.ws.unknown", finding.Id);
            Assert.Equal(MarkdownSeverity.Error, finding.Severity);
            Assert.Equal(Tags, finding.Choices);
            Assert.Equal(new[] { "ceb-x-old" }, finding.Nearest);
            Assert.Equal("ceb", MarkdownConverter.Parse("[default]{.v}", options).Document.Paragraphs[0].Runs[0].Ws);
        }

        [Fact]
        public void DirectChecksSupplyChoicesAndNearestWithoutAParser()
        {
            var checker = new RegistryWritingSystems(Tags, "en", "ceb");
            Assert.Null(checker.CheckWritingSystem("en"));
            Assert.True(checker.IsKnown("ar"));
            Assert.False(checker.IsKnown("fr"));
            var problem = Assert.IsType<MarkdownProblem>(checker.CheckWritingSystem("ceb-x-oldd"));
            Assert.Equal(MarkdownDiagnosticIds.WsUnknown, problem.Id);
            Assert.Equal(MarkdownSeverity.Error, problem.Severity);
            Assert.Equal(Tags, problem.Choices);
            Assert.Equal(new[] { "ceb-x-old" }, problem.Nearest);
            Assert.Empty(checker.CheckWritingSystem("zzzzzzzzzz")!.Nearest);
        }

        [Fact]
        public void SnapshotPreservesOrderAndExactTagsWithoutAddingDefaults()
        {
            var tags = new List<string> { "fr-CA", "en", "fr-CA", "custom-project-tag" };
            var checker = new RegistryWritingSystems(tags, "absent", "also-absent");
            tags.Clear();
            Assert.Equal(new[] { "fr-CA", "en", "custom-project-tag" }, checker.KnownTags);
            Assert.True(checker.IsKnown("custom-project-tag"));
            Assert.False(checker.IsKnown("fr-ca"));
            Assert.False(checker.IsKnown("absent"));
            Assert.Equal("absent", checker.DefaultAnalysis);
            Assert.Equal("also-absent", checker.DefaultVernacular);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)checker.KnownTags).Clear());
        }

        [Fact]
        public void RepositoryInventoryIsDetachedAndUsesIdsRatherThanLanguageTags()
        {
            var repository = new Inventory("project-id");
            var checker = new RegistryWritingSystems(repository, "en", "ceb");
            repository.Definitions.Clear();
            Assert.Equal(new[] { "project-id" }, checker.KnownTags);
            Assert.Null(checker.CheckWritingSystem("project-id"));
            Assert.NotNull(checker.CheckWritingSystem("qaa"));
        }

        [Fact]
        public void EmptyInventoryHasNoChoicesOrNearestEvenForAValidLanguageTag()
        {
            var checker = new RegistryWritingSystems(Array.Empty<string>(), "en", "en");
            var problem = checker.CheckWritingSystem("en")!;
            Assert.Equal(MarkdownSeverity.Error, problem.Severity);
            Assert.Empty(problem.Choices);
            Assert.Empty(problem.Nearest);
        }

        [Fact]
        public void NearestUsesTheMarkdownRankingIncludingSwapsStableTiesAndThreeResultLimit()
        {
            var tags = new[] { "abd", "acb", "abe", "abf", "zzzzzzzz" };
            var checker = new RegistryWritingSystems(tags, "abd", "abd");
            Assert.Equal(new[] { "abd", "acb", "abe" }, checker.CheckWritingSystem("abc")!.Nearest);
        }

        [Fact]
        public void NullInputsAndEmptyInventoryEntriesAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new RegistryWritingSystems((IWritingSystemRepository)null!, "en", "en"));
            Assert.Throws<ArgumentNullException>(() => new RegistryWritingSystems((IEnumerable<string>)null!, "en", "en"));
            Assert.Throws<ArgumentNullException>(() => new RegistryWritingSystems(Tags, null!, "en"));
            Assert.Throws<ArgumentNullException>(() => new RegistryWritingSystems(Tags, "en", null!));
            Assert.Throws<ArgumentException>(() => new RegistryWritingSystems(new[] { "" }, "en", "en"));
            Assert.Throws<ArgumentException>(() => new RegistryWritingSystems(new[] { (string)null! }, "en", "en"));
            Assert.Throws<ArgumentNullException>(() => new RegistryWritingSystems(Tags, "en", "ceb").CheckWritingSystem(null!));
        }

        sealed class Inventory : IWritingSystemRepository
        {
            public Inventory(params string[] tags) => Definitions = tags.Select(tag => new WritingSystemDefinition { Id = tag }).ToList();
            public List<WritingSystemDefinition> Definitions { get; }
            public IEnumerable<WritingSystemDefinition> AllWritingSystems => Definitions;
            public event EventHandler<WritingSystemIdChangedEventArgs> WritingSystemIdChanged { add { } remove { } }
            public event EventHandler<WritingSystemDeletedEventArgs> WritingSystemDeleted { add { } remove { } }
            public event EventHandler<WritingSystemConflatedEventArgs> WritingSystemConflated { add { } remove { } }
            public int Count => throw new NotSupportedException();
            public IWritingSystemFactory WritingSystemFactory => throw new NotSupportedException();
            public void Set(WritingSystemDefinition ws) => throw new NotSupportedException();
            public bool CanSet(WritingSystemDefinition ws) => throw new NotSupportedException();
            public WritingSystemDefinition Get(string id) => throw new NotSupportedException();
            public bool TryGet(string id, out WritingSystemDefinition ws) => throw new NotSupportedException();
            public string GetNewIdWhenSet(WritingSystemDefinition ws) => throw new NotSupportedException();
            public bool Contains(string id) => throw new NotSupportedException();
            public void Conflate(string wsToConflate, string wsToConflateWith) => throw new NotSupportedException();
            public void Remove(string id) => throw new NotSupportedException();
            public bool WritingSystemIdHasChanged(string id) => throw new NotSupportedException();
            public string WritingSystemIdHasChangedTo(string id) => throw new NotSupportedException();
            public bool CanSave(WritingSystemDefinition ws) => throw new NotSupportedException();
            public void Save() => throw new NotSupportedException();
        }
    }
}
