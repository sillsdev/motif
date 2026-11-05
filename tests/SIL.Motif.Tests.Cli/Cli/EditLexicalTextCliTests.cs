using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class EditLexicalTextCliTests(PristineProjectFixture pristine)
{
    [Fact]
    public void LexicalTextVerbsStageThroughCliAndKeepTheSavedProjectUnchanged()
    {
        var project = pristine.CopyProjectFile();
        var bytes = File.ReadAllBytes(project);
        var root = Path.Combine(Path.GetTempPath(), "motif-lexical-text-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var started = CliInProcess.Run(root, null, true, "new", "--project", project, "--draft", "text");
            Assert.Equal(0, started.ExitCode);
            foreach (var (verb, target, field, expected) in new[]
            {
                ("compose-edit-definition", pristine.Seed.FirstSenseId, (string?)null, ""),
                ("compose-edit-gloss", pristine.Seed.FirstSenseId, null, SeededProject.FirstGloss),
                ("compose-edit-lexical-note", pristine.Seed.FirstEntryId, "note", ""),
            })
            {
                var intent = new Dictionary<string, object>
                {
                    ["target"] = CanonicalId.FromGuid(target).Value,
                    ["alternatives"] = new[] { new { writingSystem = "en", expectedText = expected, text = "authored text" } },
                };
                if (field is not null) intent["field"] = field;
                var staged = CliInProcess.Run(root, null, true, verb, "--project", project, "--draft", "text",
                    "--intent", JsonSerializer.Serialize(intent), "--json");
                Assert.True(staged.ExitCode == 0, staged.Error + staged.Output);
            }
            Assert.Equal(bytes, File.ReadAllBytes(project));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public void DefinitionGuardsKeepLookupTextLiteralThroughCli()
    {
        var project = pristine.CopyProjectFile();
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(project))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(pristine.Seed.FirstSenseId)
                    .Definition.set_String(cache.DefaultAnalWs, "lookup:not-a-reference"));
            loader.Save(cache);
        }
        var root = Path.Combine(Path.GetTempPath(), "motif-literal-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var (draft, replacement) in new[] { ("edit", "changed"), ("clear", "") })
            {
                Assert.Equal(0, CliInProcess.Run(root, null, true, "new", "--project", project, "--draft", draft).ExitCode);
                var intent = new { target = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value,
                    alternatives = new[] { new { writingSystem = "en", expectedText = "lookup:not-a-reference", text = replacement } } };
                var result = CliInProcess.Run(root, null, true, "compose-edit-definition", "--project", project,
                    "--draft", draft, "--intent", JsonSerializer.Serialize(intent), "--json");
                Assert.True(result.ExitCode == 0, result.Error + result.Output);
                Assert.Contains(replacement == "" ? "clearDefinition" : "setDefinition", result.Output, StringComparison.Ordinal);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

}
