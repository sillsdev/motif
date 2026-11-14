namespace SIL.Motif.Host.Parsimony;

/// <summary>One entry's lexeme form for near-duplicate bucketing.</summary>
internal sealed record LexemeFormFact(string EntryGuid, string AllomorphGuid, string Form);

/// <summary>Every entry that has a lexeme form, and how many entries the project has.</summary>
internal sealed record LexemeFormInput(long EntryCount, IReadOnlyList<LexemeFormFact> Forms);

/// <summary>One variant link, with the two forms and the variant's phone environments read through indexes.</summary>
internal sealed record VariantShapeFact(string VariantEntryGuid, string VariantAllomorphGuid, string TypeGuid,
    string MainForm, string VariantForm, IReadOnlyList<string> EnvironmentGuids);

/// <summary>The variant links of a project and how many entries it has.</summary>
internal sealed record VariantShapeInput(long EntryCount, IReadOnlyList<VariantShapeFact> Variants);

/// <summary>
/// A tag required by a rule or an affix, against the stems in the category scope it reads: one row per tag and
/// requirer.
/// </summary>
/// <param name="TagGuid">The exception feature.</param>
/// <param name="RequirerKind">"rule" or "msa".</param>
/// <param name="RequirerGuid">The rule or affix MSA.</param>
/// <param name="Polarity">"required" for a rule that requires the tag, "excluded" for one that excludes it.</param>
/// <param name="Hosts">Stem MSAs in the requirer's category scope.</param>
/// <param name="Carriers">Of those, the stem MSAs carrying the tag.</param>
internal sealed record TagPolarityRow(string TagGuid, string RequirerKind, string RequirerGuid, string Polarity,
    long Hosts, long Carriers);

/// <summary>The tag and scope counts of a project.</summary>
internal sealed record TagPolarityInput(long StemCount, IReadOnlyList<TagPolarityRow> Rows);
