using SIL.Motif.Scripture;
using SIL.Motif.Tests.Scripture.Fixtures;
using Xunit;

namespace SIL.Motif.Tests.Scripture;

public sealed class SourceScopeTests : IDisposable
{
    private readonly TinyProject _project = TinyProject.Create();
    private readonly ParatextSource _source;

    public SourceScopeTests() => _source = ParatextSourceReader.Open(_project.Folder);

    public void Dispose() => _project.Dispose();

    private ScriptureContext Context(string text) => ScriptureContext.Parse(text, _source.Versification);

    [Fact]
    public void LaterIsEveryChapterOfTheSourceNotInTheContext()
    {
        var context = Context("GEN; 1CO 13-15");

        var later = SourceScope.Later(_source, context);
        var inContext = SourceScope.InContext(_source, context);

        Assert.Equal(_source.Chapters.Count(), later.Count + inContext.Count);
        Assert.Empty(later.Intersect(inContext));
        Assert.Equal(3 + 13 + 1, later.Count);
        Assert.DoesNotContain(new ChapterRef("1CO", 13), later);
        Assert.Contains(new ChapterRef("1CO", 12), later);
        Assert.Contains(new ChapterRef("TOB", 1), later);
        Assert.Equal(new ChapterRef("MAT", 1), later[0]);
    }

    [Fact]
    public void WholeBookContextLeavesOtherBooksLater()
    {
        var later = SourceScope.Later(_source, Context("MAT"));

        Assert.DoesNotContain(later, chapter => chapter.Book == "MAT");
        Assert.Equal(3 + 16 + 1, later.Count);
    }

    [Fact]
    public void ContextBooksTheSourceLacksAddNothing()
    {
        var inContext = SourceScope.InContext(_source, Context("GEN-REV"));

        Assert.Equal(3 + 3 + 16, inContext.Count);
        Assert.Equal([new ChapterRef("TOB", 1)], SourceScope.Later(_source, Context("GEN-REV")));
    }

    [Fact]
    public void ApocryphalBookOutsideTheContextIsLater()
    {
        var scope = SourceScope.Classify(_source, Context("GEN-REV"), null, new ChapterRef("TOB", 1));

        Assert.Equal(ChapterScope.Later, scope);
    }

    [Fact]
    public void ClassifyDistinguishesInContextCurrentAndLater()
    {
        var context = Context("1CO 13-15");
        var current = new ChapterRef("MAT", 2);

        Assert.Equal(ChapterScope.InContext, SourceScope.Classify(_source, context, current, new ChapterRef("1CO", 14)));
        Assert.Equal(ChapterScope.Current, SourceScope.Classify(_source, context, current, current));
        Assert.Equal(ChapterScope.Later, SourceScope.Classify(_source, context, current, new ChapterRef("MAT", 3)));
    }

    [Fact]
    public void ACurrentChapterInsideTheContextStaysInContext()
    {
        var current = new ChapterRef("1CO", 14);

        var scope = SourceScope.Classify(_source, Context("1CO 13-15"), current, current);

        Assert.Equal(ChapterScope.InContext, scope);
    }

    [Fact]
    public void ClassifyRefusesAChapterTheSourceDoesNotHold()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SourceScope.Classify(_source, Context("GEN"), null, new ChapterRef("GEN", 4)));

        Assert.Contains("GEN 4", ex.Message);
    }
}
