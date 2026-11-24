using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;

namespace SIL.TextEditing.Project.Tests
{
    /// <summary>A synthetic phonology in a blank project: phonemes, classes, phonological features and builders for processes and rules.</summary>
    public sealed class RuleGrammar : IDisposable
    {
        internal ProjectFixture Fixture { get; } = new ProjectFixture();
        internal LcmCache Cache => Fixture.Cache;
        internal IPhPhonemeSet Set { get; private set; } = null!;
        internal IPhBdryMarker WordBoundary { get; private set; } = null!;
        internal IPhBdryMarker MorphemeBoundary { get; private set; } = null!;
        readonly Dictionary<string, IPhPhoneme> _phonemes = new Dictionary<string, IPhPhoneme>();
        readonly Dictionary<string, IFsClosedFeature> _features = new Dictionary<string, IFsClosedFeature>();
        readonly Dictionary<string, IPhNaturalClass> _classes = new Dictionary<string, IPhNaturalClass>();

        public RuleGrammar()
        {
            Fixture.Change(() =>
            {
                var data = Cache.LangProject.PhonologicalDataOA ??= Cache.ServiceLocator.GetInstance<IPhPhonDataFactory>().Create();
                Set = data.PhonemeSetsOS.FirstOrDefault() ?? Cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
                if (!data.PhonemeSetsOS.Contains(Set)) data.PhonemeSetsOS.Add(Set);
                foreach (var old in Set.PhonemesOC.ToList()) Set.PhonemesOC.Remove(old);
                foreach (var old in Set.BoundaryMarkersOC.ToList()) Set.BoundaryMarkersOC.Remove(old);
                WordBoundary = Marker(LangProjectTags.kguidPhRuleWordBdry, "#");
                MorphemeBoundary = Marker(LangProjectTags.kguidPhRuleMorphBdry, "+");
                foreach (string code in "p t k b d g m n a i u ! +".Split(' ')) _phonemes[code] = Phoneme(code);
                Cache.LangProject.PhFeatureSystemOA ??= Cache.ServiceLocator.GetInstance<IFsFeatureSystemFactory>().Create();
                foreach (string abbreviation in new[] { "cons", "son", "voi", "nas" }) _features[abbreviation] = Feature(abbreviation);
                _classes["C"] = SegmentClass("C", "p t k b d g m n");
                _classes["V"] = SegmentClass("V", "a i u");
                _classes["Obs"] = FeatureClass("Obs", ("cons", "+"), ("son", "-"));
                _classes["Vd"] = FeatureClass("Vd", ("voi", "+"));
            });
        }

        public void Dispose() => Fixture.Dispose();

        ITsString Vern(string text) => TsStringUtils.MakeString(text, Cache.DefaultVernWs);

        IPhBdryMarker Marker(Guid guid, string code)
        {
            var marker = Cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(guid, Set);
            AddCode(marker, code);
            return marker;
        }

        internal void AddCode(IPhTerminalUnit unit, string code)
        {
            var phCode = Cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            unit.CodesOS.Add(phCode);
            phCode.Representation.set_String(Cache.DefaultVernWs, Vern(code));
        }

        IPhPhoneme Phoneme(string code)
        {
            var phoneme = Cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
            Set.PhonemesOC.Add(phoneme);
            phoneme.Name.set_String(Cache.DefaultAnalWs, TsStringUtils.MakeString("phoneme " + code, Cache.DefaultAnalWs));
            AddCode(phoneme, code);
            return phoneme;
        }

        IFsClosedFeature Feature(string abbreviation)
        {
            var feature = Cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            Cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            feature.Abbreviation.set_String(Cache.DefaultAnalWs, abbreviation);
            feature.Name.set_String(Cache.DefaultAnalWs, abbreviation + " name");
            foreach (var (name, value) in new[] { ("plus", "+"), ("minus", "-") })
            {
                var symbol = Cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                feature.ValuesOC.Add(symbol);
                symbol.Name.set_String(Cache.DefaultAnalWs, name);
                symbol.Abbreviation.set_String(Cache.DefaultAnalWs, value);
            }
            return feature;
        }

        IPhNCSegments SegmentClass(string abbreviation, string members)
        {
            var segments = Cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            Cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(segments);
            if (abbreviation.Length > 0) segments.Abbreviation.set_String(Cache.DefaultAnalWs, abbreviation);
            segments.Name.set_String(Cache.DefaultAnalWs, "class " + abbreviation);
            foreach (string code in members.Split(' ')) segments.SegmentsRC.Add(_phonemes[code]);
            return segments;
        }

        internal IPhNCFeatures FeatureClass(string? abbreviation, params (string Feature, string Value)[] values)
        {
            var features = Cache.ServiceLocator.GetInstance<IPhNCFeaturesFactory>().Create();
            Cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(features);
            if (!string.IsNullOrEmpty(abbreviation)) features.Abbreviation.set_String(Cache.DefaultAnalWs, abbreviation);
            features.Name.set_String(Cache.DefaultAnalWs, "Created automatically for rule " + abbreviation);
            features.FeaturesOA = Cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            foreach (var (feature, value) in values)
            {
                var spec = Cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
                features.FeaturesOA.FeatureSpecsOC.Add(spec);
                spec.FeatureRA = _features[feature];
                spec.ValueRA = _features[feature].ValuesOC.Single(v => v.Abbreviation.get_String(Cache.DefaultAnalWs).Text == value);
            }
            return features;
        }

