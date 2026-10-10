using SIL.Motif.Generator.Derivation;
using SIL.Motif.Generator.Checks;
using SIL.Motif.Generator.Descriptions;
using SIL.Motif.Generator.Model;

namespace SIL.Motif.Generator.Emit;

/// <summary>
/// Emits only the owning fields needed by sound-system intents. Concrete factories are explicit policy;
/// neither a caller-supplied field name nor reflection can select a construction path (ADR 0029).
/// </summary>
public static class OwningCreateCatalogWriter
{
    private sealed record Creation(
        string Owner, string Field, string Sig, FieldCard Card, string Factory, bool PositionMatters = true,
        bool SupportsMove = false);

    private static readonly Creation[] Creations =
    [
        new("PhPhonData", "PhonemeSets", "PhPhonemeSet", FieldCard.Seq,
            "var child = cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create(guid);\n" +
            "owner.PhonemeSetsOS.Insert(index, child);"),
        new("PhPhonemeSet", "BoundaryMarkers", "PhBdryMarker", FieldCard.Col,
            "var child = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(guid);\n" +
            "owner.BoundaryMarkersOC.Add(child);\n" +
            "foreach (var code in child.CodesOS.ToArray()) child.CodesOS.Remove(code);"),
        new("PhPhonemeSet", "Phonemes", "PhPhoneme", FieldCard.Col,
            "var child = cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create(guid);\n" +
            "owner.PhonemesOC.Add(child);\n" +
            "foreach (var code in child.CodesOS.ToArray()) child.CodesOS.Remove(code);"),
        new("PhTerminalUnit", "Codes", "PhCode", FieldCard.Seq,
            "var child = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create(guid);\n" +
            "owner.CodesOS.Insert(index, child);"),
        new("PhPhonData", "NaturalClasses", "PhNaturalClass", FieldCard.Seq,
            "IPhNaturalClass child = concreteClass switch\n" +
            "{\n" +
            "    \"PhNCSegments\" => cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create(guid),\n" +
            "    \"PhNCFeatures\" => cache.ServiceLocator.GetInstance<IPhNCFeaturesFactory>().Create(guid),\n" +
            "    _ => throw new InvalidOperationException(\"Natural class must be PhNCSegments or PhNCFeatures.\"),\n" +
            "};\nowner.NaturalClassesOS.Insert(index, child);"),
        new("PhPhonData", "Environments", "PhEnvironment", FieldCard.Seq,
            "var child = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create(guid);\n" +
            "owner.EnvironmentsOS.Insert(index, child);"),
        new("PhPhonData", "Contexts", "PhContextOrVar", FieldCard.Seq,
            "IPhContextOrVar child = concreteClass switch\n" +
            "{\n" +
            "    \"PhSimpleContextSeg\" => cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create(guid),\n" +
            "    \"PhSimpleContextNC\" => cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create(guid),\n" +
            "    \"PhSimpleContextBdry\" => cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create(guid),\n" +
            "    \"PhSequenceContext\" => cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create(guid),\n" +
            "    _ => throw new InvalidOperationException(\"Rule context must be a supported simple or sequence context.\"),\n" +
            "};\nowner.ContextsOS.Insert(index, child);", PositionMatters: false),
        new("PhPhonData", "PhonRules", "PhSegmentRule", FieldCard.Seq,
            "var child = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create(guid);\n" +
            "child.Disabled = true;\n" +
            "owner.PhonRulesOS.Insert(index, child);", SupportsMove: true),
        new("PhRegularRule", "RightHandSides", "PhSegRuleRHS", FieldCard.Seq,
            "var child = cache.ServiceLocator.GetInstance<IPhSegRuleRHSFactory>().Create(guid);\n" +
            "owner.RightHandSidesOS.Insert(index, child);"),
        new("PhSegmentRule", "StrucDesc", "PhSimpleContext", FieldCard.Seq,
            "IPhSimpleContext child = concreteClass switch\n" +
            "{\n" +
            "    \"PhSimpleContextSeg\" => cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create(guid),\n" +
            "    \"PhSimpleContextNC\" => cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create(guid),\n" +
            "    \"PhSimpleContextBdry\" => cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create(guid),\n" +
            "    _ => throw new InvalidOperationException(\"Structural description must be a supported simple context.\"),\n" +
            "};\nowner.StrucDescOS.Insert(index, child);"),
        new("PhSegRuleRHS", "StrucChange", "PhSimpleContext", FieldCard.Seq,
            "IPhSimpleContext child = concreteClass switch\n" +
            "{\n" +
            "    \"PhSimpleContextSeg\" => cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create(guid),\n" +
            "    \"PhSimpleContextNC\" => cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create(guid),\n" +
            "    \"PhSimpleContextBdry\" => cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create(guid),\n" +
            "    _ => throw new InvalidOperationException(\"Structural change must be a supported simple context.\"),\n" +
            "};\nowner.StrucChangeOS.Insert(index, child);"),
        new("PhSegRuleRHS", "LeftContext", "PhPhonContext", FieldCard.Atomic,
            "IPhPhonContext child = concreteClass switch\n" +
            "{\n" +
            "    \"PhSimpleContextSeg\" => cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create(guid),\n" +
            "    \"PhSimpleContextNC\" => cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create(guid),\n" +
            "    \"PhSimpleContextBdry\" => cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create(guid),\n" +
            "    \"PhSequenceContext\" => cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create(guid),\n" +
            "    _ => throw new InvalidOperationException(\"Left context must be a supported simple or sequence context.\"),\n" +
            "};\nowner.LeftContextOA = child;"),
        new("PhSegRuleRHS", "RightContext", "PhPhonContext", FieldCard.Atomic,
            "IPhPhonContext child = concreteClass switch\n" +
            "{\n" +
            "    \"PhSimpleContextSeg\" => cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create(guid),\n" +
            "    \"PhSimpleContextNC\" => cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create(guid),\n" +
            "    \"PhSimpleContextBdry\" => cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create(guid),\n" +
            "    \"PhSequenceContext\" => cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create(guid),\n" +
            "    _ => throw new InvalidOperationException(\"Right context must be a supported simple or sequence context.\"),\n" +
            "};\nowner.RightContextOA = child;"),
        new("PhNCFeatures", "Features", "FsFeatStruc", FieldCard.Atomic,
            "var child = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create(guid);\n" +
            "owner.FeaturesOA = child;"),
        new("PhPhoneme", "Features", "FsFeatStruc", FieldCard.Atomic,
            "var child = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create(guid);\n" +
            "owner.FeaturesOA = child;"),
        new("RnResearchNbk", "Records", "RnGenericRec", FieldCard.Col,
            "var child = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create(guid);\n" +
            "owner.RecordsOC.Add(child);"),
        new("RnGenericRec", "Description", "StText", FieldCard.Atomic,
            "var child = cache.ServiceLocator.GetInstance<IStTextFactory>().Create(guid);\n" +
            "owner.DescriptionOA = child;"),
        new("PartOfSpeech", "AffixSlots", "MoInflAffixSlot", FieldCard.Col,
            "var child = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create(guid);\n" +
            "owner.AffixSlotsOC.Add(child);"),
        new("PartOfSpeech", "AffixTemplates", "MoInflAffixTemplate", FieldCard.Seq,
            "var child = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create(guid);\n" +
            "owner.AffixTemplatesOS.Insert(index, child);", SupportsMove: true),
        new("StText", "Paragraphs", "StPara", FieldCard.Seq,
            "IStPara child = concreteClass switch\n" +
            "{\n" +
            "    \"StTxtPara\" => cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create(guid),\n" +
            "    _ => throw new InvalidOperationException(\"Paragraph must be StTxtPara.\"),\n" +
            "};\nowner.ParagraphsOS.Insert(index, child);"),
    ];

