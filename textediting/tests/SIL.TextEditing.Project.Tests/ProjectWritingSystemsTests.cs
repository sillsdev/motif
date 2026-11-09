using System;
using System.Linq;
using SIL.LCModel.Core.Text;
using SIL.TextEditing.LCModel;
using SIL.TextEditing.Markdown;
using SIL.WritingSystems;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class ProjectWritingSystemsTests
    {
        [Fact]
        public void Project_defaults_inventory_and_RTL_font_details_are_exposed_through_the_host_interfaces()
        {
            using (var project = new ProjectFixture())
            {
                project.Change(() =>
                {
                    var arabic = project.Cache.ServiceLocator.WritingSystemManager.Get("ar");
                    arabic.DefaultFont = new FontDefinition("Scheherazade New");
                    project.Cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Add(arabic);
                    project.Cache.ServiceLocator.WritingSystemManager.GetOrSet("fr", out _);
                });
                var systems = new ProjectWritingSystems(project.Cache);
                IWritingSystemMapper mapper = systems;
                IWritingSystemDetails details = systems;
                IKnownWritingSystems known = systems;
                Assert.Equal("en", mapper.DefaultAnalysis);
                Assert.Equal("ar", mapper.DefaultVernacular);
                Assert.Contains("en", known.KnownTags);
                Assert.Contains("ar", known.KnownTags);
                Assert.Equal(known.KnownTags.Count, known.KnownTags.Distinct().Count());
                Assert.False(mapper.IsKnown("fr"));
                Assert.False(mapper.IsKnown("qaa-x-unknown"));
                Assert.Null(details.DescribeWritingSystem("fr"));
                Assert.Null(details.DescribeWritingSystem("qaa-x-unknown"));
                var ar = details.DescribeWritingSystem("ar")!;
                Assert.Equal("ar", ar.Language);
                Assert.Equal("rtl", ar.Direction);
                Assert.Equal("Scheherazade New", ar.Font);
                Assert.Equal("ltr", details.DescribeWritingSystem("en")!.Direction);
            }
        }

        [Fact]
        public void The_existing_ITsString_adapter_uses_the_projects_handles_without_another_mapping()
        {
            using (var project = new ProjectFixture())
            {
                IWritingSystemHandles systems = new ProjectWritingSystems(project.Cache);
                Assert.Equal(project.Cache.DefaultVernWs, systems.GetHandle("ar"));
                Assert.Equal("en", systems.GetId(project.Cache.DefaultAnalWs));
                Assert.Equal(0, systems.GetHandle("qaa-x-unknown"));
                Assert.Equal(string.Empty, systems.GetId(0));
                Assert.Equal(string.Empty, systems.GetId(int.MaxValue));
                var text = TsStringUtils.MakeString("Arabic", project.Cache.DefaultVernWs);
                var runs = TsStringConversion.ToRuns(text, systems);
                Assert.Equal("ar", Assert.Single(runs).Ws);
                Assert.True(text.Equals(TsStringConversion.ToTsString(runs, systems, "en")));
            }
        }

        [Fact]
        public void Defaults_membership_and_details_follow_project_edits_without_changing_earlier_results()
        {
            using (var project = new ProjectFixture())
            {
                var systems = new ProjectWritingSystems(project.Cache);
                var before = systems.KnownTags;
                var details = systems.DescribeWritingSystem("ar")!;
                project.Change(() =>
                {
                    project.Cache.ServiceLocator.WritingSystemManager.GetOrSet("fr", out var french);
                    project.Cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Add(french);
                    project.Cache.ServiceLocator.WritingSystems.DefaultAnalysisWritingSystem = french;
                    project.Cache.ServiceLocator.WritingSystemManager.Get("ar").RightToLeftScript = false;
                });
                Assert.Equal("fr", systems.DefaultAnalysis);
                Assert.True(systems.IsKnown("fr"));
                Assert.DoesNotContain("fr", before);
                Assert.Contains("fr", systems.KnownTags);
                Assert.Equal("rtl", details.Direction);
                Assert.Equal("ltr", systems.DescribeWritingSystem("ar")!.Direction);
            }
        }

        [Fact]
        public void A_null_cache_is_rejected() => Assert.Throws<ArgumentNullException>(() => new ProjectWritingSystems(null!));
    }
}
