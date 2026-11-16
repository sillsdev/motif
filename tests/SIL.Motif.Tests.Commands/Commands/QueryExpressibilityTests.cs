using SIL.Motif.Commands;
using SIL.Motif.Contract.Query;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class QueryExpressibilityTests
{
    [Theory]
    [InlineData("/_ [NO_SUCH_CLASS]")]
    [InlineData("/_ [")]
    [InlineData("6991d981-5043-4b36-85e2-60760d25999f")]
    [InlineData("/_ a")]
    public void EnvironmentTextCannotCertifyAResolvedFieldWorksContext(string environment)
    {
        Assert.Contains(PhonologicalRuleShapes.Context.Fields, f => f.Name == "phoneme" && f.Kind == IntentFieldKind.Id && f.Role == IdRole.Reference);
        Assert.Contains(PhonologicalRuleShapes.Context.Fields, f => f.Name == "naturalClass" && f.Role == IdRole.Reference);
        Assert.Contains(PhonologicalRuleShapes.Context.Fields, f => f.Name == "boundaryMarker");
        Assert.Contains(PhonologicalRuleShapes.Outcome.Fields, f => f.Name == "right" && ReferenceEquals(f.Shape, PhonologicalRuleShapes.Context));
        var rows = Enumerable.Range(0, 100).Select(i => new QueryForm("f" + i, "ka", "ka", "lexeme", "e" + i,
            "stem", "", i < 25 ? [environment] : [], ["N"])).ToList();
        var result = FormQuery.Run(new QueryRecord(QuerySources.Forms, [], [], Score: ["split"],
            Members: rows.Take(25).Select(r => r.Id).ToArray()), rows, ["k", "a"]);
        var context = result.Scores!.Single().Contexts!.Single(c => c.Column == "environments" && c.Value == environment);
        Assert.Equal("not-checked", context.Expressible);
        Assert.Equal("not-checked", context.Status);
    }
}
