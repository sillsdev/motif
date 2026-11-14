using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsimony.RuleAttempts;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony.RuleAttempts;

public sealed class VariantOperationShapesTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static OperationEnvelope Op(string kind, CanonicalId? entity = null, CanonicalId? target = null, object? after = null) =>
        new(CanonicalId.Mint(), kind, entity, target, after is null ? null : Json(after));

    [Fact]
    public void FiveSameShapeVariantsOfExistingEntriesGroupFromTheOperationsAlone()
    {
        var ops = new List<OperationEnvelope>();
        var env = CanonicalId.Mint();
        var type = CanonicalId.Mint();
        var mains = new Dictionary<string, string>();
        for (var i = 0; i < 5; i++)
        {
            var main = CanonicalId.Mint();
            mains[main.CanonicalSuffix] = "kata" + (char)('a' + i) + "a";
            var entry = CanonicalId.Mint();
            var form = CanonicalId.Mint();
            ops.Add(Op("lexical/lexDb/createEntries", entity: entry));
            ops.Add(Op("lexical/lexEntry/createLexemeForm", entity: form, target: entry, after: new { text = "kata" + (char)('a' + i) + "-" }));
            ops.Add(Op("grammar/moStemAllomorph/addRefPhoneEnv", target: form, after: new { member = env.Value }));
            ops.Add(Op("lexical/lexEntry/createVariantRef", entity: CanonicalId.Mint(), target: entry,
                after: new { component = main.Value, variantType = type.Value }));
        }
        var shapes = VariantOperationShapes.FromOperations(ops, id => mains.GetValueOrDefault(id));
        Assert.Equal(5, shapes.Count);
        Assert.All(shapes, s => Assert.Equal([env.CanonicalSuffix], s.EnvironmentIds));
        var finding = Assert.Single(RuleAttemptCheck.Evaluate(shapes, NoRuleAttemptLedger.Instance));
        Assert.Equal(5, finding.Group.Count);
    }

    [Fact]
    public void AVariantWhoseFormsAreUnknownIsSkipped()
    {
        var entry = CanonicalId.Mint();
        var ops = new[] { Op("lexical/lexEntry/createVariantRef", entity: CanonicalId.Mint(), target: entry,
            after: new { component = CanonicalId.Mint().Value, variantType = CanonicalId.Mint().Value }) };
        Assert.Empty(VariantOperationShapes.FromOperations(ops, _ => null));
    }
}
