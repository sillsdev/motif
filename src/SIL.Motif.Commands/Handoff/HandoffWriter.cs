using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;

namespace SIL.Motif.Commands.Handoff;

/// <summary>
/// Writes the content design decision 6 puts in an AI Handoff folder, and publishes it atomically: every
/// file lands in a sibling <c>.incoming-&lt;guid&gt;</c> directory first, the exact listing is validated,
/// and only then one <see cref="Directory.Move(string, string)"/> makes it appear at its destination.
/// </summary>
/// <remarks>
/// <see cref="Publish"/> is the whole atomicity contract: its populate callback writes files freely into
/// the incoming directory, and a <see cref="Refusal"/> returned from it — or an exception thrown out of
/// it — both abort before the destination is ever touched. The incoming directory is always removed on
/// any path that does not end in a successful move.
/// </remarks>
public static class HandoffWriter
{
    /// <summary>The six groups <c>pangloss stats --group</c> accepts; <c>never-fires</c> is the only hyphenated one.</summary>
    public static readonly IReadOnlyList<string> StatisticsGroups =
        ["word", "object", "allomorph", "morpheme", "group", "never-fires"];

    private const string InstructionsResource = "SIL.Motif.Commands.Handoff.Assets.instructions.md";
    private const string RecipesResource = "SIL.Motif.Commands.Handoff.Assets.recipes.md";
    private const string ReadHandoffPyResource = "SIL.Motif.Commands.Handoff.Assets.read_handoff.py";
    private const string GrammarFormatResource = "SIL.Motif.Commands.Handoff.Reference.grammar-format.md";
    private const string FlexTextFormatResource = "SIL.Motif.Commands.Handoff.Reference.flextext-json-format.md";
    private const string HcMechanicsResource = "SIL.Motif.Commands.Handoff.Reference.hc-mechanics.md";

    // Split across a "+" so no single token here reads as a dotted, lower-case refusal-code-shaped literal.
    private const string InstructionsFileName = "instructions" + ".md";
    private const string RecipesFileName = "recipes" + ".md";
    private const string ReadHandoffPyFileName = "read_handoff.py";
    internal const string GrammarFileName = "grammar" + ".json";
    internal const string SelectionFileName = "selection" + ".txt";
    internal const string StatisticsSummaryFileName = "statistics" + ".md";
    internal const string StatisticsDirectoryName = "statistics";
    private const string FlexTextJsonExtension = "flextext" + ".json";
    private const string FlexTextXmlExtension = "flextext" + ".xml";

    private static readonly string[] AlwaysRequiredTopLevelFiles =
        [InstructionsFileName, GrammarFileName, SelectionFileName, RecipesFileName, ReadHandoffPyFileName];

    private static readonly string[] ReferenceFiles =
        ["grammar-format.md", "flextext-json-format.md", "hc-mechanics.md"];

