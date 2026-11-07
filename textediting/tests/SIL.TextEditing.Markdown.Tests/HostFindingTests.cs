using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>What a host can say about the objects, writing systems and styles of a text, and where the findings land: the host's id, severity, choices, nearest names and fix reach
    /// the result with the line and column of the object. The library's own findings are conformance fixtures; these tests are about the host interfaces, which the fixtures only use
    /// through one host.</summary>
    public class HostFindingTests
    {
        const string EntryGuid = "11111111-2222-3333-4444-555555555555";

        /// <summary>A host that resolves every object by a script. The yes/no method must not be used when this interface is there.</summary>
        sealed class ScriptedHost : IObjectMapper, IObjectResolver
        {
            public delegate bool Resolve(MarkdownObject target, out string objData, out MarkdownProblem? problem);

            readonly Resolve _resolve;
            public readonly List<MarkdownObject> Seen = new List<MarkdownObject>();

            public ScriptedHost(Resolve resolve) { _resolve = resolve; }

            public MarkdownObject? Describe(string objData) => null;

            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
                => throw new InvalidOperationException("TryResolve is the way in when the host implements IObjectResolver.");

            public bool TryResolve(MarkdownObject target, out string objData, out MarkdownProblem? problem)
            {
                Seen.Add(target);
                return _resolve(target, out objData, out problem);
            }
        }

        static ScriptedHost Host(ScriptedHost.Resolve resolve) => new ScriptedHost(resolve);

        static MarkdownOptions With(IObjectMapper host) => new MarkdownOptions { Objects = host, WriteFrontMatter = false, StyleNames = Hosts.Tables() };

        static readonly string Link = "[palit](/objects/" + EntryGuid + ")";

        // ------------------------------------------------------------ problems with ids, severities, choices and nearest names

        [Fact]
        public void AnAmbiguousLinkIsReportedWithItsChoicesAtTheLink()
        {
            string[] choices = { "/lexicon/palit(1)", "/lexicon/palit(2)", "/lexicon/palit(3)" };
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkAmbiguous, MarkdownSeverity.Error, "palit matches 3 entries.") { Choices = choices };
                return false;
            });
            var result = MarkdownConverter.Parse("a\n\nsee " + Link + " here", With(host));
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("fwmd.link.ambiguous", d.Id);
            Assert.Equal(MarkdownSeverity.Error, d.Severity);
            Assert.Equal("palit matches 3 entries.", d.Message);
            Assert.Equal(choices, d.Choices);
            Assert.Null(d.Replacement);
            Assert.Equal((3, 5, 3, 5 + Link.Length), (d.Line, d.Column, d.EndLine, d.EndColumn));
            Assert.Equal(Link, d.Observed);
            Assert.True(result.HasErrors);
            Assert.Equal("see palit here", result.Document.Paragraphs[1].Text);
            Assert.All(result.Document.Paragraphs[1].Runs, r => Assert.Null(r.ObjData));
        }

        [Fact]
        public void ABrokenLinkIsAWarningAndTheLinkIsKept()
        {
            string data = TestObjects.LinkData("lexiconEdit", new Guid(EntryGuid));
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = data;
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "The link target was deleted; the text is kept.");
                return true;
            });
            var result = MarkdownConverter.Parse("a\n\n" + Link, With(host));
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("fwmd.link.broken", d.Id);
            Assert.Equal(MarkdownSeverity.Warning, d.Severity);
            Assert.Equal((3, 1), (d.Line, d.Column));
            Assert.Equal(Link, d.Observed);
            Assert.False(result.HasErrors);
            Assert.Equal(data, Assert.Single(result.Document.Paragraphs[1].Runs).ObjData);
        }

        [Fact]
        public void ALinkThatIsNotFoundCarriesTheNearestNamesAndTheDefaultId()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem("/lexicon/palt matches no entry. Nearest: palit, palito.") { Nearest = new[] { "palit", "palito" } };
                return false;
            });
            var d = Assert.Single(MarkdownConverter.Parse("see " + Link, With(host)).Diagnostics);
            Assert.Equal("fwmd.link.not-found", d.Id);
            Assert.Equal(MarkdownSeverity.Error, d.Severity);
            Assert.Equal(new[] { "palit", "palito" }, d.Nearest);
            Assert.Empty(d.Choices);
        }

        [Fact]
        public void APictureThatIsUnknownHasItsOwnDefaultId()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem("not a picture of this project");
                return false;
            });
            var d = Assert.Single(MarkdownConverter.Parse("![c](/objects/99999999-8888-7777-6666-555555555555)", With(host)).Diagnostics);
            Assert.Equal("fwmd.picture.unknown", d.Id);
        }

        [Fact]
        public void AHostThatCannotSayWhyStillGetsAFinding()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = null;
                return false;
            });
            var d = Assert.Single(MarkdownConverter.Parse(Link, With(host)).Diagnostics);
            Assert.Equal("fwmd.link.not-found", d.Id);
            Assert.Equal(MarkdownSeverity.Error, d.Severity);
        }

        [Fact]
        public void AFailureIsAnErrorWhateverSeverityTheHostGave()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem(null, MarkdownSeverity.Suggestion, "nope");
                return false;
            });
            var d = Assert.Single(MarkdownConverter.Parse(Link, With(host)).Diagnostics);
            Assert.Equal(MarkdownSeverity.Error, d.Severity);
        }

        [Fact]
        public void AReplacementFromTheHostIsTheFix()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem("moved") { Replacement = "[palit](/lexicon/palit(2))" };
                return false;
            });
            var d = Assert.Single(MarkdownConverter.Parse(Link, With(host)).Diagnostics);
            Assert.Equal("[palit](/lexicon/palit(2))", d.Replacement);
        }

        [Fact]
        public void HostProblemsAreFoundInTableCellsAndDivsAtTheirPlaces()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem("no");
                return false;
            });
            var result = MarkdownConverter.Parse("| h |\n|---|\n| x " + Link + " |\n\n::: {custom-style=S}\nb " + Link + "\n:::", With(host));
            Assert.Equal(new[] { 3, 6 }, result.Diagnostics.Select(d => d.Line).ToArray());
            Assert.Equal(new[] { 5, 3 }, result.Diagnostics.Select(d => d.Column).ToArray());
        }

        [Fact]
        public void TheHostIsToldWhereTheObjectIsWhileItResolvesIt()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = "data";
                problem = null;
                return true;
            });
            MarkdownConverter.Parse("a\n\nsee " + Link, With(host));
            var source = host.Seen.Single().Source!;
            Assert.Equal((3, 5, 3, 5 + Link.Length), (source.Line, source.Column, source.EndLine, source.EndColumn));
        }

        [Fact]
        public void TheResultListsEveryObjectWithItsRangeAndData()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) => { objData = "d"; problem = null; return true; });
            var result = MarkdownConverter.Parse(Link + " and " + Link + "\n\n[w](https://x.org)", With(host));
            Assert.Equal(3, result.Objects.Count);
            Assert.All(result.Objects, o => Assert.NotNull(o.Source));
            Assert.Equal(new[] { 1, 1, 3 }, result.Objects.Select(o => o.Source!.Line).ToArray());
            Assert.True(result.Objects[0].Source!.Start < result.Objects[1].Source!.Start);
        }

        [Fact]
        public void TheHostCanPlaceItsOwnFindingOnAnObjectOfTheResult()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) => { objData = "d"; problem = null; return true; });
            var result = MarkdownConverter.Parse("a\n\nsee " + Link, With(host));
            var o = result.Objects.Single();
            var d = result.CreateDiagnostic("host.rule", MarkdownSeverity.Warning, "the project's own rule", o.Source!.Start, o.Source.End);
            Assert.Equal(Link, d.Observed);
            Assert.Equal((3, 5), (d.Line, d.Column));
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void AnInlineResultHasObjectsAndCreatesFindingsToo()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) => { objData = "d"; problem = null; return true; });
            var result = MarkdownConverter.ParseInline("a " + Link, With(host));
            Assert.Single(result.Objects);
            Assert.Equal(Link, result.CreateDiagnostic("x.y", MarkdownSeverity.Suggestion, "m", 2, 2 + Link.Length).Observed);
        }

        // ------------------------------------------------------------ writing systems and styles

        sealed class ListedWritingSystems : IWritingSystemMapper, IKnownWritingSystems
        {
            public string DefaultAnalysis => "en";
            public string DefaultVernacular => "ceb";
            public bool IsKnown(string tag) => KnownTags.Contains(tag);
            public IReadOnlyList<string> KnownTags { get; } = new[] { "en", "ceb", "ceb-x-old", "ar" };
        }

        sealed class ListedStyles : IStyleCatalog, IKnownStyles
        {
            public IReadOnlyList<string> ParagraphStyleNames { get; } = new[] { "Normal", "Example", "Heading 1" };
            public IReadOnlyList<string> CharacterStyleNames { get; } = new[] { "Emphasized Text", "Strong" };
            public bool IsParagraphStyle(string name) => ParagraphStyleNames.Contains(name);
            public bool IsCharacterStyle(string name) => CharacterStyleNames.Contains(name);
        }

        static MarkdownOptions Body() => new MarkdownOptions { WriteFrontMatter = false };

        [Fact]
        public void AnUnknownWritingSystemOffersTheProjectsSystemsAndTheClosest()
        {
            var options = Body();
            options.WritingSystems = new ListedWritingSystems();
            var d = Assert.Single(MarkdownConverter.Parse("[x]{ws=ceb-x-oldd}", options).Diagnostics);
            Assert.Equal("fwmd.ws.unknown", d.Id);
            Assert.Equal(new[] { "en", "ceb", "ceb-x-old", "ar" }, d.Choices);
            Assert.Equal(new[] { "ceb-x-old" }, d.Nearest);
        }

        [Fact]
        public void AnUnknownWritingSystemOfAnEmptyMarkerOffersThemToo()
        {
            var options = Body();
            options.WritingSystems = new ListedWritingSystems();
            var d = Assert.Single(MarkdownConverter.Parse("[]{.empty ws=zzz}", options).Diagnostics);
            Assert.Equal(new[] { "en", "ceb", "ceb-x-old", "ar" }, d.Choices);
        }

        [Fact]
        public void AnUnknownWritingSystemWithoutAListHasNoChoices()
        {
            var options = Body();
            options.WritingSystems = new TestWritingSystems();
            var d = Assert.Single(MarkdownConverter.Parse("[x]{ws=zzz-unknown}", options).Diagnostics);
            Assert.Empty(d.Choices);
            Assert.Empty(d.Nearest);
        }

        [Fact]
        public void AnUnknownStyleNamesTheNearest()
        {
            var options = Body();
            options.Styles = new ListedStyles();
            var result = MarkdownConverter.Parse("::: {custom-style=Exampel}\nx\n:::\n\n[y]{custom-style=\"Emphasised Text\"}", options);
            Assert.Equal(new[] { "Example" }, result.Diagnostics[0].Nearest);
            Assert.Equal(new[] { "Emphasized Text" }, result.Diagnostics[1].Nearest);
        }

        [Fact]
        public void AnUnknownStyleFarFromEveryNameHasNoNearest()
        {
            var options = Body();
            options.Styles = new ListedStyles();
            var d = Assert.Single(MarkdownConverter.Parse("[y]{custom-style=zzzzzzzzzz}", options).Diagnostics);
            Assert.Empty(d.Nearest);
        }

        sealed class CheckedWritingSystems : IWritingSystemMapper, IWritingSystemChecker
        {
            public readonly List<string> Asked = new List<string>();
            public string DefaultAnalysis => "en";
            public string DefaultVernacular => "ceb";
            public bool IsKnown(string tag) => throw new InvalidOperationException("CheckWritingSystem is the way in when the host implements IWritingSystemChecker.");

            public MarkdownProblem? CheckWritingSystem(string tag)
            {
                Asked.Add(tag);
                switch (tag)
                {
                    case "ok": return null;
                    case "old": return new MarkdownProblem("host.ws.legacy", MarkdownSeverity.Warning, "'old' is legacy.") { Replacement = "ws=new" };
                    case "who": return new MarkdownProblem(null, MarkdownSeverity.Error, "unknown") { Choices = new[] { "a", "b" }, Nearest = new[] { "a" } };
                    default: return new MarkdownProblem("no such system");
                }
            }
        }

        sealed class CheckedStyles : IStyleCatalog, IStyleChecker
        {
            public bool IsParagraphStyle(string name) => throw new InvalidOperationException("CheckStyle is the way in.");
            public bool IsCharacterStyle(string name) => throw new InvalidOperationException("CheckStyle is the way in.");

            public MarkdownProblem? CheckStyle(string name, bool paragraph)
                => name == "fine" ? null : name == "meh" ? new MarkdownProblem(null, MarkdownSeverity.Suggestion, "meh is deprecated") { Nearest = new[] { "fine" } } : new MarkdownProblem("no style " + name);
        }

        [Fact]
        public void AHostsProblemForAWritingSystemIsReportedWithItsChoicesAndNearest()
        {
            var options = Body();
            options.WritingSystems = new CheckedWritingSystems();
            var d = Assert.Single(MarkdownConverter.Parse("[x]{ws=who}", options).Diagnostics);
            Assert.Equal("fwmd.ws.unknown", d.Id);
            Assert.Equal(new[] { "a", "b" }, d.Choices);
            Assert.Equal(new[] { "a" }, d.Nearest);
        }

        [Fact]
        public void AKnownButWarnedWritingSystemStillReadsWithNoError()
        {
            var options = Body();
            options.WritingSystems = new CheckedWritingSystems();
            var result = MarkdownConverter.Parse("[x]{ws=old}", options);
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("host.ws.legacy", d.Id);
            Assert.Equal("ws=new", d.Replacement);
            Assert.Equal("old", result.Document.Paragraphs[0].Runs[0].Ws);
        }

        [Fact]
        public void AWritingSystemTheHostLikesIsNotReportedAndIsAskedAboutOnceEachTimeItAppears()
        {
            var checker = new CheckedWritingSystems();
            var options = Body();
            options.WritingSystems = checker;
            var result = MarkdownConverter.Parse("[x]{ws=ok} [y]{ws=ok}", options);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(new[] { "ok", "ok" }, checker.Asked);
        }

        [Fact]
        public void AHostsProblemForTheWritingSystemOfAnEmptyMarkerIsReportedToo()
        {
            var options = Body();
            options.WritingSystems = new CheckedWritingSystems();
            var d = Assert.Single(MarkdownConverter.Parse("[]{.empty ws=old}", options).Diagnostics);
            Assert.Equal("host.ws.legacy", d.Id);
        }

        [Fact]
        public void AHostsProblemForAStyleCarriesItsNearestNamesForParagraphsAndCharacters()
        {
            var options = Body();
            options.Styles = new CheckedStyles();
            var result = MarkdownConverter.Parse("::: {custom-style=meh}\nx\n:::\n\n[y]{custom-style=meh}", options);
            Assert.Equal(2, result.Diagnostics.Count);
            Assert.All(result.Diagnostics, d => Assert.Equal(new[] { "fine" }, d.Nearest));
        }

        [Fact]
        public void AStyleThatIsAcceptedWithASuggestionStillReadsWithNoError()
        {
            var options = Body();
            options.Styles = new CheckedStyles();
            var result = MarkdownConverter.Parse("[y]{custom-style=meh}", options);
            Assert.False(result.HasErrors);
            Assert.Equal("meh", result.Document.Paragraphs[0].Runs[0].Props.StrProps[TextPropertyNumbers.NamedStyle]);
            Assert.Equal(MarkdownSeverity.Suggestion, Assert.Single(result.Diagnostics).Severity);
        }

        sealed class ListedAndChecked : IWritingSystemMapper, IWritingSystemChecker, IKnownWritingSystems
        {
            public string DefaultAnalysis => "en";
            public string DefaultVernacular => "ceb";
            public bool IsKnown(string tag) => false;
            public IReadOnlyList<string> KnownTags { get; } = new[] { "en", "ceb", "ar" };
            public MarkdownProblem? CheckWritingSystem(string tag) => new MarkdownProblem("not here");
        }

        [Fact]
        public void TheListsFillWhatTheHostsCheckerLeavesEmpty()
        {
            var options = Body();
            options.WritingSystems = new ListedAndChecked();
            var d = Assert.Single(MarkdownConverter.Parse("[x]{ws=cebb}", options).Diagnostics);
            Assert.Equal(new[] { "en", "ceb", "ar" }, d.Choices);
            Assert.Equal(new[] { "ceb" }, d.Nearest);
        }

        [Fact]
        public void ADiagnosticsPropertiesAreSettableForTheHost()
        {
            var d = new MarkdownDiagnostic("x.y", MarkdownSeverity.Warning, "m", 3, 1)
            {
                Observed = "o", Replacement = "r", Choices = new[] { "c" }, Nearest = new[] { "n" },
            };
            Assert.Equal((3, 3), (d.Start, d.End)); // an end before the start is the start
            Assert.Equal(new[] { "c" }, d.Choices);
            Assert.Equal(new[] { "n" }, d.Nearest);
        }
    }
}
