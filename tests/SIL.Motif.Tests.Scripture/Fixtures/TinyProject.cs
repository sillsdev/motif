using System.Text;

namespace SIL.Motif.Tests.Scripture.Fixtures;

/// <summary>
/// Writes a small Paratext project of invented verses into a temporary folder: GEN with three chapters,
/// MAT with three, 1CO with sixteen, and the apocryphal book TOB with one. The text is made up and
/// carries no real scripture or project data.
/// </summary>
internal sealed class TinyProject : IDisposable
{
    public const string Name = "TINY";
    public const string Guid = "00000000000000000000000000000000000000ab";

    private TinyProject(string folder) => Folder = folder;

    public string Folder { get; }

    public static TinyProject Create(int versification = 4, bool addNonCanonBook = false)
    {
        var folder = Path.Combine(Path.GetTempPath(), "motif-tiny-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Settings.xml"), Settings(versification), new UTF8Encoding(false));
        WriteBook(folder, "01GEN", "GEN", 3);
        WriteBook(folder, "41MAT", "MAT", 3);
        WriteBook(folder, "47" + "1CO", "1CO", 16);
        WriteBook(folder, "67TOB", "TOB", 1);
        if (addNonCanonBook) WriteBook(folder, "99XXA", "XXA", 1);
        return new TinyProject(folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }

    private static string Settings(int versification) => $"""
        <ScriptureText>
          <StyleSheet>usfm.sty</StyleSheet>
          <Language>Invented</Language>
          <BiblicalTermsListSetting>Major::BiblicalTerms.xml</BiblicalTermsListSetting>
          <Encoding>65001</Encoding>
          <Name>{Name}</Name>
          <Guid>{Guid}</Guid>
          <FullName>Tiny invented test project</FullName>
          <FileNameBookNameForm>41MAT</FileNameBookNameForm>
          <FileNamePrePart />
          <FileNamePostPart>.SFM</FileNamePostPart>
          <Versification>{versification}</Versification>
        </ScriptureText>
        """;

    private static void WriteBook(string folder, string filePrefix, string id, int chapters)
    {
        var usfm = new StringBuilder();
        usfm.Append($"\\id {id} Invented\n\\h {id}\n");
        for (var chapter = 1; chapter <= chapters; chapter++)
        {
            usfm.Append($"\\c {chapter}\n\\p\n");
            for (var verse = 1; verse <= 2; verse++)
                usfm.Append($"\\v {verse} The lamp {id} {chapter} keeps watch over verse {verse}.\n");
        }

        File.WriteAllText(Path.Combine(folder, filePrefix + ".SFM"), usfm.ToString(), new UTF8Encoding(false));
    }
}
