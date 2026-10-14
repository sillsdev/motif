namespace SIL.Motif.Contract.Requests;

/// <summary>The closed set of project objects available to the grammar read command.</summary>
public enum GrammarReadKind
{
    /// <summary>Phonemes and their written representations.</summary>
    phoneme,

    /// <summary>Natural classes and the phonemes or features that define them.</summary>
    natural_class,

    /// <summary>Phonological environments and their stored notation.</summary>
    environment,

    /// <summary>Parts of speech and their parent category.</summary>
    part_of_speech,

    /// <summary>Inflection features and their declared values.</summary>
    inflection_feature,

    /// <summary>Lexical entries, senses, forms and grammatical information.</summary>
    lexical_entry,
}

/// <summary>Reads a page of one authored object kind from a project's current Baseline.</summary>
/// <param name="ProjectPath">The path to the FieldWorks project.</param>
/// <param name="Kind">The one project object kind to read.</param>
/// <param name="Id">An exact canonical id, when one object is requested.</param>
/// <param name="Query">Text to match against names, forms, representations or sense glosses.</param>
/// <param name="Offset">The number of matches to skip.</param>
/// <param name="Limit">The maximum number of matches to return.</param>
public sealed record GrammarReadRequest(
    string ProjectPath,
    GrammarReadKind Kind,
    string? Id = null,
    string? Query = null,
    int Offset = 0,
    int Limit = 25);
