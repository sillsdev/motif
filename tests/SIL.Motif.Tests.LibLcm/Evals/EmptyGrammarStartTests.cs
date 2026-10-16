using System.Xml.Linq;
using SIL.Motif.SampleProjects;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Evals;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class EmptyGrammarStartTests
{
    [EvalSetFact]
    public void EveryLanguageBuildsAnEmptyGrammarWithItsStemsAndNoGrammarObjects()
    {
        foreach (var setId in new[] { "eval-t0-qaxu", "eval-t1-vexu", "eval-t2-lomi", "eval-t3-panu" })
        {
            var root = Path.Combine(Path.GetTempPath(), "motif-empty-grammar-" + Guid.NewGuid().ToString("N"));
            try
            {
                var language = File.ReadAllText(Path.Combine(TestGrammars.SetsRoot!, setId, "language", "language.yaml"));
                var build = EvaluationProjectBuilder.Build(language, root, "empty-grammar");

                var classes = XDocument.Load(build.ProjectPath).Root!.Elements("rt")
                    .GroupBy(element => (string?)element.Attribute("class") ?? "")
                    .ToDictionary(group => group.Key, group => group.Count());
                Assert.True(classes.GetValueOrDefault("LexEntry") == 60, $"{setId} has {classes.GetValueOrDefault("LexEntry")} entries.");
                foreach (var grammarClass in new[] { "MoInflClass", "FsFeatStruc", "FsClosedFeature", "MoInflAffixSlot",
                             "MoInflAffixTemplate", "MoInflAffMsa", "PhEnvironment", "PhRegularRule" })
                    Assert.True(!classes.ContainsKey(grammarClass), $"{setId} empty grammar holds {grammarClass}.");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
