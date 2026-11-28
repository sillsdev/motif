namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string QueryInvalid = "query.invalid";
    /// <summary>The requested grouping exceeds the bounded result size.</summary>
    public const string QueryTooManyGroups = "query.too-many-groups";
    /// <summary>The query asks about a value the Trial facts did not measure.</summary>
    public const string QueryNotMeasured = "query.not-measured";
    /// <summary>A scorer was asked to count in a unit its hits or exceptions are not in.</summary>
    public const string QueryMixedUnits = "query.mixed-units";
    /// <summary>A query's op does not fit the type of the column it names, such as a numeric comparison on text.</summary>
    public const string QueryOpTypeMismatch = "query.op-type-mismatch";
    /// <summary>A query uses a kind the schema reserves for a later release, or a realizes tier that is not built yet.</summary>
    public const string QueryNotYetAvailable = "query.not-yet-available";
    /// <summary><c>not realizes(...)</c> was asked over a variant set that is not complete, so the absence it would claim is not proven.</summary>
    public const string QueryRealizesNegationIncomplete = "query.realizes-negation-incomplete";
    /// <summary><c>realizes(M, attested)</c> found no stored analysis that uses the morpheme to read its forms from.</summary>
    public const string QueryRealizesNoParses = "query.realizes-no-parses";
    /// <summary><c>--baseline-grammar</c> names the same file as <c>--grammar</c>, so nothing could change.</summary>
    public const string QueryBaselineSameGrammar = "query.baseline-same-grammar";
    /// <summary><c>--twin</c> was given with no <c>form ~</c> or <c>form matches</c> regex to rewrite the matches of.</summary>
    public const string QueryTwinNeedsRegex = "query.twin-needs-regex";
    /// <summary><c>--twin</c> was given with several distinct <c>form ~</c> patterns, or one under <c>or</c>, so the rewrite has no single regex.</summary>
    public const string QueryTwinRegexAmbiguous = "query.twin-regex-ambiguous";
    /// <summary>A pattern written decomposed cannot be composed to NFC without changing what it matches (a class or range holding a letter and its mark).</summary>
    public const string QueryPatternNotNormalizable = "query.pattern-not-normalizable";
    /// <summary>A <c>--twin</c> template names a group its regex does not have, or is malformed.</summary>
    public const string QueryTwinGroupUnknown = "query.twin-group-unknown";
    /// <summary><c>realizes(M, attested)</c> was given an id that no morph of the parse and no entry of the project carries.</summary>
    public const string QueryRealizesIdUnknown = "query.realizes-id-unknown";
    /// <summary><c>realizes(M, attested)</c> could not build the parse index of the corpus: <c>pangloss batch</c> failed or its analyses file is malformed.</summary>
    public const string QueryRealizesIndexFailed = "query.realizes-index-failed";
    /// <summary><c>realizes</c> ran with no project to resolve the morpheme in; corpus mode names one with <c>--grammar</c>.</summary>
    public const string QueryRealizesNeedsProject = "query.realizes-needs-project";
    /// <summary>A readings query (<c>readings</c>, <c>only-via</c>, <c>--group-by cause</c>, <c>--what-if-retire</c>) ran with no grammar to parse the corpus with.</summary>
    public const string QueryReadingsNeedsGrammar = "query.readings-needs-grammar";
    /// <summary>A readings query named an entry, and only a project's lexicon says which entry owns a morph's analysis.</summary>
    public const string QueryReadingsNeedsProject = "query.readings-needs-project";
    /// <summary><c>only-via</c> or <c>--what-if-retire</c> named an id that no morph of the corpus parse and no entry of the project carries, so it would silently match nothing.</summary>
    public const string QueryRetireIdUnknown = "query.retire-id-unknown";
    /// <summary><c>realizes(M, possible)</c> or <c>generated</c> needs PanGloss to answer the effects or generate call and it could not.</summary>
    public const string QueryRealizesGrammarUnavailable = "query.realizes-grammar-unavailable";
    /// <summary><c>realizes(M, possible)</c> or <c>generated</c> asks PanGloss about one morpheme, so it needs the entry and analysis ids, not one id or a bare form.</summary>
    public const string QueryRealizesNeedsIdentity = "query.realizes-needs-identity";
    /// <summary>A motif.query.v2 document or text expression is malformed or names an unsupported version.</summary>
    public const string QueryDocumentInvalid = "query.document-invalid";
    public const string QueryNoTrialRows = "query.no-trial-rows";
    /// <summary>The Baseline or Proposal Trial copy a query was asked to read as of is not available.</summary>
    public const string QueryAsOfUnavailable = "query.as-of-unavailable";
    /// <summary>A corpus word list or segment file named by a corpus query cannot be read or is malformed.</summary>
    public const string QueryCorpusUnreadable = "query.corpus-unreadable";
    /// <summary>A Guide block asked for a parse index that is not built; a render never builds one, so the block says which command does.</summary>
    public const string QueryParseIndexCold = "query.parse-index-cold";
    /// <summary>A corpus query option is out of range or its pattern is not a valid regular expression.</summary>
    public const string QueryCorpusInvalid = "query.corpus-invalid";
    /// <summary><c>--write-words</c> names a file that exists and <c>--force</c> was not given; nothing was written.</summary>
    public const string QueryWriteWordsExists = "query.write-words-exists";
    /// <summary><c>not morph(...)</c> or <c>not seq(...)</c> was asked over a corpus parse that is not complete, so the absence it would claim is not proven.</summary>
    public const string QuerySeqNegationIncomplete = "query.seq-negation-incomplete";
    /// <summary>An item of <c>morph(...)</c> or <c>seq(...)</c> matches no morph of the corpus parse, so the query would silently answer nothing.</summary>
    public const string QuerySeqItemUnknown = "query.seq-item-unknown";
    /// <summary>A <c>phon</c> or <c>env</c> predicate ran with no grammar to segment words and name natural classes; corpus mode needs <c>--grammar</c>.</summary>
    public const string QueryPhonNeedsGrammar = "query.phon-needs-grammar";
    /// <summary>The installed PanGloss does not declare the <c>match</c> serve op, so <c>phon</c> and <c>env</c> cannot run.</summary>
    public const string QueryPhonUnavailable = "query.phon-unavailable";
    /// <summary>PanGloss refused a <c>phon</c> or <c>env</c> pattern with a typed reason: a parse error, an unknown class or feature, or a construct it does not yet support.</summary>
    public const string QueryPhonPatternRefused = "query.phon-pattern-refused";
    /// <summary>The resident PanGloss failed, timed out or sent a reply Motif cannot read while matching a <c>phon</c> or <c>env</c> pattern.</summary>
    public const string QueryPhonFailed = "query.phon-failed";
    /// <summary>An <c>--approved</c> file cannot be read, or one of its lines is not a word with approved readings; the refusal names the line.</summary>
    public const string QueryApprovedInvalid = "query.approved-invalid";
}
