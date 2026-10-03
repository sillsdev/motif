using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class StoredParseWarningsTests
{
    [Fact]
    public void EveryNamedAllomorphAndItsRecordedRefusalSurviveWithoutTruncation()
    {
        var ids = Enumerable.Range(0, 15).Select(_ => Guid.NewGuid().ToString("D")).ToArray();
        var lines = ids.Select(id => $"warning: Allomorph 'chat' could not be segmented with this project's phonemes: " +
            $"allomorph \"{id}\": cannot segment \"chat\": no character definition matches at position 0; skipped").ToArray();
        var result = StoredParseWarnings.Merge(new GrammarCheckResponse([], true), lines.Concat(lines));

        Assert.Equal(15, result.Findings.Count);
        Assert.Equal(ids, result.Findings.Select(finding => Assert.Single(finding.Subject).SubjectGuid));
        Assert.Equal(lines.Select(line => line[9..]), result.Findings.Select(finding => finding.Description));
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal("parse.allomorph.unsegmentable", finding.Code);
            Assert.Equal(GrammarDiagnosticLevel.Warning, finding.Severity);
            Assert.Equal(["chat"], Assert.Single(finding.Subject).Reach!.Spellings);
        });
    }

    [Fact]
    public void AnUnrecognisedLineDoesNotInventAnAllomorphIdentity()
    {
        var result = StoredParseWarnings.Merge(new GrammarCheckResponse([], true),
            ["warning: Lexical entry 'chat' has no usable allomorphs.", "warning: something else"]);
        Assert.Empty(result.Findings);
    }
}
