using System.Text.Json;
using System.Xml.Linq;
using SIL.Motif.Contract.Ids;
using SIL.Motif.EvalSets;
using SIL.Motif.SampleProjects;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Evals;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class OutputMeasurementTests
{
    private const string Start = "gold-minus:plural-number-feature-missing";

    [RealEvalSetParserFact]
    public async Task AProposalTargetingAnAffixMsaMeasuresAgainstTheStartCopyTheAgentWorkedOn()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-measure-" + Guid.NewGuid().ToString("N"));
        try
        {
            var setRoot = Path.Combine(TestGrammars.SetsRoot!, "eval-t0-qaxu");
            var language = File.ReadAllText(Path.Combine(setRoot, "language", "language.yaml"));
            var agentStart = EvaluationProjectBuilder.Build(language, Path.Combine(root, "agent"), Start);
            var msa = XDocument.Load(agentStart.ProjectPath).Root!.Elements("rt")
                .First(element => (string?)element.Attribute("class") == "MoInflAffMsa" && element.Element("InflFeats") is null);
            var target = CanonicalId.FromGuid(Guid.Parse((string)msa.Attribute("guid")!)).Value;
            var proposalPath = Path.Combine(root, "intent.json");
            await File.WriteAllTextAsync(proposalPath, JsonSerializer.Serialize(new
            {
                contractVersions = new { grammar = "1.0" },
                proposalId = CanonicalId.Mint().Value,
                requires = Array.Empty<string>(),
                operations = new[]
                {
                    new
                    {
                        operationId = CanonicalId.Mint().Value, kind = "grammar/moInflAffMsa/createInflFeats",
                        entityId = CanonicalId.Mint().Value, target, dependsOn = Array.Empty<string>(),
                        after = new { },
                    },
                },
            }));

            var measured = await OutputMeasurement.RunAsync(Path.GetDirectoryName(agentStart.ProjectPath)!,
                proposalPath, Path.Combine(root, "measure"), setRoot);

            var measuredJson = JsonSerializer.SerializeToElement(measured);
            var failure = measuredJson.GetProperty("proposalFailure");
            Assert.True(failure.ValueKind == JsonValueKind.Null, failure.ToString());
            var parsed = measuredJson.GetProperty("parserRows").GetRawText();
            var trainOnly = File.ReadAllLines(Path.Combine(setRoot, "words", "train.txt")).Select(word => word.Trim())
                .Where(word => word.Length > 0).First();
            Assert.DoesNotContain("\"" + trainOnly + "\"", parsed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
