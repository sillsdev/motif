using SIL.Motif.Scripture;
using Xunit;

namespace SIL.Motif.Tests.Scripture;

/// <summary>
/// Opens real Paratext projects kept outside the repository, named by environment variables. Without the
/// variable a check skips, so no build or CI run needs the data.
/// </summary>
[Trait("MotifTestLevel", "Integration")]
public sealed class LocalSourceTests
{
    public const string WebVariable = "MOTIF_SCRIPTURE_WEB_DIR";
    public const string CebocbVariable = "MOTIF_SCRIPTURE_CEBOCB_DIR";

    [LocalSourceFact(WebVariable)]
    public void WebOpensWithItsCanonBooksAndANewTestamentContextWorks()
    {
        var source = ParatextSourceReader.Open(Environment.GetEnvironmentVariable(WebVariable)!);
        var context = ScriptureContext.Parse("NT", source.Versification);

        Assert.Equal(80, source.ChaptersByBook.Count);
        Assert.Equal(28, source.ChaptersByBook["MAT"].Count);
        Assert.True(context.Contains("MAT", 28));
        Assert.DoesNotContain(SourceScope.Later(source, context), chapter => chapter.Book == "MAT");
        Assert.Contains(new ChapterRef("GEN", 1), SourceScope.Later(source, context));
    }

    [LocalSourceFact(CebocbVariable)]
    public void CebocbOpensAndFirstCorinthiansHasSixteenChapters()
    {
        var source = ParatextSourceReader.Open(Environment.GetEnvironmentVariable(CebocbVariable)!);

        Assert.Equal(Enumerable.Range(1, 16), source.ChaptersByBook["1CO"]);
    }
}

/// <summary>A fact that skips unless the named environment variable names an existing folder.</summary>
public sealed class LocalSourceFactAttribute : FactAttribute
{
    public LocalSourceFactAttribute(string variable)
    {
        var folder = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            Skip = $"{variable} is not set to a Paratext project folder.";
    }
}
