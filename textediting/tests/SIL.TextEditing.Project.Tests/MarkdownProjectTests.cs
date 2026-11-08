using SIL.LCModel.Core.KernelInterfaces;
using SIL.TextEditing.Markdown;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class MarkdownProjectTests
    {
        [Fact]
        public void Markdown_reads_project_defaults_styles_and_inventory_and_writes_actual_font_details()
        {
            using (var project = new ProjectFixture())
            {
                project.Style("Normal", StyleType.kstParagraph, null, b => { });
                project.Style("Emphasis", StyleType.kstCharacter, null, b => b.SetIntPropValues(2, 3, 1));
                var systems = new ProjectWritingSystems(project.Cache);
                var options = new MarkdownOptions { WritingSystems = systems, Styles = new ProjectStyles(project.Cache) };
                var parsed = MarkdownConverter.Parse("analysis [vernacular]{.v custom-style=Emphasis}", options);
                Assert.Empty(parsed.Diagnostics);
                Assert.Contains(parsed.Document.Paragraphs[0].Runs, r => r.Ws == "en");
                Assert.Contains(parsed.Document.Paragraphs[0].Runs, r => r.Ws == "ar");
                string markdown = MarkdownConverter.ToMarkdown(parsed.Document, options);
                Assert.Contains("rtl", markdown);
                Assert.Contains(systems.DescribeWritingSystem("ar")!.Font, markdown);
                var unknown = MarkdownConverter.Parse("[text]{ws=qaa-x-unknown}", options);
                var finding = Assert.Single(unknown.Diagnostics, d => d.Id == "fwmd.ws.unknown");
                Assert.Contains("en", finding.Choices);
                Assert.Contains("ar", finding.Choices);
            }
        }
    }
}
