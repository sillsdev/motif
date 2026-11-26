namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A forms-explain request names no morpheme or word, names both, or is over a cap.</summary>
    public const string FormsExplainRequestInvalid = "forms-explain.request-invalid";
    /// <summary>The installed PanGloss does not declare a traced <c>generate</c> serve op, so it cannot generate forms.</summary>
    public const string FormsExplainParserUnsupported = "forms-explain.parser-unsupported";
    /// <summary>A morpheme named in a forms-explain request matches nothing in the project's lexicon.</summary>
    public const string FormsExplainMorphemeUnresolved = "forms-explain.morpheme-unresolved";
    /// <summary>A morpheme named in a forms-explain request matches more than one entry or analysis.</summary>
    public const string FormsExplainMorphemeAmbiguous = "forms-explain.morpheme-ambiguous";
    /// <summary>PanGloss rejected the generate request, such as an unknown entry, and said why.</summary>
    public const string FormsExplainParserRefused = "forms-explain.parser-refused";
    /// <summary>PanGloss wrote a generate answer Motif cannot read.</summary>
    public const string FormsExplainMalformed = "forms-explain.malformed";
    /// <summary>The resident PanGloss failed or timed out before it answered the generate request.</summary>
    public const string FormsExplainUnfinished = "forms-explain.unfinished";
    /// <summary>No analysis of the word could be explained: it has none, or every one has a morph the lexicon cannot name.</summary>
    public const string FormsExplainNoReading = "forms-explain.no-reading";
}
