# Query corpus forms

`motif query --corpus <words.tsv|folder> forms where <expression>` (also `forms --where <expression>` or `forms --query-json <file|->`) lists the words of a plain word list that satisfy a query expression, with no project, Trial or parse. The corpus is read as for `query pairs`; the expression is the text form of `motif.query.v2`, over the columns `form`, `tokens`, `parse.state`, `shape`, `syllables`, `fold`, `initial:n` and `final:n`.

```powershell
motif query --corpus words.tsv forms where 'phon(Stop V Stop V)' --grammar project.fwdata
motif query --corpus texts/ forms --where 'env([V] / # _ Stop) and tokens > 2' --grammar project.fwdata --json
```

`phon` and `env` need `--grammar <path>`, the grammar file `pangloss serve` loads; PanGloss segments the words with its surface character table and names the natural classes. Without it they are refused with `query.phon-needs-grammar`. Rows come by token count then form, `--limit` (default 100) keeps the first; a cut says `TRUNCATED`. The header counts the words the grammar cannot segment and lists a sample; `--json` writes `motif.query.corpus-forms.v1` with each row's `matches`. See `query forms` for the pattern language.

`realizes(<morpheme>, attested)` finds the words that carry a morpheme in any surface form its stored analyses cut for it. The morpheme is `entry:msa`, an entry or analysis id, or a form in the lexicon, optionally led by `root:`, `pfx:` or `sfx:`; `--grammar` names the project that holds the lexicon and the analyses, which Motif reads from a private copy and never writes. The forms compile to one search over the words' surface text, anchored at the start for a prefix and the end for a suffix. The header says what was searched for, whether the set is complete, and that the search can over-match, because it reads characters and not the grammar's segments; each row names the forms it carries. A project with no analysis that uses the morpheme is refused as `query.realizes-no-parses`, and `not realizes(...)` over a set that is not complete is refused as `query.realizes-negation-incomplete`. The tiers `possible` and `generated` parse and are refused as not yet available.
