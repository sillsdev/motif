using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class OwningFieldCapabilityTests(PristineProjectFixture pristine)
{
    public static IEnumerable<object[]> OwningDeclarations()
    {
        OperationRegistryBootstrap.Initialize();
        foreach (var kind in OperationHandlerRegistry.RegisteredKinds.Order(StringComparer.Ordinal))
        foreach (var write in OperationHandlerRegistry.FieldWrites(kind))
            if (write.Verb is RecordWriteVerb.AddObject or RecordWriteVerb.RemoveObject)
                yield return [kind, write.DeclaringClass, write.Field, write.Verb];
    }

    [Theory]
    [MemberData(nameof(OwningDeclarations))]
    public void OwningDeclarationAgreesWithRunner(string kind, string declaringClass, string field, RecordWriteVerb verb)
    {
        using var cache = pristine.NewScratch();
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var flid = metadata.GetFieldId(declaringClass, field, false);
        var destination = metadata.GetDstClsId(flid);
        var types = metadata.GetAllSubclasses(destination).Append(destination).Distinct()
            .Where(id => !metadata.GetAbstract(id)).Select(metadata.GetClassName).Order(StringComparer.Ordinal).ToArray();
        var write = Assert.Single(OperationHandlerRegistry.FieldWrites(kind),
            write => write.DeclaringClass == declaringClass && write.Field == field && write.Verb == verb);
        Assert.NotEmpty(types);
        Assert.Empty(write.ChildClasses.Except(types, StringComparer.Ordinal));

        string[] ownerClasses = declaringClass switch
        {
            "CmPossibility" => ["CmPossibility", "PartOfSpeech"],
            "PhTerminalUnit" => ["PhBdryMarker", "PhPhoneme"],
            "PhSegmentRule" => ["PhMetathesisRule", "PhRegularRule", "PhSegmentRule"],
            _ => [declaringClass],
        };
        var failures = new List<string>();
        foreach (var ownerClass in ownerClasses)
        foreach (var childClass in types)
        {
            var probe = new Probe(cache, kind, ownerClass, flid, childClass, metadata.GetClassName(destination));
            var (ran, refusal) = verb == RecordWriteVerb.AddObject ? probe.Create() : probe.Delete();
            var declared = (write.OwnerClass is null || write.OwnerClass == ownerClass) &&
                (write.ChildClasses.Count == 0 || write.ChildClasses.Contains(childClass, StringComparer.Ordinal));
            var subject = $"{ownerClass}.{field} / {childClass}";
            if (ran != declared)
                failures.Add($"{subject}: Runner {(ran ? "executed" : $"refused ({refusal})")}, declaration says {declared}.");
            if (ran && declared)
            {
                var placement = verb == RecordWriteVerb.AddObject ? probe.CreatePlacement() : probe.DeletePlacement();
                if (placement != write.Placement)
                    failures.Add($"{subject}: Runner uses {placement} placement, declaration says {write.Placement}.");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private sealed class Probe(LcmCache cache, string kind, string ownerClass, int flid, string childClass,
        string destinationClass)
    {
        private CellarPropertyType FieldType => (CellarPropertyType)cache.MetaDataCacheAccessor.GetFieldType(flid);
        private bool Atomic => FieldType == CellarPropertyType.OwningAtomic;
        private IOperationHandler Handler => OperationHandlerRegistry.Resolve(kind, "owning capability test");

        internal (bool Ran, string? Refusal) Create()
        {
            using var unit = RollbackUnit();
            var owner = MakeCreateOwner();
            var id = CanonicalId.Mint();
            var error = ApplyCreate(owner, id, placement: null);
            return (error is null, error?.Message);
        }

        internal FieldWritePlacement CreatePlacement()
        {
            if (Atomic)
            {
                using var unit = RollbackUnit();
                var owner = MakeCreateOwner();
                ApplyCreate(owner, CanonicalId.Mint(), new(after: null, before: Id(owner)));
                return FieldWritePlacement.Forbidden;
            }

            var absent = CreateBesideIncumbent(place: null);
            var before = CreateBesideIncumbent(place: true);
            var after = CreateBesideIncumbent(place: false);
            if (FieldType == CellarPropertyType.OwningSequence && before == 0 && after == 1)
            {
                Assert.Null(absent);
                return FieldWritePlacement.RequiredWhenNonempty;
            }
            Assert.NotNull(absent);
            var sameResult = FieldType == CellarPropertyType.OwningSequence ? before == after : before.HasValue == after.HasValue;
            Assert.True(sameResult, $"{kind} responds to placement without honoring both anchors.");
            return FieldWritePlacement.Forbidden;
        }

        private int? CreateBesideIncumbent(bool? place)
        {
            using var unit = RollbackUnit();
            var owner = MakeCreateOwner();
            var incumbent = CanonicalId.Mint();
            Assert.Null(ApplyCreate(owner, incumbent, placement: null));
            var placement = place switch
            {
                true => new Placement(after: null, before: incumbent),
                false => new Placement(after: incumbent, before: null),
                null => null,
            };
            var added = CanonicalId.Mint();
            return ApplyCreate(owner, added, placement) is null
                ? Array.IndexOf(Members(owner), added.ToGuid()) : null;
        }

        private Exception? ApplyCreate(ICmObject owner, CanonicalId id, Placement? placement)
        {
            var before = Members(owner);
            var operation = new OperationEnvelope(CanonicalId.Mint(), kind, entityId: id, target: Id(owner),
                after: JsonSerializer.SerializeToElement(CreatePayload()), placement: placement);
            var error = Apply(operation);
            var after = Members(owner);
            if (error is null)
            {
                Assert.Equal(childClass, cache.ServiceLocator.ObjectRepository.GetObject(id.ToGuid()).ClassName);
                Assert.Contains(id.ToGuid(), after);
                Assert.Equal(before, after.Where(member => member != id.ToGuid()));
            }
            else
                Assert.Equal(before, after);
            return error;
        }

        internal (bool Ran, string? Refusal) Delete() => TryDelete(positioned: null);

        internal FieldWritePlacement DeletePlacement()
        {
            var (againstSibling, _) = TryDelete(positioned: true);
            var (againstMissingSibling, _) = TryDelete(positioned: false);
            Assert.Equal(againstSibling, againstMissingSibling);
            return FieldWritePlacement.Forbidden;
        }

        private (bool Ran, string? Refusal) TryDelete(bool? positioned)
        {
            using var unit = RollbackUnit();
            var owner = MakeOwner(cache, ownerClass);
            var child = Attach(cache, owner, flid, childClass);
            if (kind == BoundaryMarkerDeleteOperationKinds.DeleteBoundaryMarkers)
                SetWordBoundarySpellings((IPhPhonemeSet)owner, (IPhBdryMarker)child);

            ICmObject? sibling = Atomic ? null : Attach(cache, owner, flid, childClass);
            var before = Members(owner);
            var childGuid = child.Guid;
            var (target, payload) = DeleteArguments(owner, child);
            var placement = positioned switch
            {
                true => new Placement(after: null, before: Id(sibling ?? owner)),
                false => new Placement(after: null, before: CanonicalId.Mint()),
                null => null,
            };
            var operation = new OperationEnvelope(CanonicalId.Mint(), kind, target: Id(target),
                after: JsonSerializer.SerializeToElement(payload), placement: placement);
            var error = Apply(operation);
            Assert.Equal(error is null ? before.Where(member => member != childGuid) : before, Members(owner));
            return (error is null, error?.Message);
        }

        private void SetWordBoundarySpellings(IPhPhonemeSet owner, IPhBdryMarker duplicate)
        {
            var standard = owner.BoundaryMarkersOC.SingleOrDefault(marker => marker.Guid == LangProjectTags.kguidPhRuleWordBdry);
            if (standard is null)
            {
                standard = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(LangProjectTags.kguidPhRuleWordBdry);
                owner.BoundaryMarkersOC.Add(standard);
            }
            foreach (var marker in new[] { standard, duplicate })
            {
                var code = (IPhCode)Attach(cache, marker, "Codes", "PhCode");
                code.Representation.set_String(cache.DefaultVernWs, "#");
            }
        }

        private (ICmObject Target, object Payload) DeleteArguments(ICmObject owner, ICmObject child) => kind switch
        {
            LexEntryLexemeFormOperationKinds.DeleteLexemeForm or MoStemMsaMsFeaturesOperationKinds.DeleteMsFeatures =>
                (owner, new { }),
            AlternateFormRemoveOperationKinds.Remove => (owner, new { form = Id(child).Value }),
            PhonologicalRuleDeleteOperationKinds.DeleteRightHandSides =>
                (child, new { graphDigest = ReadGraphDigest(child) }),
            LexSenseRemoveOperationKinds.Remove => (child, new
            {
                owner = Id(owner).Value,
                topLevel = owner is ILexEntry,
                entry = Id(owner is ILexEntry ? owner : owner.OwnerOfClass<ILexEntry>()!).Value,
                msas = Array.Empty<string>(),
            }),
            _ => (child, new { }),
        };

        private string ReadGraphDigest(ICmObject child)
        {
            var operation = new OperationEnvelope(CanonicalId.Mint(), kind, target: Id(child));
            var effects = Assert.IsAssignableFrom<ICompoundOperationHandler>(Handler)
                .ReadCurrentFootprintEffects(cache, operation);
            return Assert.Single(effects, effect => effect.Before.ContainsKey("digest")).Before["digest"];
        }

        private object CreatePayload()
        {
            var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
            return kind switch
            {
                LexEntryLexemeFormOperationKinds.CreateLexemeForm or LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms =>
                    FormPayload(ws),
                VariantRefCreateOperationKinds.Create => new
                {
                    component = Id(MakeOwner(cache, "LexEntry")).Value,
                    variantType = Id(cache.LangProject.LexDbOA.VariantEntryTypesOA.PossibilitiesOS
                        .OfType<ILexEntryType>().First()).Value,
                },
                ComplexFormRefCreateOperationKinds.Create => new
                {
                    complexTypes = new[] { Id(cache.LangProject.LexDbOA.ComplexEntryTypesOA.PossibilitiesOS
                        .OfType<ILexEntryType>().First()).Value },
                    components = new[] { Id(MakeOwner(cache, "LexEntry")).Value },
                },
                LexExampleOperationKinds.Create => new { text = new[] { new { ws, text = "example" } } },
                WfiAnalysisOperationKinds.CreateAnalysis => new { morphs = new[] { new { guessedString = "form" } } },
                FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs when childClass == "FsClosedValue" => new { },
                CmPossibilityListPossibilitiesOperationKinds.Create or CmPossibilitySubPossibilitiesOperationKinds.Create =>
                    new { @class = childClass },
                _ when childClass != destinationClass => new { @class = childClass },
                _ => new { },
            };
        }

        private object FormPayload(string ws)
        {
            var prefix = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value;
            return childClass switch
            {
                "MoStemAllomorph" => new
                {
                    morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value, ws, text = "form",
                },
                "MoAffixAllomorph" => new { morphType = prefix, ws, text = "form" },
                _ => new { @class = childClass, morphType = prefix },
            };
        }

        private Exception? Apply(OperationEnvelope operation)
        {
            try { Handler.ApplyAndCaptureEffect(cache, operation, []); }
            catch (Exception error) when (error is InvalidOperationException or ContractParseException) { return error; }
            return null;
        }

        private Guid[] Members(ICmObject owner)
        {
            var data = cache.DomainDataByFlid;
            IEnumerable<int> handles = Atomic ? [data.get_ObjectProp(owner.Hvo, flid)] :
                Enumerable.Range(0, data.get_VecSize(owner.Hvo, flid)).Select(i => data.get_VecItem(owner.Hvo, flid, i));
            var ids = handles.Where(hvo => hvo != 0)
                .Select(hvo => cache.ServiceLocator.ObjectRepository.GetObject(hvo).Guid);
            return FieldType == CellarPropertyType.OwningCollection ? ids.Order().ToArray() : ids.ToArray();
        }

        private ICmObject MakeCreateOwner()
        {
            var owner = kind == CmPossibilityListPossibilitiesOperationKinds.Create
                ? PossibilityList() : MakeOwner(cache, ownerClass);
            if (kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms)
                Attach(cache, owner, "LexemeForm", childClass == "MoStemAllomorph" ? "MoStemAllomorph" : "MoAffixAllomorph");
            if (FieldType != CellarPropertyType.OwningCollection)
            {
                foreach (var id in Members(owner))
                    cache.ServiceLocator.ObjectRepository.GetObject(id).Delete();
                Assert.Empty(Members(owner));
            }
            return owner;
        }

        private ICmPossibilityList PossibilityList()
        {
            var factory = cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>();
            if (childClass == "CmPossibility")
                return cache.LangProject.MorphologicalDataOA.ProdRestrictOA ??= factory.Create();
            if (childClass == "PhPhonRuleFeat")
                return cache.LangProject.PhonologicalDataOA.PhonRuleFeatsOA ??= factory.Create();
            var list = factory.CreateUnowned("Probe", cache.DefaultAnalWs);
            list.ItemClsid = cache.MetaDataCacheAccessor.GetClassId(childClass);
            return list;
        }

        private UndoableUnitOfWorkHelper RollbackUnit() => new(cache.ActionHandlerAccessor, "owning probe", "owning probe");
    }

    private static ICmObject MakeOwner(LcmCache cache, string name) => name switch
    {
        "CmPossibilityList" => cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>()
            .CreateUnowned("Probe", cache.DefaultAnalWs),
        "CmPossibility" => Attach(cache, MakeOwner(cache, "CmPossibilityList"), "Possibilities", name),
        "FsFeatureSystem" => cache.LangProject.MsFeatureSystemOA,
        "FsClosedFeature" => Attach(cache, MakeOwner(cache, "FsFeatureSystem"), "Features", name),
        "FsFeatStruc" => Attach(cache, MakeOwner(cache, "MoStemMsa"), "MsFeatures", name),
        "LexEntry" => cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(),
        "LexSense" => Attach(cache, MakeOwner(cache, "LexEntry"), "Senses", name),
        "MoAffixAllomorph" or "MoAffixProcess" => Attach(cache, MakeOwner(cache, "LexEntry"), "LexemeForm", name),
        "MoDerivAffMsa" or "MoInflAffMsa" or "MoStemMsa" =>
            Attach(cache, MakeOwner(cache, "LexEntry"), "MorphoSyntaxAnalyses", name),
        "MoInflClass" => Attach(cache, MakeOwner(cache, "PartOfSpeech"), "InflectionClasses", name),
        "MoStemName" => Attach(cache, MakeOwner(cache, "PartOfSpeech"), "StemNames", name),
        "PartOfSpeech" => Attach(cache, cache.LangProject.PartsOfSpeechOA, "Possibilities", name),
        "PhPhonData" => cache.LangProject.PhonologicalDataOA,
        "PhNCFeatures" => Attach(cache, MakeOwner(cache, "PhPhonData"), "NaturalClasses", name),
        "PhPhonemeSet" => cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault() ??
            Attach(cache, MakeOwner(cache, "PhPhonData"), "PhonemeSets", name),
        "PhPhoneme" => Attach(cache, MakeOwner(cache, "PhPhonemeSet"), "Phonemes", name),
        "PhBdryMarker" => Attach(cache, MakeOwner(cache, "PhPhonemeSet"), "BoundaryMarkers", name),
        "PhMetathesisRule" or "PhRegularRule" or "PhSegmentRule" => Attach(cache, MakeOwner(cache, "PhPhonData"), "PhonRules", name),
        "PhSegRuleRHS" => Attach(cache, MakeOwner(cache, "PhRegularRule"), "RightHandSides", name),
        "RnResearchNbk" => cache.LangProject.ResearchNotebookOA,
        "RnGenericRec" => Attach(cache, MakeOwner(cache, "RnResearchNbk"), "Records", name),
        "StText" => Attach(cache, MakeOwner(cache, "RnGenericRec"), "Description", name),
        "WfiWordform" => cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
            .Create(TsStringUtils.MakeString("form", cache.DefaultVernWs)),
        _ => throw new InvalidOperationException($"Add an owning-field test fixture for {name}."),
    };

    private static ICmObject Attach(LcmCache cache, ICmObject owner, string field, string childClass) =>
        Attach(cache, owner, cache.MetaDataCacheAccessor.GetFieldId(owner.ClassName, field, true), childClass);

    private static ICmObject Attach(LcmCache cache, ICmObject owner, int flid, string childClass)
    {
        var metadata = cache.MetaDataCacheAccessor;
        var data = cache.DomainDataByFlid;
        var index = (CellarPropertyType)metadata.GetFieldType(flid) switch
        {
            CellarPropertyType.OwningAtomic => -2,
            CellarPropertyType.OwningCollection => -1,
            CellarPropertyType.OwningSequence => data.get_VecSize(owner.Hvo, flid),
            _ => throw new InvalidOperationException($"{owner.ClassName} field {flid} is not owning."),
        };
        var hvo = data.MakeNewObject(metadata.GetClassId(childClass), owner.Hvo, flid, index);
        return cache.ServiceLocator.ObjectRepository.GetObject(hvo);
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
}
