using Xunit;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;

namespace SIL.Motif.Tests.Commands.Commands;

public sealed class ApprovedExpectationsDigestTests
{
    [Fact]
    public void DigestChangesWhenApprovedMorphologyChanges()
    {
        var first = new Dictionary<string, IReadOnlyList<ApprovedMorphology>>
        {
            ["word"] = [new ApprovedMorphology([new ApprovedMorph("form", "msa", null, [])])]
        };
        var changed = new Dictionary<string, IReadOnlyList<ApprovedMorphology>>
        {
            ["word"] = [new ApprovedMorphology([new ApprovedMorph("other", "msa", null, [])])]
        };

        Assert.NotEqual(ApprovedExpectationsDigest.Compute(first), ApprovedExpectationsDigest.Compute(changed));
    }

    [Fact]
    public void DigestDoesNotDependOnDictionaryOrAnalysisOrder()
    {
        var a = new ApprovedMorphology([new ApprovedMorph("a", null, null, [])]);
        var b = new ApprovedMorphology([new ApprovedMorph("b", null, null, [])]);
        var first = new Dictionary<string, IReadOnlyList<ApprovedMorphology>> { ["x"] = [a, b], ["y"] = [] };
        var second = new Dictionary<string, IReadOnlyList<ApprovedMorphology>> { ["y"] = [], ["x"] = [b, a] };

        Assert.Equal(ApprovedExpectationsDigest.Compute(first), ApprovedExpectationsDigest.Compute(second));
    }
}