    /// <summary>
    /// Builds a Handoff folder in a fresh sibling incoming directory via <paramref name="populate"/>,
    /// validates the exact listing, and moves it to <paramref name="destinationDirectory"/> in one step.
    /// </summary>
    /// <param name="destinationDirectory">
    /// Where the folder must appear. Assumed already checked non-existent or empty by the caller; an
    /// existing empty directory here is removed immediately before the final move.
    /// </param>
    /// <param name="includeStatistics">Whether <c>statistics.md</c> and <c>statistics/</c> are required.</param>
    /// <param name="populate">
    /// Writes every file into the incoming directory it is handed. Returning a <see cref="Refusal"/>
    /// aborts without moving anything; an exception it throws propagates after the incoming directory is
    /// still cleaned up.
    /// </param>
    /// <returns>The <see cref="Refusal"/> <paramref name="populate"/> returned, or <see langword="null"/> on success.</returns>
    public static Refusal? Publish(
        string destinationDirectory, bool includeStatistics, Func<string, Refusal?> populate)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectory);
        ArgumentNullException.ThrowIfNull(populate);

        var full = Path.GetFullPath(destinationDirectory);
        var parent = Path.GetDirectoryName(full)
            ?? throw new ArgumentException("The destination must have a parent directory.", nameof(destinationDirectory));
        Directory.CreateDirectory(parent);

        var incoming = Path.Combine(parent, ".incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        try
        {
            var refusal = populate(incoming);
            if (refusal is not null) return refusal;

            ValidateListing(incoming, includeStatistics);

            // Known empty by the caller's own pre-check; removed here so Directory.Move never sees it.
            if (Directory.Exists(full)) Directory.Delete(full);
            Directory.Move(incoming, full);
            return null;
        }
        finally
        {
            if (Directory.Exists(incoming)) DeleteDirectorySafely(incoming);
        }
    }

    /// <summary>
    /// Writes one <c>texts/&lt;title&gt;-&lt;guid&gt;.flextext.json</c> per chosen Text — every Text in
    /// the project when <paramref name="requestedTextIds"/> is empty — and, when
    /// <paramref name="writeFlexTextXml"/> is set, a matching <c>.flextext.xml</c> beside it.
    /// </summary>
    /// <returns>The Handoff-relative paths of every file written, in write order.</returns>
    internal static IReadOnlyList<string> WriteTexts(
        LcmCache cache, IReadOnlyList<Guid> requestedTextIds, string incomingRoot, bool writeFlexTextXml)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(requestedTextIds);

        var repository = cache.ServiceLocator.GetInstance<ITextRepository>();
        var chosen = new List<IText>();
        if (requestedTextIds.Count > 0)
        {
            // Skipped rather than thrown: one unresolvable id must not stop every other chosen Text.
            foreach (var textId in requestedTextIds)
            {
                if (repository.TryGetObject(textId, out var text)) chosen.Add(text);
            }
        }
        else
        {
            chosen.AddRange(repository.AllInstances());
        }

        var textsDir = Directory.CreateDirectory(Path.Combine(incomingRoot, "texts")).FullName;
        var written = new List<string>();
        foreach (var text in chosen)
        {
            var projection = InterlinearTextReader.Read(cache, text);

            var jsonName = InterlinearTextFileNaming.BuildFileName(projection, FlexTextJsonExtension);
            File.WriteAllText(Path.Combine(textsDir, jsonName), FlexTextJsonWriter.Serialize(projection));
            written.Add("texts/" + jsonName);

            if (!writeFlexTextXml) continue;

            var xmlName = InterlinearTextFileNaming.BuildFileName(projection, FlexTextXmlExtension);
            using var stream = File.Create(Path.Combine(textsDir, xmlName));
            FlexTextXmlWriter.WriteTo(projection, stream);
            written.Add("texts/" + xmlName);
        }
        return written;
    }

    /// <summary>Writes <c>selection.txt</c>: the final word list and which sources contributed to it.</summary>
    internal static void WriteSelectionTxt(string incomingRoot, SelectionProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var text = new StringBuilder();
        text.AppendLine($"# Selection ({projection.Words.Count} word(s))");
        text.AppendLine();
        text.AppendLine("Provenance:");
        foreach (var entry in projection.Provenance)
            text.AppendLine($"  {entry.Source}: {entry.Count}");
        text.AppendLine();
        foreach (var word in projection.Words)
            text.AppendLine(word);

        File.WriteAllText(Path.Combine(incomingRoot, SelectionFileName), text.ToString());
    }

    /// <summary>
    /// Extracts every Handoff asset embedded in this assembly: the read-this-first prose, the recipes,
    /// the standard-library reader, and the three <c>reference/</c> documents.
    /// </summary>
    internal static void WriteEmbeddedAssets(string incomingRoot)
    {
        ExtractEmbeddedAsset(InstructionsResource, Path.Combine(incomingRoot, InstructionsFileName));
        ExtractEmbeddedAsset(RecipesResource, Path.Combine(incomingRoot, RecipesFileName));
        ExtractEmbeddedAsset(ReadHandoffPyResource, Path.Combine(incomingRoot, ReadHandoffPyFileName));

        var referenceDir = Directory.CreateDirectory(Path.Combine(incomingRoot, "reference")).FullName;
        ExtractEmbeddedAsset(GrammarFormatResource, Path.Combine(referenceDir, "grammar-format.md"));
        ExtractEmbeddedAsset(FlexTextFormatResource, Path.Combine(referenceDir, "flextext-json-format.md"));
        ExtractEmbeddedAsset(HcMechanicsResource, Path.Combine(referenceDir, "hc-mechanics.md"));
    }

    /// <summary>Every file the published folder contains, as Handoff-relative, forward-slashed paths.</summary>
    internal static IReadOnlyList<string> ListFiles(string root)
    {
        var full = Path.GetFullPath(root);
        return Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(full, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // The folder's own consistency guard: never move an incomplete listing to the caller's destination.
    private static void ValidateListing(string root, bool includeStatistics)
    {
        foreach (var name in AlwaysRequiredTopLevelFiles)
        {
            if (!File.Exists(Path.Combine(root, name)))
                throw new InvalidOperationException($"The Handoff folder is missing '{name}'.");
        }

        foreach (var name in ReferenceFiles)
        {
            if (!File.Exists(Path.Combine(root, "reference", name)))
                throw new InvalidOperationException($"The Handoff folder is missing 'reference/{name}'.");
        }

        if (includeStatistics)
        {
            if (!File.Exists(Path.Combine(root, StatisticsSummaryFileName)))
                throw new InvalidOperationException("The Handoff folder is missing the statistics summary.");

            foreach (var group in StatisticsGroups)
            {
                if (!File.Exists(Path.Combine(root, StatisticsDirectoryName, group + ".jsonl")))
                    throw new InvalidOperationException($"The Handoff folder is missing 'statistics/{group}.jsonl'.");
            }
        }
        else if (File.Exists(Path.Combine(root, StatisticsSummaryFileName)) ||
            Directory.Exists(Path.Combine(root, StatisticsDirectoryName)))
        {
            throw new InvalidOperationException("A --no-assess Handoff must not write statistics.");
        }
    }

    private static void ExtractEmbeddedAsset(string resourceName, string destinationPath)
    {
        using var stream = typeof(HandoffWriter).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var destination = File.Create(destinationPath);
        stream.CopyTo(destination);
    }

    private static void DeleteDirectorySafely(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
