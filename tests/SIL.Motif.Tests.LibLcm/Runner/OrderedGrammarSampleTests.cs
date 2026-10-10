using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Trait("MotifTestLevel", "System")]
public sealed class OrderedGrammarSampleTests
{
    [OrderedGrammarSampleFact]
    public void PrefixSlotMoveDryRunsAppliesSavesAndReopensInTheDeclaredOrder()
    {
        var sourceRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")!);
        var root = Path.Combine(Path.GetTempPath(), "motif-ordered-grammar-sample-" + Guid.NewGuid().ToString("N"));
        var copiedProjectRoot = Path.Combine(root, "project-copy");
        Directory.CreateDirectory(copiedProjectRoot);
        CopyTree(Path.Combine(sourceRoot, "WritingSystemStore"), Path.Combine(copiedProjectRoot, "WritingSystemStore"));
        var copiedProject = Path.Combine(copiedProjectRoot, "mbugwe.fwdata");
        File.Copy(Path.Combine(sourceRoot, "mbugwe.fwdata"), copiedProject);
        var loader = new FwDataProjectLoader();

        try
        {
            Guid templateGuid;
            Guid[] expectedOrder;
            using (var cache = loader.LoadCache(copiedProject))
            {
                var template = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
                    .OfType<IMoInflAffixTemplate>()
                    .First(item => item.PrefixSlotsRS.Count >= 3);
                var originalOrder = template.PrefixSlotsRS.Select(slot => slot.Guid).ToArray();
                templateGuid = template.Guid;
                expectedOrder = [originalOrder[^1], .. originalOrder[..^1]];
                var proposal = new Proposal(
                    new Dictionary<string, string> { ["grammar"] = "1.0" },
                    CanonicalId.Mint(), null,
                    [new OperationEnvelope(
                        CanonicalId.Mint(),
                        MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots,
                        target: CanonicalId.FromGuid(template.Guid),
                        after: System.Text.Json.JsonSerializer.SerializeToElement(
                            new { member = CanonicalId.FromGuid(originalOrder[^1]).Value }),
                        placement: new Placement(null, CanonicalId.FromGuid(originalOrder[0]))) ]);

                var dryRun = ScratchDryRun.Of(cache, proposal);
                var expectedEffect = Assert.Single(dryRun.ExpectedEffects);
                Assert.Equal(expectedOrder.Select(guid => CanonicalId.FromGuid(guid).Value),
                    expectedEffect.After.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value));

                var receipt = ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "motif-sample-check");
                Assert.False(receipt.AlreadyApplied);
                Assert.Equal(expectedOrder, template.PrefixSlotsRS.Select(slot => slot.Guid));
                loader.Save(cache);
            }

            using var reopened = loader.LoadCache(copiedProject);
            var reopenedTemplate = (IMoInflAffixTemplate)reopened.ServiceLocator
                .GetInstance<ICmObjectRepository>().GetObject(templateGuid);
            Assert.Equal(expectedOrder, reopenedTemplate.PrefixSlotsRS.Select(slot => slot.Guid));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}

public sealed class OrderedGrammarSampleFactAttribute : FactAttribute
{
    public OrderedGrammarSampleFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")))
            Skip = "Set MOTIF_PANGLOSS_SAMPLES to PanGloss samples/data for the disposable grammar sequence check.";
    }
}
