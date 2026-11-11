using SIL.Motif.Host.RichText;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Host;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class MotifMarkdownOptionsTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AProjectsOptionsWriteNoFrontMatterMarkAnUnknownObjectBrokenAndKnowTheProject()
    {
        using var cache = pristine.NewScratch();

        var options = MotifMarkdownOptions.For(cache, "project-1");

        Assert.False(options.WriteFrontMatter);
        Assert.Equal(UnknownGuidPolicy.MarkBroken, options.UnknownGuids);
        Assert.IsType<ProjectWritingSystems>(options.WritingSystems);
        Assert.IsType<ProjectStyles>(options.Styles);
        Assert.IsType<ProjectPathResolver>(options.Paths);
        Assert.Equal("project-1", options.ProjectName);

        var read = new FieldWorksMarkdownSourceForm(options).Read($"[gone](/objects/{Guid.NewGuid():D})", singleParagraph: true);
        Assert.DoesNotContain(read.Diagnostics, finding => finding.Severity == SIL.TextEditing.DiagnosticSeverity.Error);
    }
}
