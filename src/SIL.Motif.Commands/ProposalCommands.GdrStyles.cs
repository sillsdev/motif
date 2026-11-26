using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    // Stages the standard styles a body uses and the project lacks; other styles refuse in the body step.
    private static ComposeStep EnsureStandardStylesStep(
        Func<LcmCache, (IEnumerable<string?> Paragraph, IEnumerable<string?> Character)> used, List<string> created) =>
        new(null, "EnsureStandardStyles", (cache, _) =>
        {
            created.Clear();
            var (paragraph, character) = used(cache);
            var operations = new List<OperationEnvelope>();
            var staged = new HashSet<(string, StyleType)>();
            var languageProject = CanonicalId.FromGuid(cache.LangProject.Guid);
            foreach (var (names, type) in new[] { (paragraph, StyleType.kstParagraph), (character, StyleType.kstCharacter) })
                foreach (var name in names.OfType<string>().Where(name => name.Length > 0).Distinct(StringComparer.Ordinal))
                {
                    if (StandardStyles.Find(name, type) is not { } standard) continue;
                    foreach (var style in StandardStyles.WithDependencies(standard))
                    {
                        if (cache.LangProject.StylesOC.Any(held => held.Name == style.Name && held.Type == style.Type) ||
                            !staged.Add((style.Name, style.Type))) continue;
                        var after = JsonSerializer.SerializeToElement(new
                        {
                            name = style.Name, type = style.Type == StyleType.kstCharacter ? "character" : "paragraph",
                        });
                        operations.Add(new OperationEnvelope(CanonicalId.Mint(), StyleCreateOperationKinds.Create,
                            entityId: CanonicalId.FromGuid(style.Guid),
                            target: languageProject, after: after, rationale: $"Create the standard style '{style.Name}'."));
                        created.Add(style.Name);
                    }
                }
            return (JsonSerializer.SerializeToElement(new { styles = created.ToArray() }), operations);
        });

    // The styles a new section's body uses, or none when the body cannot be read.
    private static (IEnumerable<string?> Paragraph, IEnumerable<string?> Character) GdrBodyStyles(LcmCache cache, string section,
        string projectPath, string markdown)
    {
        var name = Path.GetFileNameWithoutExtension(projectPath);
        if (GdrReader.Show(cache, section, name) is not { } shown) return ([], []);
        var read = GdrMarkdownImport.Read(shown, name, markdown);
        if (read.HasErrors || read.Edits?.Paragraphs is not { } paragraphs) return ([], []);
        return (paragraphs.Changed.Select(change => change.Style), paragraphs.Changed.SelectMany(change => change.Runs).Select(run => run.CharacterStyle));
    }
}
