using System.Web;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// Pins how a grammar finding the parser printed becomes a row a person can read: context split from
/// problem, identifiers replaced by names, and each name linked into FieldWorks at the right tool.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class GrammarWarningReaderTests : IDisposable
{
    private const string EntryId = "c1e60f3f-3606-4ac9-b05b-33a79d0d1478";
    private const string SenseId = "14ff3655-598a-4ce5-8186-805c65398553";
    private const string MsaId = "0c686afa-8d21-4e3b-bc0e-41812150cf4c";

    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public GrammarWarningReaderTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AFindingSplitsIntoWhereItAppliesAndWhatIsWrong()
    {
        var warning = GrammarWarningReader.Read(
            $"warning: lex entry \"{EntryId}\" sense \"{SenseId}\": msa \"{MsaId}\" does not resolve within this entry",
            id => id == Guid.Parse(MsaId) ? null : new GrammarWarningReader.ResolvedObject(
                id == Guid.Parse(EntryId) ? "kuona" : "to see", id == Guid.Parse(EntryId) ? "Entry" : "Sense", null));

        Assert.Equal("warning", warning.Severity);
        Assert.Equal("Entry", warning.Kind);
        Assert.Equal(
            new[] { ("lex entry", "text"), ("kuona", "object"), ("sense", "text"), ("to see", "object") },
            warning.Subject.Select(part => (part.Text, part.Role)));
        Assert.Equal(
            new[] { ("msa", "text"), (MsaId, "missing"), ("does not resolve within this entry", "text") },
            warning.Problem.Select(part => (part.Text, part.Role)));
        Assert.StartsWith("warning: lex entry", warning.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALineWithNoContextIsAllProblem_AndAQuotedNonIdentifierIsAValue()
    {
        var warning = GrammarWarningReader.Read(
            "capability: ParserParameters XAmple MaxRoots has invalid value \"a \\\"big\\\" one\"", _ => null);

        Assert.Equal("capability", warning.Severity);
        Assert.Empty(warning.Subject);
        Assert.Equal(string.Empty, warning.Kind);
        Assert.Equal(("a \"big\" one", "value"), (warning.Problem[^1].Text, warning.Problem[^1].Role));
    }

    [Fact]
    public void AColonInsideAQuotedValueDoesNotSplitTheLine()
    {
        var warning = GrammarWarningReader.Read("warning: boundary marker \"a: b\" does not resolve", _ => null);

        Assert.Empty(warning.Subject);
        Assert.Contains(warning.Problem, part => part is { Text: "a: b", Role: "value" });
    }

    [Fact]
    public void IdentifiersResolveToNamesAndLinkIntoFieldWorksAtTheOwningEntry()
    {
        var line = $"warning: lex entry \"{_seed.FirstEntryId:D}\" sense \"{_seed.FirstSenseId:D}\": " +
                   $"part of speech \"{_seed.PartOfSpeechId:D}\" does not resolve";

        var warning = Assert.Single(GrammarWarningReader.Read(_cache, "Sena 3", [line]));

        var entry = warning.Subject[1];
        Assert.Equal((SeededProject.FirstForm, "Entry"), (entry.Text, entry.Kind));
        Assert.Equal(
            $"database=Sena 3&tool=lexiconEdit&guid={_seed.FirstEntryId:D}&tag=", QueryOf(entry));

        var sense = warning.Subject[3];
        Assert.Equal((SeededProject.FirstGloss, "Sense"), (sense.Text, sense.Kind));
        Assert.Equal(
            $"database=Sena 3&tool=lexiconEdit&guid={_seed.FirstEntryId:D}&tag=", QueryOf(sense));

        var category = warning.Problem[1];
        Assert.Equal("Category", category.Kind);
        Assert.Equal(
            $"database=Sena 3&tool=posEdit&guid={_seed.PartOfSpeechId:D}&tag=", QueryOf(category));
    }

    [Fact]
    public void AnIdentifierTheProjectDoesNotContainIsMarkedMissing()
    {
        var warning = Assert.Single(GrammarWarningReader.Read(
            _cache, "Sena 3", [$"warning: lex entry \"{_seed.FirstEntryId:D}\": msa \"{MsaId}\" does not resolve"]));

        Assert.Equal((MsaId, "missing"), (warning.Problem[1].Text, warning.Problem[1].Role));
        Assert.Null(warning.Problem[1].FieldWorksLink);
    }

    // FieldWorks decodes the query as one string and then splits it on '&'; decoding here does the same.
    private static string QueryOf(GrammarWarningPart part)
    {
        Assert.NotNull(part.FieldWorksLink);
        const string prefix = "silfw://localhost/link?";
        Assert.StartsWith(prefix, part.FieldWorksLink, StringComparison.Ordinal);
        return HttpUtility.UrlDecode(part.FieldWorksLink[prefix.Length..]);
    }
}