        internal IPhPhoneme P(string code) => _phonemes[code];
        internal IPhNaturalClass Class(string abbreviation) => _classes[abbreviation];
        internal IFsClosedFeature F(string abbreviation) => _features[abbreviation];
        internal IPhPhonData Data => Cache.LangProject.PhonologicalDataOA;

        internal IPhFeatureConstraint Constraint(string feature)
        {
            var constraint = Cache.ServiceLocator.GetInstance<IPhFeatureConstraintFactory>().Create();
            Data.FeatConstraintsOS.Add(constraint);
            constraint.FeatureRA = _features[feature];
            return constraint;
        }

        // ------------------------------------------------------------------ contexts

        internal IPhSimpleContextSeg Seg(string code)
        {
            var context = Cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
            Data.ContextsOS.Add(context);
            context.FeatureStructureRA = _phonemes[code];
            return context;
        }

        internal IPhSimpleContextBdry Bdry(IPhBdryMarker marker)
        {
            var context = Cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create();
            Data.ContextsOS.Add(context);
            context.FeatureStructureRA = marker;
            return context;
        }

        internal IPhSimpleContextNC Nc(IPhNaturalClass? naturalClass, IPhFeatureConstraint[]? plus = null, IPhFeatureConstraint[]? minus = null)
        {
            var context = Cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
            Data.ContextsOS.Add(context);
            context.FeatureStructureRA = naturalClass;
            foreach (var constraint in plus ?? Array.Empty<IPhFeatureConstraint>()) context.PlusConstrRS.Add(constraint);
            foreach (var constraint in minus ?? Array.Empty<IPhFeatureConstraint>()) context.MinusConstrRS.Add(constraint);
            return context;
        }

        internal IPhIterationContext Iteration(IPhPhonContext member, int minimum, int maximum)
        {
            var iteration = Cache.ServiceLocator.GetInstance<IPhIterationContextFactory>().Create();
            Data.ContextsOS.Add(iteration);
            iteration.MemberRA = member;
            iteration.Minimum = minimum;
            iteration.Maximum = maximum;
            return iteration;
        }

        internal IPhSequenceContext Sequence(params IPhPhonContext[] members)
        {
            var sequence = Cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create();
            Data.ContextsOS.Add(sequence);
            foreach (var member in members) sequence.MembersRS.Add(member);
            return sequence;
        }

        // ------------------------------------------------------------------ affix process

        internal ILexEntry Entry(out IMoAffixProcess process)
        {
            var entry = Cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            process = Cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.AlternateFormsOS.Add(process);
            process.InputOS.Clear();
            process.OutputOS.Clear();
            return entry;
        }

        internal IPhVariable Gap(IMoAffixProcess process)
        {
            var variable = Cache.ServiceLocator.GetInstance<IPhVariableFactory>().Create();
            process.InputOS.Add(variable);
            return variable;
        }

        /// <summary>Owns a context in the project's context pool, where sequence and iteration members live.</summary>
        internal T Pool<T>(T context) where T : IPhPhonContext
        {
            Data.ContextsOS.Add(context);
            return context;
        }

        internal IMoCopyFromInput Copy(IMoAffixProcess process, IPhContextOrVar? content)
        {
            var copy = Cache.ServiceLocator.GetInstance<IMoCopyFromInputFactory>().Create();
            process.OutputOS.Add(copy);
            copy.ContentRA = content;
            return copy;
        }

        internal IMoModifyFromInput Modify(IMoAffixProcess process, IPhContextOrVar content, IPhNCFeatures modification)
        {
            var modify = Cache.ServiceLocator.GetInstance<IMoModifyFromInputFactory>().Create();
            process.OutputOS.Add(modify);
            modify.ContentRA = content;
            modify.ModificationRA = modification;
            return modify;
        }

        internal IMoInsertPhones Insert(IMoAffixProcess process, params IPhTerminalUnit[] units)
        {
            var insert = Cache.ServiceLocator.GetInstance<IMoInsertPhonesFactory>().Create();
            process.OutputOS.Add(insert);
            foreach (var unit in units) insert.ContentRS.Add(unit);
            return insert;
        }

        internal IMoInsertNC InsertClass(IMoAffixProcess process, IPhNaturalClass? content)
        {
            var insert = Cache.ServiceLocator.GetInstance<IMoInsertNCFactory>().Create();
            process.OutputOS.Add(insert);
            insert.ContentRA = content;
            return insert;
        }

        // ------------------------------------------------------------------ rules

        internal IPhRegularRule Rule(out IPhSegRuleRHS rhs)
        {
            var rule = Cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            Data.PhonRulesOS.Add(rule);
            rhs = rule.RightHandSidesOS.FirstOrDefault() ?? Cache.ServiceLocator.GetInstance<IPhSegRuleRHSFactory>().Create();
            if (!rule.RightHandSidesOS.Contains(rhs)) rule.RightHandSidesOS.Add(rhs);
            return rule;
        }
    }
}
