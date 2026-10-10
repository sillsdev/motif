using SIL.Motif.Generator;
using SIL.Motif.Generator.Emit;
using Xunit;

namespace SIL.Motif.Tests.Generator;

public sealed class OwningCreateGeneratedFilesTests
{
    [Fact]
    public void AddMoveSupportMatchesRegistrationBodyWithWindowsLineEndings()
    {
        var source = """
            using System.Runtime.CompilerServices;
            /// <summary>Creates a member with an authored identity inside the caller's unit of work.</summary>
            public static class GeneratedOperations
            {
                public const string Create = "create-kind";
                public static void Register()
                {
                }
            }
            """.ReplaceLineEndings("\r\n");

        var generated = OwningCreateCatalogWriter.AddMoveSupport(source, "OwnerField", "Owner", "Field",
            "move-kind", "create-kind");

        Assert.Contains("using System.Text.Json;", generated);
        Assert.Contains("OperationKindRegistry.Register(MoveField);", generated);
    }

    [Fact]
    public void WriteAll_EmitsDisabledRegularRuleCreationAndClosedDirectionOperations()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "motif-rewrite-rule-emission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = OwningCreateCatalogWriter.WriteAll(MotifModelLoader.Load(), temporary);

            Assert.Contains(written, file => file.RelativePath.EndsWith("Operations/Generated5/PhPhonDataPhonRules.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith("Operations/Generated5/PhRegularRuleRightHandSides.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith("Operations/Generated5/PhSegmentRuleName.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith("Operations/Generated5/PhSegmentRuleDirection.g.cs", StringComparison.Ordinal));

            var ruleCreate = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner/Operations/Generated5/PhPhonDataPhonRules.g.cs"));
            Assert.Contains("IPhRegularRuleFactory", ruleCreate);
            Assert.Contains("child.Disabled = true", ruleCreate);
            Assert.Contains("owner.PhonRulesOS.Insert(index, child)", ruleCreate);
            Assert.Contains("new[] { \"PhRegularRule\" }", ruleCreate);
            Assert.Contains("grammar/phPhonData/movePhonRules", ruleCreate);
            Assert.Contains("ReferenceSequenceFieldLowering.ApplyMove", ruleCreate);
            Assert.Contains("PhPhonDataPhonRulesMoveHandler.Instance", ruleCreate);

            var rhsCreate = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner/Operations/Generated5/PhRegularRuleRightHandSides.g.cs"));
            Assert.Contains("IPhSegRuleRHSFactory", rhsCreate);
            Assert.Contains("owner.RightHandSidesOS.Insert(index, child)", rhsCreate);

            var direction = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner/Operations/Generated5/PhSegmentRuleDirection.g.cs"));
            Assert.Contains("grammar/phSegmentRule/setDirection", direction);
            Assert.Contains("AllowedValues = { 0, 1, 2 }", direction);

            var name = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner/Operations/Generated5/PhSegmentRuleName.g.cs"));
            Assert.Contains("grammar/phSegmentRule/setName", name);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    [Fact]
    public void WriteAll_EmitsTypedRewriteContextCreationOperations()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "motif-rewrite-context-emission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = OwningCreateCatalogWriter.WriteAll(MotifModelLoader.Load(), temporary);

            Assert.Contains(written, file => file.RelativePath.EndsWith("Operations/Generated5/PhPhonDataContexts.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith("Operations/Generated5/PhSegmentRuleStrucDesc.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith("Operations/Generated5/PhSegRuleRHSStrucChange.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith("Operations/Generated5/PhSegRuleRHSLeftContext.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith("Operations/Generated5/PhSegRuleRHSRightContext.g.cs", StringComparison.Ordinal));

            var contexts = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner/Operations/Generated5/PhPhonDataContexts.g.cs"));
            Assert.Contains("PhSimpleContextSeg", contexts);
            Assert.Contains("PhSimpleContextNC", contexts);
            Assert.Contains("PhSimpleContextBdry", contexts);
            Assert.Contains("PhSequenceContext", contexts);
            Assert.Contains("\"PhContextOrVar\", false", contexts);
            Assert.DoesNotContain("PhIterationContext", contexts);
            Assert.DoesNotContain("PhVariable", contexts);
            Assert.DoesNotContain("PhEnvironment", contexts);

            foreach (var fileName in new[] { "PhSegmentRuleStrucDesc.g.cs", "PhSegRuleRHSStrucChange.g.cs" })
            {
                var source = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner/Operations/Generated5", fileName));
                Assert.Contains("PhSimpleContextSeg", source);
                Assert.Contains("PhSimpleContextNC", source);
                Assert.Contains("PhSimpleContextBdry", source);
                Assert.DoesNotContain("PhSequenceContext", source);
                Assert.DoesNotContain("PhIterationContext", source);
            }

            foreach (var fileName in new[] { "PhSegRuleRHSLeftContext.g.cs", "PhSegRuleRHSRightContext.g.cs" })
            {
                var source = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner/Operations/Generated5", fileName));
                Assert.Contains("PhSimpleContextSeg", source);
                Assert.Contains("PhSimpleContextNC", source);
                Assert.Contains("PhSimpleContextBdry", source);
                Assert.Contains("PhSequenceContext", source);
                Assert.DoesNotContain("PhIterationContext", source);
            }
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    [Fact]
    public void WriteAll_EmitsCategoryOwnedAffixSlotCreationAndNameSurface()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "motif-affix-slot-emission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = OwningCreateCatalogWriter.WriteAll(MotifModelLoader.Load(), temporary);

            Assert.Contains(written, file => file.RelativePath.EndsWith(
                "Operations/Generated5/PartOfSpeechAffixSlots.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith(
                "Operations/Generated5/MoInflAffixSlotName.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith(
                "Snapshotting/Generated5/MoInflAffixSlotSnapshotter.g.cs", StringComparison.Ordinal));

            var create = File.ReadAllText(Path.Combine(temporary,
                "src/SIL.Motif.Runner/Operations/Generated5/PartOfSpeechAffixSlots.g.cs"));
            Assert.Contains("grammar/partOfSpeech/createAffixSlots", create);
            Assert.Contains("IMoInflAffixSlotFactory", create);
            Assert.Contains("owner.AffixSlotsOC.Add(child)", create);

            var name = File.ReadAllText(Path.Combine(temporary,
                "src/SIL.Motif.Runner/Operations/Generated5/MoInflAffixSlotName.g.cs"));
            Assert.Contains("grammar/moInflAffixSlot/setName", name);
            Assert.Contains("MultiAlternativesFieldLowering.ApplySet", name);

            var snapshot = File.ReadAllText(Path.Combine(temporary,
                "src/SIL.Motif.Runner/Snapshotting/Generated5/MoInflAffixSlotSnapshotter.g.cs"));
            Assert.Contains("class MoInflAffixSlotAuthoringSnapshotter", snapshot);
            Assert.Contains("SnapshotFields.MoInflAffixSlotName", snapshot);
            Assert.Contains("MultiAlternativesFieldSnapshotting.ReadAlternatives", snapshot);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    [Fact]
    public void WriteAll_EmitsCategoryOwnedAffixTemplateCreationAndNameSurface()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "motif-affix-template-emission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = OwningCreateCatalogWriter.WriteAll(MotifModelLoader.Load(), temporary);

            Assert.Contains(written, file => file.RelativePath.EndsWith(
                "Operations/Generated5/PartOfSpeechAffixTemplates.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith(
                "Operations/Generated5/MoInflAffixTemplateName.g.cs", StringComparison.Ordinal));
            Assert.Contains(written, file => file.RelativePath.EndsWith(
                "Snapshotting/Generated5/MoInflAffixTemplateSnapshotter.g.cs", StringComparison.Ordinal));

            var create = File.ReadAllText(Path.Combine(temporary,
                "src/SIL.Motif.Runner/Operations/Generated5/PartOfSpeechAffixTemplates.g.cs"));
            Assert.Contains("grammar/partOfSpeech/createAffixTemplates", create);
            Assert.Contains("IMoInflAffixTemplateFactory", create);
            Assert.Contains("owner.AffixTemplatesOS.Insert(index, child)", create);
            Assert.Contains("grammar/partOfSpeech/moveAffixTemplates", create);

            var name = File.ReadAllText(Path.Combine(temporary,
                "src/SIL.Motif.Runner/Operations/Generated5/MoInflAffixTemplateName.g.cs"));
            Assert.Contains("grammar/moInflAffixTemplate/setName", name);
            Assert.Contains("MultiAlternativesFieldLowering.ApplySet", name);

            var snapshot = File.ReadAllText(Path.Combine(temporary,
                "src/SIL.Motif.Runner/Snapshotting/Generated5/MoInflAffixTemplateSnapshotter.g.cs"));
            Assert.Contains("class MoInflAffixTemplateAuthoringSnapshotter", snapshot);
            Assert.Contains("SnapshotFields.MoInflAffixTemplateName", snapshot);
            Assert.Contains("MultiAlternativesFieldSnapshotting.ReadAlternatives", snapshot);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    [Fact]
    public void WriteAll_EmitsMoveOnlyOperationForAlternateForms()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "motif-allomorph-order-emission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = OwningCreateCatalogWriter.WriteAll(MotifModelLoader.Load(), temporary);
            var relativePath = "Operations/Generated5/LexEntryAlternateForms.g.cs";

            Assert.Contains(written, file => file.RelativePath.EndsWith(relativePath, StringComparison.Ordinal));
            var source = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner", relativePath));
            Assert.Contains("lexical/lexEntry/moveAlternateForms", source);
            Assert.Contains("owner.AlternateFormsOS", source);
            Assert.Contains("SnapshotFields.LexEntryAlternateForms", source);
            Assert.DoesNotContain("public const string Create", source);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    [Fact]
    public void EveryOwningCreateFileMatchesTheCheckedManifestAndGenerator()
    {
        var root = RepoPaths.FindRepoRoot();
        var temporary = Path.Combine(Path.GetTempPath(), "motif-owning-drift-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = OwningCreateCatalogWriter.WriteAll(MotifModelLoader.Load(), temporary);
            Assert.NotEmpty(written);
            foreach (var file in written)
                Assert.Equal(File.ReadAllText(Path.Combine(root, file.RelativePath)).ReplaceLineEndings("\n"),
                    File.ReadAllText(Path.Combine(temporary, file.RelativePath)).ReplaceLineEndings("\n"));
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
}