    private static readonly (string Owner, string Field)[] MoveOnlyFields =
    [
        ("LexEntry", "AlternateForms"),
    ];

    public static IReadOnlyList<GeneratedCatalogWriter.WrittenFile> WriteAll(LoadedMotifModel model, string repoRoot)
    {
        var selected = Creations.Select(c => (c.Owner, c.Field)).Concat(MoveOnlyFields).Concat(new[]
        {
            ("PhTerminalUnit", "Name"), ("PhNaturalClass", "Name"), ("PhEnvironment", "Name"),
            ("PhEnvironment", "StringRepresentation"), ("FsClosedValue", "Value"),
            ("PhSegmentRule", "Name"),
            ("MoInflAffixSlot", "Name"), ("MoInflAffixTemplate", "Name"),
        }).ToHashSet();
        var directionRows = RewriteRuleFieldSelector.SelectDirection(model.Rows);
        DescriptionCheck.CheckEmittedKinds(model.Rows.Where(r => selected.Contains((r.DeclaringClass, r.FieldName)))
                .Concat(directionRows).ToArray(),
            KindDescriptionTsvParser.Parse(RepoPaths.DefaultDescriptionsPath()));
        var written = new List<GeneratedCatalogWriter.WrittenFile>();
        var constants = new List<Slice3SnapshotFieldsEmitter.ConstantEntry>();
        foreach (var creation in Creations)
        {
            var row = model.Rows.Single(r => r.DeclaringClass == creation.Owner && r.FieldName == creation.Field);
            if (row.Manifest.Scope != "in" || row.Kind != FieldKind.Owning || row.Card != creation.Card ||
                row.Sig != creation.Sig || !row.Manifest.Verbs.Split('|').Contains("create"))
                throw new GeneratorException($"{creation.Owner}.{creation.Field}: creation policy no longer matches the model.");
            if (creation.SupportsMove && (creation.Card != FieldCard.Seq ||
                !row.Manifest.Verbs.Split('|').Contains("move")))
                throw new GeneratorException($"{creation.Owner}.{creation.Field}: move policy no longer matches the model.");
            var group = GroupDerivation.Derive(row.DeclaringClass);
            var segment = ConstructDerivation.Derive(row.DeclaringClass);
            var prefix = creation.Owner + creation.Field;
            var accessor = creation.Field + (creation.Card == FieldCard.Atomic ? "OA" :
                creation.Card == FieldCard.Col ? "OC" : "OS");
            var members = creation.Card == FieldCard.Atomic ?
                $"owner.{accessor} is {{ }} child ? new ICmObject[] {{ child }} : Array.Empty<ICmObject>()" :
                $"owner.{accessor}.Cast<ICmObject>().ToArray()";
            var createKind = KindNameDerivation.DeriveOne(group, segment, "create", creation.Field);
            var code = Template.Replace("__PREFIX__", prefix).Replace("__OWNER__", creation.Owner)
                .Replace("__KIND__", createKind)
                .Replace("__SIG__", creation.Sig).Replace("__MEMBERS__", members).Replace("__FACTORY__", creation.Factory.Replace("\n", "\n                        "))
                .Replace("__SEQ__", creation.Card == FieldCard.Seq ? "true" : "false")
                .Replace("__ATOMIC__", creation.Card == FieldCard.Atomic ? "true" : "false")
                .Replace("__POSITION_MATTERS__", creation.PositionMatters ? "true" : "false")
                .Replace("__CLASSES__", creation.Sig switch
                {
                    "PhNaturalClass" => "new[] { \"PhNCSegments\", \"PhNCFeatures\" }",
                    "PhSegmentRule" => "new[] { \"PhRegularRule\" }",
                    "PhContextOrVar" => "new[] { \"PhSimpleContextSeg\", \"PhSimpleContextNC\", \"PhSimpleContextBdry\", \"PhSequenceContext\" }",
                    "PhSimpleContext" => "new[] { \"PhSimpleContextSeg\", \"PhSimpleContextNC\", \"PhSimpleContextBdry\" }",
                    "PhPhonContext" => "new[] { \"PhSimpleContextSeg\", \"PhSimpleContextNC\", \"PhSimpleContextBdry\", \"PhSequenceContext\" }",
                    "StPara" => "new[] { \"StTxtPara\" }",
                    _ => "Array.Empty<string>()",
                });
            if (creation.SupportsMove)
                code = AddMoveSupport(code, prefix, creation.Owner, creation.Field,
                    KindNameDerivation.DeriveOne(group, segment, "move", creation.Field), createKind);
            Write($"src/SIL.Motif.Runner/Operations/Generated5/{prefix}.g.cs", code);
            constants.Add(new(creation.Owner, creation.Field, group, segment, prefix, $"owning {creation.Sig}"));
        }

        foreach (var (owner, field) in MoveOnlyFields)
        {
            var row = model.Rows.Single(r => r.DeclaringClass == owner && r.FieldName == field);
            if (row.Manifest.Scope != "in" || row.Kind != FieldKind.Owning || row.Card != FieldCard.Seq ||
                !row.Manifest.Verbs.Split('|').Contains("move"))
                throw new GeneratorException($"{owner}.{field}: move-only policy no longer matches the model.");

            var prefix = owner + field;
            var group = GroupDerivation.Derive(owner);
            var segment = ConstructDerivation.Derive(owner);
            var moveKind = KindNameDerivation.DeriveOne(group, segment, "move", field);
            var code = MoveOnlyTemplate.Replace("__PREFIX__", prefix)
                .Replace("__FIELD__", field)
                .Replace("__KIND__", moveKind);
            code = code.TrimEnd() + "\n\n" + RenderMoveHandler(prefix, owner, field).Trim() + "\n";
            Write($"src/SIL.Motif.Runner/Operations/Generated5/{prefix}.g.cs", code);
        }

        foreach (var (owner, field) in new[] { ("PhTerminalUnit", "Name"), ("PhNaturalClass", "Name"),
                     ("PhEnvironment", "Name"), ("PhSegmentRule", "Name"), ("MoInflAffixSlot", "Name"),
                     ("MoInflAffixTemplate", "Name") })
        {
            var row = model.Rows.Single(r => r.DeclaringClass == owner && r.FieldName == field);
            var spec = BasicFieldSpecBuilder.Build(row);
            Write($"src/SIL.Motif.Runner/Operations/Generated5/{owner}{field}.g.cs", AlternativesFieldEmitter.Render(spec).Replace(owner + "Snapshotter", owner + "AuthoringSnapshotter"));
            Write($"src/SIL.Motif.Runner/Snapshotting/Generated5/{owner}Snapshotter.g.cs",
                SnapshotterEmitter.Render(owner, new[] { spec }).Replace(owner + "Snapshotter", owner + "AuthoringSnapshotter"));
            constants.Add(new(owner, field, spec.Group, spec.Construct, spec.SnapshotFieldConstant, "basic MultiUnicode"));
        }
        foreach (var spec in IntegerEnumFieldSpecBuilder.BuildAll(directionRows)
                     .Select(spec => spec with { SnapshotterTypeName = "PhSegmentRuleDirectionSnapshotter" }))
        {
            Write("src/SIL.Motif.Runner/Operations/Generated5/PhSegmentRuleDirection.g.cs",
                IntegerEnumFieldEmitter.Render(spec));
            Write("src/SIL.Motif.Runner/Snapshotting/Generated5/PhSegmentRuleDirectionSnapshotter.g.cs",
                IntegerEnumSnapshotterEmitter.Render(spec.DeclaringClass, new[] { spec }, spec.SnapshotterTypeName));
            constants.Add(new(spec.DeclaringClass, spec.FieldName, spec.Group, spec.Construct,
                spec.SnapshotFieldConstant, $"basic {spec.Sig}"));
        }
        var environmentRow = model.Rows.Single(r => r.DeclaringClass == "PhEnvironment" && r.FieldName == "StringRepresentation");
        if (environmentRow.Sig != "String" || environmentRow.Manifest.Scope != "in")
            throw new GeneratorException("PhEnvironment.StringRepresentation no longer matches the string policy.");
        Write("src/SIL.Motif.Runner/Operations/Generated5/PhEnvironmentStringRepresentation.g.cs", EnvironmentStringEmitter.Render());
        constants.Add(new("PhEnvironment", "StringRepresentation", "grammar", "phEnvironment", "PhEnvironmentStringRepresentation", "basic String"));
        var valueRow = model.Rows.Single(r => r.DeclaringClass == "FsClosedValue" && r.FieldName == "Value");
        var valueSpec = RelationFieldSpecBuilder.BuildAtomic(valueRow);
        Write("src/SIL.Motif.Runner/Operations/Generated5/FsClosedValueValue.g.cs", ReferenceAtomicFieldEmitter.Render(valueSpec));
        Write("src/SIL.Motif.Runner/Snapshotting/Generated5/FsClosedValueRelationsSnapshotter.g.cs",
            RelationsSnapshotterEmitter.Render("FsClosedValue", "IFsClosedValue", "fsClosedValue", new[]
            {
                new RelationsSnapshotterEmitter.PopulationLine("FsClosedValue.Value (rel atomic)",
                    "AddIfPopulated(fields, SnapshotFields.FsClosedValueValue, ReferenceFieldSnapshotting.ReadAlternatives(fsClosedValue.ValueRA));"),
            }));
        constants.Add(new("FsClosedValue", "Value", valueSpec.Group, valueSpec.Construct, "FsClosedValueValue", "rel atomic"));
        Write("src/SIL.Motif.Model/Snapshot/SnapshotFields.Generated5.g.cs", Slice3SnapshotFieldsEmitter.Render(constants));
        return written;

        void Write(string relative, string content)
        {
            var path = Path.Combine(repoRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n"));
            written.Add(new(relative));
        }
    }

    private static string RenderMoveHandler(string prefix, string owner, string field) => $$"""

        /// <summary>Parses the member identity addressed by a move operation.</summary>
        public static class {{prefix}}MovePayload
        {
            private static readonly string[] AllowedProperties = { "member" };

            public static CanonicalId Parse(JsonElement after)
            {
                ClosedPayloadParsing.RequireObject(after, {{prefix}}OperationKinds.Move{{field}});
                ClosedPayloadParsing.RejectUnknownProperties(after, AllowedProperties, {{prefix}}OperationKinds.Move{{field}});
                return ClosedPayloadParsing.GetRequiredCanonicalId(after, "member", {{prefix}}OperationKinds.Move{{field}});
            }
        }

        /// <summary>Moves an existing member by identity inside its ordered owning sequence.</summary>
        public static class {{prefix}}MoveLowering
        {
            public static void Apply(I{{owner}} owner, CanonicalId memberId, Placement placement) =>
                ReferenceSequenceFieldLowering.ApplyMove(
                    owner.{{field}}OS, memberId, placement, {{prefix}}OperationKinds.Move{{field}});
        }

        internal sealed class {{prefix}}MoveHandler : IOperationHandler
        {
            internal static readonly {{prefix}}MoveHandler Instance = new();
            private {{prefix}}MoveHandler() { }

            public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation,
                List<CanonicalId> touchedTargets)
            {
                if (operation.Target is not { })
                    throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{ {{prefix}}OperationKinds.Move{{field}} }' requires 'target'.");
                if (operation.After is not { } after)
                    throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{ {{prefix}}OperationKinds.Move{{field}} }' requires 'after'.");
                if (operation.Placement is not { } placement)
                    throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{ {{prefix}}OperationKinds.Move{{field}} }' requires 'placement'.");

                var memberId = {{prefix}}MovePayload.Parse(after);
                var (id, owner) = TargetResolution.Resolve<I{{owner}}>(cache, operation,
                    {{prefix}}OperationKinds.Move{{field}});
                touchedTargets.Add(id);
                var before = SIL.Motif.Runner.Snapshotting.ReferenceSequenceFieldSnapshotting.ReadAlternatives(owner.{{field}}OS);
                {{prefix}}MoveLowering.Apply(owner, memberId, placement);
                var afterValue = SIL.Motif.Runner.Snapshotting.ReferenceSequenceFieldSnapshotting.ReadAlternatives(owner.{{field}}OS);
                return new ExpectedEffect(id, SnapshotFields.{{prefix}}, before, afterValue);
            }

            public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
            {
                var (id, owner) = TargetResolution.Resolve<I{{owner}}>(cache, operation,
                    {{prefix}}OperationKinds.Move{{field}});
                var current = SIL.Motif.Runner.Snapshotting.ReferenceSequenceFieldSnapshotting.ReadAlternatives(owner.{{field}}OS);
                return new ExpectedEffect(id, SnapshotFields.{{prefix}}, current, current);
            }
        }
        """;

    internal static string AddMoveSupport(string code, string prefix, string owner, string field,
        string moveKind, string createKind)
    {
        code = code.ReplaceLineEndings("\n");
        code = code.Replace("using System.Runtime.CompilerServices;\n",
            "using System.Runtime.CompilerServices;\nusing System.Text.Json;\n", StringComparison.Ordinal);
        code = code.Replace("/// <summary>Creates a member with an authored identity inside the caller's unit of work.</summary>",
            "/// <summary>Names creation and move operation kinds for this owned field.</summary>", StringComparison.Ordinal);
        code = code.Replace($"    public const string Create = \"{createKind}\";",
            $"    public const string Create = \"{createKind}\";\n    public const string Move{field} = \"{moveKind}\";",
            StringComparison.Ordinal);

        const string registrationEnd = "\n    }\n}";
        var registrationEndIndex = code.IndexOf(registrationEnd, StringComparison.Ordinal);
        if (registrationEndIndex < 0)
            throw new GeneratorException($"Could not locate the registration body for {owner}.{field}.");
        code = code.Insert(registrationEndIndex,
            $"\n        OperationKindRegistry.Register(Move{field});\n" +
            $"        OperationHandlerRegistry.Register(Move{field}, {prefix}MoveHandler.Instance);");
        return code.TrimEnd() + "\n\n" + RenderMoveHandler(prefix, owner, field).Trim() + "\n";
    }

    private const string Template = """
        // <auto-generated>Produced by SIL.Motif.Generator emit. Do not hand-edit.</auto-generated>
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Runtime.CompilerServices;
        using SIL.LCModel;
        using SIL.Motif.Contract.Ids;
        using SIL.Motif.Contract.Model;
        using SIL.Motif.Contract.Parsing;
        using SIL.Motif.Model.Effects;
        using SIL.Motif.Model.Snapshot;

        namespace SIL.Motif.Runner.Operations;

        /// <summary>Creates a member with an authored identity inside the caller's unit of work.</summary>
        public static class __PREFIX__OperationKinds
        {
            public const string Create = "__KIND__";

            [ModuleInitializer]
            internal static void Register()
            {
                OperationKindRegistry.Register(Create);
                OperationHandlerRegistry.Register(Create, new OwningCreateHandler<I__OWNER__>(
                    Create, SnapshotFields.__PREFIX__, owner => __MEMBERS__,
                    (cache, owner, guid, index, concreteClass) =>
                    {
                        __FACTORY__
                    }, __SEQ__, __ATOMIC__, __CLASSES__, "__SIG__", __POSITION_MATTERS__));
            }
        }

        """;

    private const string MoveOnlyTemplate = """
        // <auto-generated>Produced by SIL.Motif.Generator emit. Do not hand-edit.</auto-generated>
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Text.Json;
        using SIL.LCModel;
        using SIL.Motif.Contract.Ids;
        using SIL.Motif.Contract.Model;
        using SIL.Motif.Contract.Parsing;
        using SIL.Motif.Model.Effects;
        using SIL.Motif.Model.Snapshot;

        namespace SIL.Motif.Runner.Operations;

        /// <summary>Names the move operation kind for an ordered owning field.</summary>
        public static class __PREFIX__OperationKinds
        {
            public const string Move__FIELD__ = "__KIND__";

            [ModuleInitializer]
            internal static void Register()
            {
                OperationKindRegistry.Register(Move__FIELD__);
                OperationHandlerRegistry.Register(Move__FIELD__, __PREFIX__MoveHandler.Instance);
            }
        }
        """;
}
