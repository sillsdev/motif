using System.Reflection;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class RetirementShapeTests
{
    private static IntentShape ShapeOf(string verb) => ProposalCommands.DraftComposers[verb].Shape!;

    private static IntentShape Nested(IntentShape shape, string field) => shape.Fields.Single(item => item.Name == field).Shape!;

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    // Every constructor parameter of a closed contract record is a required, closed property of its shape.
    private static void AssertShapeMatchesRecord(IntentShape shape, Type record, params string[] alsoDeclared)
    {
        var parameters = record.GetConstructors().Single().GetParameters();
        var declared = parameters.Select(parameter => CamelCase(parameter.Name!)).Concat(alsoDeclared)
            .Order(StringComparer.Ordinal);
        Assert.Equal(declared, shape.Fields.Select(field => field.Name).Order(StringComparer.Ordinal));
        var nullability = new NullabilityInfoContext();
        foreach (var parameter in parameters)
        {
            var field = shape.Fields.Single(item => item.Name == CamelCase(parameter.Name!));
            Assert.Equal(!parameter.HasDefaultValue, field.Required);
            if (field.Kind is IntentFieldKind.Id or IntentFieldKind.Text or IntentFieldKind.Number)
                Assert.Equal(nullability.Create(parameter).WriteState == NullabilityState.Nullable, field.Nullable);
        }
    }

    [Fact]
    public void TheRetirementComponentAndEveryPartOfItMatchTheContractRecords()
    {
        var retirement = Nested(ShapeOf("retire-allomorph"), "retirement");
        AssertShapeMatchesRecord(retirement, typeof(RetireAllomorphIntent));
        var identity = Nested(retirement, "retiredForms");
        AssertShapeMatchesRecord(identity, typeof(AllomorphIdentity));
        var roles = Nested(retirement, "roleReplacements");
        AssertShapeMatchesRecord(roles, typeof(AllomorphRoleReplacement));
        AssertShapeMatchesRecord(Nested(roles, "replacement"), typeof(AllomorphIdentity));
        AssertShapeMatchesRecord(Nested(retirement, "bundles"), typeof(RetirementBundle));
        var adhoc = Nested(retirement, "adhocReplacements");
        AssertShapeMatchesRecord(adhoc, typeof(AdhocOccurrenceReplacement));
        AssertShapeMatchesRecord(Nested(adhoc, "replacement"), typeof(AllomorphIdentity));
    }

    [Fact]
    public void TheRuleReplacementPartsMatchTheContractRecords()
    {
        var shape = ShapeOf("retire-allomorph");
        AssertShapeMatchesRecord(Nested(shape, "naturalClass"), typeof(RetirementNaturalClass));
        var rule = Nested(shape, "rule");
        AssertShapeMatchesRecord(rule, typeof(RetirementRule));
        AssertShapeMatchesRecord(Nested(rule, "left"), typeof(RetirementContextAtom));
        AssertShapeMatchesRecord(Nested(rule, "right"), typeof(RetirementContextAtom));
        AssertShapeMatchesRecord(Nested(rule, "placement"), typeof(RetirementRulePlacement));
        AssertShapeMatchesRecord(Nested(shape, "operations"), typeof(RetirementOperationBinding));
        AssertShapeMatchesRecord(Nested(shape, "display"), typeof(RetirementDisplay));
        AssertShapeMatchesRecord(Nested(shape, "retirements"), typeof(RetireAllomorphIntent));
    }

    [Fact]
    public void TheTopLevelIsTheUnionOfTheTwoDocumentsWithOnlyFormatAndVersionRequired()
    {
        var shape = ShapeOf("retire-allomorph");
        var union = typeof(RetireAllomorphIntentDocument).GetConstructors().Single().GetParameters()
            .Concat(typeof(ReplaceListedAllomorphsWithRuleIntent).GetConstructors().Single().GetParameters())
            .Select(parameter => CamelCase(parameter.Name!)).Concat(["format", "version"]).Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(union, shape.Fields.Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["format", "version"],
            shape.Fields.Where(field => field.Required).Select(field => field.Name).Order(StringComparer.Ordinal));
        var format = shape.Fields.Single(field => field.Name == "format");
        Assert.Equal(["motif-retire-allomorph", "motif-allomorph-retirement"], format.Values);
    }

    [Fact]
    public void TheZeroAffixShapeIsTheClosedDocument()
    {
        var shape = ShapeOf("retire-redundant-zero-affix");
        Assert.Equal(["format", "version", "retirement"], shape.Fields.Select(field => field.Name));
        Assert.All(shape.Fields, field => Assert.True(field.Required));
        Assert.Equal(["motif-retire-redundant-zero-affix"], shape.Fields[0].Values);
        AssertShapeMatchesRecord(Nested(shape, "retirement"), typeof(RetireRedundantZeroAffixIntent));
    }

    [Fact]
    public void EveryValidPortableRuleReplacementVectorPassesTheShape()
    {
        using var stream = typeof(AllomorphRetirementCodec).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.allomorph-retirement.vectors.json")!;
        using var document = JsonDocument.Parse(stream);
        var shape = ShapeOf("retire-allomorph");
        var valid = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray()
                     .Where(vector => !vector.TryGetProperty("error", out _)))
        {
            shape.RequireClosed(vector.GetProperty("input"), "RetireAllomorph");
            valid++;
        }
        Assert.Equal(7, valid);
    }

    [Fact]
    public void EveryValidPortableZeroAffixVectorPassesTheShape()
    {
        using var stream = typeof(RetireRedundantZeroAffixCodec).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.retire-redundant-zero-affix.vectors.json")!;
        using var document = JsonDocument.Parse(stream);
        var shape = ShapeOf("retire-redundant-zero-affix");
        var valid = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray()
                     .Where(vector => !vector.TryGetProperty("error", out _)))
        {
            shape.RequireClosed(vector.GetProperty("input"), "RetireRedundantZeroAffix");
            valid++;
        }
        Assert.True(valid > 0);
    }

    [Fact]
    public void AStandaloneDocumentWithEveryPartPassesTheShapeAndAnExtraPropertyDoesNot()
    {
        const string identity = """{"id":"a","entry":"b","class":"MoAffixAllomorph","location":"alternate","position":"prefix","semanticDigest":"d","stemName":null}""";
        var document = JsonDocument.Parse($$"""
            {"format":"motif-retire-allomorph","version":1,
             "retirement":{"entry":"b","scope":"affix","retiredForms":[{{identity}}],
               "roleReplacements":[{"retiredForm":"a","msa":"m","inflType":null,"expansionRole":"whole","replacement":{{identity}}}],
               "bundles":[{"bundle":"u","analysis":"v","wordform":"w","retiredForm":"a","msa":"m","inflType":null,"expansionRole":"whole"}],
               "adhocReplacements":[{"rule":"r","field":"FirstAllomorph","ordinal":null,"retiredForm":"a","replacement":{{identity}}}]},
             "duplicateSurvivors":[{{identity}}]}
            """).RootElement.Clone();
        var shape = ShapeOf("retire-allomorph");

        shape.RequireClosed(document, "RetireAllomorph");

        var extra = JsonDocument.Parse(document.GetRawText().Replace("\"bundle\":\"u\"", "\"bundle\":\"u\",\"extra\":1"))
            .RootElement;
        var refused = Assert.Throws<SIL.Motif.Contract.Parsing.ContractParseException>(() =>
            shape.RequireClosed(extra, "RetireAllomorph"));
        Assert.Equal("'RetireAllomorph.retirement.bundles': unknown property 'extra'.", refused.Message);
    }

    [Fact]
    public void ADispositionShapeNamesEveryIntentPropertyTheParserAccepts()
    {
        var shape = ShapeOf("compose-record-parsimony-disposition");
        var intent = typeof(SIL.Motif.Runner.Composers.RecordParsimonyDispositionIntent).GetConstructors().Single()
            .GetParameters().Select(parameter => CamelCase(parameter.Name!)).Order(StringComparer.Ordinal);
        Assert.Equal(intent, shape.Fields.Select(field => field.Name).Order(StringComparer.Ordinal));
        var kinds = typeof(HumanJudgmentSubject).GetCustomAttributes<System.Text.Json.Serialization.JsonDerivedTypeAttribute>()
            .Select(attribute => (string)attribute.TypeDiscriminator!).Order(StringComparer.Ordinal);
        Assert.Equal(kinds, Nested(shape, "subject").Fields.Single(field => field.Name == "kind").Values!
            .Order(StringComparer.Ordinal));
    }
}
