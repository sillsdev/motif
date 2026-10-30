# 0078. Case handling lives in the host layer

Motif copies FieldWorks' lowercase second pass in its host layer. The parser, PanGloss, stays exact-match,
as HermitCrab does.

Every word is parsed as written. When the whole word's lowercase form differs, the host parses that form too,
even when the original parsed. The lowercase form comes from the default vernacular writing system's ICU casing
(liblcm `CaseFunctions`: the case alias when the writing system has one, otherwise its ICU locale), never from a
generic `ToLower` and never from Unicode case folding. Turkish and Azeri dotted and dotless I therefore lowercase
as ICU says for the writing system.

The lowercase parse's readings belong to a separate lowercase wordform. They are never merged into the original,
which keeps the readings of its own parse. In Assess and Trial the lowercase form is one more row of the same
batch, marked with the word it came from; a lowercase form already asked for, or shared by several words, is
parsed once. In Try a Word the response carries the lowercase trace beside the original's. Parse cost is counted
in work steps, so the lowercase parse's steps are part of every total that sums rows. The seam is the batch the
assessor sends and the trace the word-trace query makes, so Assess, Trial and Try a Word share one rule.

## Why not in the parser

HermitCrab compares strings exactly. FieldWorks keeps it that way and does case upstream, in `ParserWorker`.
Putting case inside PanGloss would make it differ from HermitCrab on the conformance suite, and a parser-side
case rule has no writing system to take its casing from. The host knows the writing system, so it owns the rule.

## Accepted limits

- There is no case-required lexicon flag. A lexeme cannot say it must be capitalised.
- Mid-word capitals, such as Irish *hÉireann*, are not handled: the pass lowercases the whole word or not at all.
- *Bill* and *bill* both get readings, under separate wordforms. Neither suppresses the other.
- Case is taken from the project's default vernacular writing system, not per wordform. FieldWorks uses each
  wordform's own writing system; a parser word here is a bare string, so a second vernacular writing system whose
  casing differs (a Turkish and an English pair, or a case alias) is lowercased by the default's rules.
- Unicode case folding is never used for matching. It is lossy (ß folds to ss, İ to i plus a combining dot) and is
  not locale-aware for Turkish and Azeri.

## Evidence

How FieldWorks does it, with the code and tickets that added it: `_briefs/case-folding-fieldworks.md`. The
casing options considered, and why writing-system ICU casing is the one to use: `_briefs/case-folding-literature.md`.
