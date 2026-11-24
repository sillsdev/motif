# Query corpus forms

`motif query --corpus <words.tsv|folder> forms where <expression>` (also `forms --where <expression>` or `forms --query-json <file|->`) lists the words of a plain word list that satisfy a query expression, with no project, Trial or parse. The corpus is read as for `query pairs`; the expression is the text form of `motif.query.v2`, over the columns `form`, `tokens`, `parse.state`, `shape`, `syllables`, `fold`, `initial:n` and `final:n`.

```powershell
motif query --corpus words.tsv forms where 'phon(Stop V Stop V)' --grammar project.fwdata
motif query --corpus texts/ forms --where 'env([V] / # _ Stop) and tokens > 2' --grammar project.fwdata --json
```

`phon` and `env` need `--grammar <path>`, the grammar file `pangloss serve` loads; PanGloss segments the words with its surface character table and names the natural classes. Without it they are refused with `query.phon-needs-grammar`. Rows come by token count then form, `--limit` (default 100) keeps the first; a cut says `TRUNCATED`. The header counts the words the grammar cannot segment and lists a sample; `--json` writes `motif.query.corpus-forms.v1` with each row's `matches`. See `query forms` for the pattern language.
