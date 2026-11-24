# ADR 0036 — Motif Query v2 is one JSON AST, with a text view and a builder view

**Status:** Accepted, 2026-11-23.

## Context

`motif query`, the MCP tool `motif_query` and the Grammar Guide's `motif-query` blocks share one parser and one
closed language: `column op value` conditions, all ANDed, no OR, no nesting. The grammar lanes' retro found they
almost never ran it. Every corpus question went to `grep`, `awk` or Python over a word-list TSV, because the query
had no OR or backreference, no frequency, no context fields and needed a project and a Trial.

The main use is searching a corpus, often unannotated, for candidate matches while building rules: discover what
recurs, write a candidate as a query, test it against hits, near misses and counterexamples, then turn it into a
grammar change that the Trial and Proposal machinery measures. The bar for v2 is to be quicker to reach for than
`grep`. It has three faces over one AST: JSON for agents, text for people and a builder for the window.

## Decisions

Each is "A or B, I am choosing A"; the owner may override any.

| # | Decision | Choice |
|---|---|---|
| D1 | Canonical form | A **JSON AST**, versioned `motif.query.v2`, shaped like Mongo and Elasticsearch queries. The text form is a lossless rendering of it, so switching views never loses anything. |
| D2 | Text syntax | **Extend the existing run-line.** Every v1 line keeps its meaning except unquoted `and`, `or` and `not` used as operators, which now combine predicates; `or`, `not` and parentheses are added, and an expression has no implicit precedence beyond `not` over `and` over `or`. |
| D3 | Regex anchoring | **Anchored by default**; the explicit `contains` op is the unanchored search. Both are NonBacktracking with the existing length cap. |
| D4 | Phonological patterns | Run by **PanGloss** (a `serve` op `match`), never a second matcher in C#, so they behave exactly like HermitCrab. |
| D5 | "Any rendering of a morpheme" | **Two tiers, always labelled:** `possible` (a sound superset from static rule analysis) and `generated` (forms actually generated, flagged `complete` or stopped at a cap). An exact reverse FST waits for the cascade-backend lane. |
| D6 | Negation over an incomplete set | **Refuse.** `not realizes(X, generated)` is refused unless the set is `complete`. |
| D7 | Variant entries | **Count as forms of a morpheme, labelled** allomorph, variant or rule; `include` narrows them. |
| D8 | Schema and completion | **Generated from the C# records** into `query.schema.json`, served by `motif query --schema` and the MCP read tool `motif_query_schema`; values come from the live grammar through `motif query --values`. One source, so it cannot drift. |
| D9 | Segments | **The grammar's segmentation** (PanGloss `segment`), words that will not segment reported as `unsegmentable`. The `aeiou` skeleton of `shape` stays as a fallback labelled `approximate`. |
| D10 | Running without a project | **Yes.** `--corpus <wordlist|folder>` runs every surface-only predicate with no project or Trial; frequency, and parse state when known, are joined automatically. Without an orthography file segments are code points, labelled `approximate`. |
| D11 | Occurrences | A **`tokens` source**, one row per occurrence, with `text`, `sentence.position`, `case`, `left.word` and `right.word`. |
| D12 | Backreferences | **Named captures in the pattern language**, not in `regex`, which stays NonBacktracking. |
| D13 | Near misses | A **`near` predicate** (`form`, `distance`, `unit: segment|feature`); `contrast` uses it for its near-miss group. |
| D14 | Pairs | `pairs` output can be grouped by base, with `having` patterns, to find bases attested with both affixes. |
| D15 | Residue | `residue: {against: stems|affixes|<file>}` strips known pieces and shows what is left. |
| D16 | Why parsing fails | A `parse.failure` field once PanGloss reports it; a field, not a feature. |
| D17 | Abugida scripts | An **orthography transform** in the segment file (inherent vowel, virama) so `phon` and `env` see consonants and vowels; plain regex keeps code points. |
| D18 | Generated contexts | `generated` defaults to rule-chosen test roots chosen by the `possible` analysis, plus attested neighbours when parses exist. Template walking is an opt-in audit (`--slots auto --depth 2`) labelled low confidence. Rules that reach past the edges (harmony) mark the result `complete: false`. |

The plan's phase table fixes the order: P1 (this ADR and the first slice) is the AST, the `and`/`or`/`not` text
form, the generated schema, `--schema`, `--query-json` and the MCP `query` object, over the four existing sources,
with a version on the response. Corpus mode, pairs and distribution follow it; `phon`, `env`, `realizes`, `morph`
and `seq` follow the PanGloss work.

## What the first slice commits to

- The AST kinds are a closed set. `morph`, `seq`, `phon`, `env` and `realizes` are reserved: they are in the
  schema so it stays stable, and a query that uses one is refused as not yet available.
- A field predicate's op must fit the field's type; a mismatch is a typed refusal, not a silent empty result.
- Guide blocks and ledgers keep the v1 text; nothing is rewritten.
- Corpus queries are strictly read-only; the Guide's `motif assess` behaviour does not apply to them.

## Consequences

- One AST means one validator, so an agent that is given only `query.schema.json` can write a valid query.
- Any divergence between the text, JSON and builder views is a bug with a round-trip test, not a design choice.
- PanGloss work (D4, D5, D9, D12) is filed to the PanGloss lead as PRs, each with a conformance fixture replayed
  on C# HermitCrab.
