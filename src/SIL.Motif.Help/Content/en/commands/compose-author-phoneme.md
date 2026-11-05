# Author a phoneme

Stages a phoneme with explicitly authored name and representations in a Draft. Use its created identity in later natural classes or phonological rules. Only a person can Apply.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

`representations` are literal spellings, including `(`, `)`, `/`, square brackets and other symbols used by imported projects. Do not escape them as environment syntax: `"representations": ["/"]` stores a literal slash. Supply `features` as `{ "feature": "<feature ID>", "value": "<value ID>" }` pairs to give each distinct sound or literal symbol its explicit feature bundle. A shared bundle permits unification; use distinct bundles when the segments must stay distinct. Empty representations, dotted-circle-only representations, parser-reserved boundary/null spellings (`+`, `.`, `^0`, `*0`, `&0`, `∅`), duplicate spellings and spellings owned by another phoneme or boundary are refused. Spellings are canonically decomposed, and display dotted circles are removed as in FieldWorks' parser loader.

Put case spellings of the same sound on the same phoneme: `["c", "C"]` or `["ng", "Ng", "NG"]`. Accented vowels remain distinct when they express different sounds; do not fold accents away. Implicit capital letters carry no phonological features, so [Check a grammar](cmd:grammar%20check) warns when a featured lowercase phoneme lacks titlecase or uppercase representations.

A literal symbol in a phonological rule is a typed phoneme context, not textual syntax. In an allomorph environment, use a singleton [natural class](cmd:compose-author-natural-class) for a symbol that collides with environment syntax. For example, a class containing only the `(` phoneme can appear as `[LP]`; parentheses outside a class reference keep their environment meaning.

Create features, choose their particular values and author phonemes in one [batch](cmd:compose-batch) with references such as `@identity:values:slash`.
