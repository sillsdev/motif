using System;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class ProjectPhonologicalRuleRecordTests : IClassFixture<RuleGrammar>
    {
        readonly RuleGrammar _g;

        public ProjectPhonologicalRuleRecordTests(RuleGrammar grammar) => _g = grammar;

        LcmCache Cache => _g.Cache;

        ProjectRecordExporter Exporter() =>
            new ProjectRecordExporter(Cache, ProjectRecordSchema.Generate(Cache, ProjectRecordSchema.PhonologicalRuleOverlay));

        void Name(IPhSegmentRule rule, string name) => rule.Name.set_String(Cache.DefaultAnalWs, TsStringUtils.MakeString(name, Cache.DefaultAnalWs));

        IPhRegularRule Voicing(string name)
        {
            IPhRegularRule rule = null!;
            _g.Fixture.Change(() =>
            {
                rule = _g.Rule(out var rhs);
                Name(rule, name);
                rule.Description.set_String(Cache.DefaultAnalWs, TsStringUtils.MakeString("Stops voice between vowels.", Cache.DefaultAnalWs));
                rule.StrucDescOS.Add(_g.Seg("p"));
                rhs.StrucChangeOS.Add(_g.Seg("b"));
                rhs.LeftContextOA = _g.Nc(_g.Class("V"));
                rhs.RightContextOA = _g.Nc(_g.Class("V"));
                rule.Direction = 1;
                rule.Disabled = true;
            });
            return rule;
        }

        [Fact]
        public void TheOverlayGeneratesAKindWithBothConcreteRuleClassesAsRoots()
        {
            var schema = Exporter().Schema;
            var kind = schema.Kind("grammar/phonological-rule")!;
            Assert.Equal(3, kind.PathSegments);
            Assert.Contains("PhMetathesisRule", kind.Roots.Select(r => r.Name));
            Assert.Contains("PhRegularRule", kind.Roots.Select(r => r.Name));
            Assert.Equal(RecordFieldKind.Notation, schema.ObjectType("PhRegularRule")!.Field("struc-desc")!.Kind);
            Assert.Equal(RecordFieldKind.Notation, schema.ObjectType("PhSegRuleRHS")!.Field("left-context")!.Kind);
            Assert.NotNull(schema.ObjectType("PhSegmentRule")?.Field("direction") ?? schema.ObjectType("PhRegularRule")!.Field("direction"));
        }

        [Fact]
        public void ARegularRuleExportsItsTextFieldsFormulasAndTypedScalars()
        {
            var rule = Voicing("Intervocalic voicing");
            var export = Exporter().Export("grammar/phonological-rule", rule.Guid);
            Assert.Equal("/grammar/phonological-rules/Intervocalic voicing", export.Path);
            Assert.NotNull(export.Text);
            Assert.Contains("class: \"PhRegularRule\"", export.Text);
            Assert.Contains("struc-desc: \"p\"", export.Text);
            Assert.Contains("struc-change: \"b\"", export.Text);
            Assert.Contains("left-context: \"[V]\"", export.Text);
            Assert.Contains("right-context: \"[V]\"", export.Text);
            Assert.Contains("direction: 1", export.Text);
            Assert.Contains("disabled: true", export.Text);
            Assert.Contains("Stops voice between vowels.", export.Text);
            Assert.DoesNotContain(export.Unexported, u => u.Reason.Contains("could not be converted"));
        }

        [Fact]
        public void ExportedTextReadsBackAsTheSameDocument()
        {
            var rule = Voicing("Read back");
            var exporter = Exporter();
            var export = exporter.Export("grammar/phonological-rule", rule.Guid);
            var read = RecordFileReader.Read(export.Text!, exporter.Schema, exporter.CreateReadOptions());
            Assert.False(read.HasErrors, string.Join("\n", read.Diagnostics));
            Assert.Equal(export.Document.Data.Guid, read.Document!.Data.Guid);
            Assert.Equal("PhRegularRule", read.Document.Data.Class);
        }

        [Fact]
        public void AMetathesisRuleExportsNameAndDescriptionAndReportsItsFormulaUnexported()
        {
            IPhMetathesisRule rule = null!;
            _g.Fixture.Change(() =>
            {
                rule = Cache.ServiceLocator.GetInstance<IPhMetathesisRuleFactory>().Create();
                _g.Data.PhonRulesOS.Add(rule);
                Name(rule, "Swap");
                rule.Description.set_String(Cache.DefaultAnalWs, TsStringUtils.MakeString("Swaps two segments.", Cache.DefaultAnalWs));
                rule.StrucDescOS.Add(_g.Seg("p"));
            });
            var export = Exporter().Export("grammar/phonological-rule", rule.Guid);
            Assert.Contains("class: \"PhMetathesisRule\"", export.Text);
            Assert.Contains("Swaps two segments.", export.Text);
            Assert.DoesNotContain("struc-desc", export.Text);
            Assert.Contains(export.Unexported, u => u.Field == "StrucDesc" && u.Reason.Contains(RuleNotation.Unrenderable));
        }

        [Fact]
        public void RulesWithTheSameNameOrNoNameGetUniquePathsAndExportAllKeepsRuleOrder()
        {
            IPhRegularRule first = null!, second = null!, blank = null!, last = null!;
            _g.Fixture.Change(() =>
            {
                foreach (var old in _g.Data.PhonRulesOS.ToList()) _g.Data.PhonRulesOS.Remove(old);
                first = _g.Rule(out _); Name(first, "Zeta");
                second = _g.Rule(out _); Name(second, "Dup");
                blank = _g.Rule(out _);
                last = _g.Rule(out _); Name(last, "Dup");
            });
            var all = Exporter().ExportAll("grammar/phonological-rule");
            Assert.Equal(new[] { first.Guid, second.Guid, blank.Guid, last.Guid }, all.Select(e => e.Guid));
            Assert.Equal("/grammar/phonological-rules/Zeta", all[0].Path);
            Assert.Equal("/grammar/phonological-rules/Dup(" + second.Guid.ToString("N").Substring(0, 8) + ")", all[1].Path);
            Assert.Equal("/grammar/phonological-rules/" + blank.Guid.ToString("D"), all[2].Path);
            Assert.Equal("/grammar/phonological-rules/Dup(" + last.Guid.ToString("N").Substring(0, 8) + ")", all[3].Path);
            Assert.Equal(4, all.Select(e => e.Path).Distinct().Count());
        }

        [Fact]
        public void APathResolvesToTheRuleByNameAndByGuidSuffix()
        {
            IPhRegularRule rule = null!;
            _g.Fixture.Change(() => { rule = _g.Rule(out _); Name(rule, "Resolvable"); });
            var resolver = new ProjectPathResolver(Cache);
            Assert.Equal(rule.Guid, resolver.Resolve(ProjectPath.Parse("/grammar/phonological-rules/Resolvable")!, out _));
            Assert.Equal("/grammar/phonological-rules/Resolvable", resolver.PathOf(rule.Guid)!.ToString());
        }
    }
}
