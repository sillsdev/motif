#!/usr/bin/env python3
"""parse_grammar_texts_assessment.py -- read the three JSON files a Motif Handoff carries.

A Handoff is a folder of flat files a linguist drags into a chat model and asks about: why a
word did not parse, or why parsing is slow. This script is one of those files. It never talks to
the network and imports nothing beyond the standard library, because it has to run equally well on
a linguist's own machine and inside a chat product's sandboxed interpreter -- neither one will
install a package for it.

It ships beside three JSON files it knows how to read, and a fourth, `handoff.md`, that introduces
all four of them and is the right place to start:

    grammar.json      Every rule, part of speech, phoneme, and lexicon entry PanGloss parsed
                       with, exactly as PanGloss produced it. Its schema belongs to PanGloss, not
                       to this script -- see PanGloss's own docs/formats/grammar-format.md.
    texts.json         Every interlinear Text that was selected, carried over from FieldWorks'
                       own FLExText structure: paragraphs, phrases, words, and each analysed
                       word's morphemes. See docs/handoff/flextext-json-format.md.
    assessment.json    Present only when someone ran an Assessment. One record per Selection
                        word: whether PanGloss accepted it, how long that took, and -- for
                        whichever words were chosen for closer study -- the parser's own
                        derivation trace. See docs/handoff/assessment-format.md.
    handoff.md          Orientation: what these files are, one grep example and one Python call
                        per file. Read it first.

THE ONE-RECORD-PER-LINE CONVENTION
-----------------------------------
Every JSON file above is written as one JSON array, pretty-printed only down to the line: the
opening `[` and closing `]` each get their own line, and every element is compact JSON on its own
line, followed by a comma on every line but the last. This means two things stay true at once:

  * The whole file is one valid JSON document -- `json.load(open("assessment.json"))` just works,
    and returns the list of records.
  * A single line, pulled out on its own (`grep '"mirusi"' assessment.json`, or a line pasted into
    a chat), holds one whole, readable record -- possibly with a trailing comma, since it does not
    know whether the writer considered it the last one.

Every loader in this file accepts both forms without being told which one it is looking at: hand
it the file, or hand it a single grepped line (trailing comma and all), and either way you get the
same record back. That tolerance is this file's own job to provide; `grep` and a plain `json.load`
already do the rest, so you do not need this script at all to read a Handoff -- it exists to save
writing the same handful of filters and tree-walks by hand.

WHAT THIS FILE PROVIDES
------------------------
Every routine below has an obvious name and does one small thing:

  * Loading         load_grammar, load_texts, load_assessment
  * assessment.json  words_by_outcome, word_records, slowest_words
  * texts.json       get_text, iter_paragraphs, iter_phrases, iter_words, iter_morphemes,
                     surface_form, word_pos, analysis_status, phrase_translation,
                     morpheme_form, morpheme_gloss
  * grammar.json     find_rule, find_part_of_speech, find_phoneme, find_lexicon_entry
  * A trace, when a traced word's record carries one -- see the TRACES section below
                     trace_of, has_trace, walk_trace, trace_failures, trace_deepest_rule,
                     trace_summary_of

Import this file to use them from your own code (`from parse_grammar_texts_assessment import
word_records, load_assessment`), or run it as a command: `python parse_grammar_texts_assessment.py
--help` lists every subcommand, and each subcommand's own `--help` documents its arguments.

ON assessment.json's TRACES
-----------------------------
No Motif release writes a trace into assessment.json yet -- the batch pass that produces every
word's outcome and timing ships before the traced pass does. The routines in the TRACES section
below are written against the shape ADR 0045 describes for when tracing lands: a traced word's
record carries the derivation tree PanGloss's own `pangloss parse <grammar> <word> --trace
--trace-format=json` produced, verbatim, plus a short derived summary built from that same tree.
They have not been exercised against a real trace, because none exists yet to exercise them
against; `trace_of` and `has_trace` simply return nothing for a record that carries none, which is
every record today. The tree-walking routines (`walk_trace`, `trace_failures`,
`trace_deepest_rule`) work directly from PanGloss's published node shape -- every node carries
`type`, `children` (always present, possibly empty), and on some node kinds `source`, `subrule`,
`inputShape`, `outputShape`, and `failureReason` -- see PanGloss's docs/formats/trace-format.md for
the full list of node types and failure reasons. What is genuinely uncertain is only the key
`assessment.json` will use to attach that tree to a word's record; this file tries a short list of
plausible names (see `_TRACE_KEYS` below) and is written to degrade to "no trace" rather than
raise when none of them match.

Reference documents, for anything past what this file and `handoff.md` state:
  https://raw.githubusercontent.com/sillsdev/motif/{{MOTIF_REF}}/docs/handoff/flextext-json-format.md
  https://raw.githubusercontent.com/sillsdev/motif/{{MOTIF_REF}}/docs/handoff/assessment-format.md
  https://raw.githubusercontent.com/sillsdev/PanGloss/{{PANGLOSS_REF}}/docs/formats/grammar-format.md
  https://raw.githubusercontent.com/sillsdev/PanGloss/{{PANGLOSS_REF}}/docs/formats/trace-format.md
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Iterator

JsonValue = Any

# ---------------------------------------------------------------------------------------------
# Loading
# ---------------------------------------------------------------------------------------------


def load_json_lenient(path: str) -> JsonValue:
    """Load one Handoff JSON file, accepting either the whole file or a single grepped line.

    Tries a plain ``json.loads`` on the whole file first: every file this Handoff writes is
    already valid JSON end to end, so that succeeds for a complete `grammar.json`, `texts.json`,
    or `assessment.json` and returns whatever top-level value it holds -- a list for the latter
    two, since Motif always writes those as a JSON array; `grammar.json`'s own top-level shape
    belongs to PanGloss and is not assumed here.

    Falls back to reading line by line only when that whole-file parse fails -- which happens
    when `path` holds a fragment rather than the complete file: a handful of lines saved out of a
    `grep`, each possibly still carrying the trailing comma the writer puts after every record but
    the last, or the file's own bracketing `[` / `]` lines pulled in alongside them. Each surviving
    line is parsed on its own, and the results come back as a list -- a fragment of records is
    inherently plural, whatever shape the complete file turned out to have.
    """
    text = Path(path).read_text(encoding="utf-8")
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        pass

    records = []
    for raw_line in text.splitlines():
        line = raw_line.strip()
        if line in ("", "[", "]"):
            continue
        if line.endswith(","):
            line = line[:-1]
        try:
            records.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    return records


def _require_list(data: JsonValue, path: str) -> list:
    if not isinstance(data, list):
        raise ValueError(
            f"{path} did not load as a JSON array of records (got {type(data).__name__}); "
            "a Handoff's texts.json and assessment.json are always arrays, so this is not a "
            "shape this file will guess its way through."
        )
    return data


def load_grammar(path: str = "grammar.json") -> JsonValue:
    """Load grammar.json as whatever top-level JSON value it is -- PanGloss's shape, not ours."""
    return load_json_lenient(path)


def load_texts(path: str = "texts.json") -> list:
    """Load texts.json: one record per selected Text, each carrying its own ``key`` field."""
    return _require_list(load_json_lenient(path), path)


def load_assessment(path: str = "assessment.json") -> list:
    """Load assessment.json: one record per Selection word, keyed by its own ``word`` field."""
    return _require_list(load_json_lenient(path), path)


# ---------------------------------------------------------------------------------------------
# assessment.json
# ---------------------------------------------------------------------------------------------

OUTCOMES = ("analysed", "no-analysis", "capped", "timed-out", "skipped")
"""Every value assessment-format.md defines for a word record's ``outcome`` field."""


def words_by_outcome(records: list, outcome: str) -> list:
    """Every word record with the given ``outcome``.

    ``capped`` and ``timed-out`` mean the search was cut short, not exhausted: neither one is
    evidence that a word does not parse, only that PanGloss did not finish looking.
    """
    if outcome not in OUTCOMES:
        raise ValueError(f"outcome must be one of {OUTCOMES!r}, got {outcome!r}")
    return [record for record in records if record.get("outcome") == outcome]


def word_records(records: list, surface_form: str) -> list:
    """Every word record whose ``word`` field equals ``surface_form``, exactly as a grep would find."""
    return [record for record in records if record.get("word") == surface_form]


def slowest_words(records: list, count: int) -> list:
    """The ``count`` word records with the largest ``elapsedMs``, descending.

    A record with no ``elapsedMs`` (a word that was never measured) is excluded rather than
    sorted arbitrarily against the ones that were.
    """
    timed = [record for record in records if isinstance(record.get("elapsedMs"), (int, float))]
    return sorted(timed, key=lambda record: record["elapsedMs"], reverse=True)[:count]


# ---------------------------------------------------------------------------------------------
# assessment.json: traces (see the module docstring's TRACES section)
# ---------------------------------------------------------------------------------------------

_TRACE_KEYS = ("trace", "traceTree", "derivationTrace")
"""Candidate keys for a traced word's tree on its assessment.json record; see the module docstring."""

_TRACE_SUMMARY_KEYS = ("traceSummary", "traceSummaryV1")
"""Candidate keys for a traced word's precomputed one-line summary, tried before deriving one."""


def trace_of(record: dict) -> JsonValue | None:
    """The verbatim trace tree on a word's assessment.json record, or ``None`` when it has none.

    Every word in the Selection gets a statistics record; only the ones chosen for tracing carry
    a tree at all, so ``None`` here is the ordinary case, not a failure.
    """
    for key in _TRACE_KEYS:
        if key in record:
            return record[key]
    return None


def has_trace(record: dict) -> bool:
    """Whether a word's record carries a trace at all."""
    return trace_of(record) is not None


def walk_trace(node: dict) -> Iterator[tuple[int, dict]]:
    """Yield ``(depth, node)`` for a trace node and every descendant, depth-first, pre-order.

    Depth-first order matches how a derivation actually proceeds: a node's ``children`` are the
    steps tried inside it. A node missing ``children`` entirely is treated as a leaf rather than
    raising -- this file reads a trace, it does not validate one against PanGloss's schema.
    """
    stack: list[tuple[int, dict]] = [(0, node)]
    while stack:
        depth, current = stack.pop()
        yield depth, current
        children = list(current.get("children") or [])
        for child in reversed(children):
            stack.append((depth + 1, child))


def trace_failures(tree: dict) -> list[tuple[int, dict]]:
    """Every ``(depth, node)`` anywhere in ``tree`` that carries a ``failureReason``.

    PanGloss's trace-format.md defines 23 failure reasons; this does not enumerate or validate
    them, it only finds whichever nodes recorded one.
    """
    return [(depth, node) for depth, node in walk_trace(tree) if node.get("failureReason")]


def trace_deepest_rule(tree: dict) -> str | None:
    """The ``source`` (rule, stratum, or template name) on the deepest node the derivation reached.

    "Deepest" is by tree depth, not by how many steps preceded it in the walk order -- the node
    furthest from the root that named a rule at all.
    """
    deepest_depth = -1
    deepest_source: str | None = None
    for depth, node in walk_trace(tree):
        source = node.get("source")
        if source and depth > deepest_depth:
            deepest_depth = depth
            deepest_source = source
    return deepest_source


def summarize_trace(tree: dict) -> dict:
    """Build the one-line-summary fields ADR 0045 describes, directly from a raw trace tree.

    Used only as a fallback when a record's own precomputed summary is absent. The tree carries
    no PanGloss step-budget counter -- `MaxApplicationCount` in PanGloss's own failure reasons is
    HermitCrab's per-rule cap, not PanGloss's step budget -- so ``nodeCount`` here counts tree
    nodes visited, a proxy for "how much derivation happened," not PanGloss's own counter.

    An unmerged trace tries many branches, most of which fail, before one succeeds or the whole
    search gives up -- so ``outcome`` asks "does a ``Successful`` node appear anywhere in the
    tree", not "what was the first outcome node visited", which a plain pre-order walk would
    usually answer with a failed branch tried before the one that mattered.
    """
    nodes = list(walk_trace(tree))
    failure_reasons = sorted({node.get("failureReason") for _, node in nodes if node.get("failureReason")})
    node_types = {node.get("type") for _, node in nodes}
    if "Successful" in node_types:
        outcome = "Successful"
    elif "Failed" in node_types:
        outcome = "Failed"
    else:
        outcome = None
    return {
        "outcome": outcome,
        "nodeCount": len(nodes),
        "failureReasons": failure_reasons,
        "deepestRule": trace_deepest_rule(tree),
    }


def trace_summary_of(record: dict) -> dict | None:
    """A traced word's one-line summary: the record's own precomputed one if present, else derived.

    Returns ``None`` when the record carries no trace at all.
    """
    for key in _TRACE_SUMMARY_KEYS:
        if key in record:
            return record[key]
    tree = trace_of(record)
    return summarize_trace(tree) if tree is not None else None


# ---------------------------------------------------------------------------------------------
# texts.json
# ---------------------------------------------------------------------------------------------


def get_text(records: list, key: str) -> dict | None:
    """The Text record whose own ``key`` field matches, or ``None``."""
    return next((record for record in records if record.get("key") == key), None)


def interlinear_text(record: dict) -> dict | None:
    """The one ``interlinear-text`` object a texts.json record wraps, or ``None``.

    Every record HandoffWriter writes holds exactly one Text, nested under
    ``document.interlinear-text`` because that is FLExText's own element name, carried into JSON
    unchanged (see docs/handoff/flextext-json-format.md).
    """
    texts = record.get("document", {}).get("interlinear-text", [])
    return texts[0] if texts else None


def iter_paragraphs(record: dict) -> Iterator[dict]:
    """Every paragraph in a Text record, in document order."""
    text = interlinear_text(record)
    if text is None:
        return
    yield from text.get("paragraphs", {}).get("paragraph", [])


def iter_phrases(record: dict) -> Iterator[dict]:
    """Every phrase in a Text record, across all its paragraphs, in document order."""
    for paragraph in iter_paragraphs(record):
        yield from paragraph.get("phrases", {}).get("phrase", [])


def iter_words(record: dict) -> Iterator[tuple[dict, dict]]:
    """Every ``(phrase, word)`` pair in a Text record, in document order.

    The phrase comes along because a word's own free translation lives one level up, on the
    phrase's ``gls`` item, not on the word.
    """
    for phrase in iter_phrases(record):
        for word in phrase.get("words", {}).get("word", []):
            yield phrase, word


def iter_morphemes(word: dict) -> list:
    """A word's morphemes, or an empty list for an unanalysed word or a punctuation token."""
    morphemes = word.get("morphemes")
    return morphemes.get("morph", []) if morphemes else []


def item_value(items: list, item_type: str, lang: str | None = None) -> str | None:
    """The ``value`` of the first item matching ``item_type`` (and ``lang``, when given).

    An ``item``'s ``type`` string means different things depending on what contains it -- ``gls``
    on a phrase is a sentence's free translation, ``gls`` on a morpheme is that morpheme's sense
    gloss -- so this always operates on one container's own ``item`` list, never the whole
    document at once.
    """
    for item in items or []:
        if item.get("type") == item_type and (lang is None or item.get("lang") == lang):
            return item.get("value")
    return None


def surface_form(word: dict) -> str | None:
    """A word's own surface form, or ``None`` for a punctuation token, which carries no ``txt`` item."""
    return item_value(word.get("item"), "txt")


def word_pos(word: dict) -> str | None:
    """A word's chosen analysis category, abbreviated when FieldWorks had an abbreviation."""
    return item_value(word.get("item"), "pos")


def analysis_status(word: dict) -> str:
    """One of ``unanalysed``, ``approved``, or ``unapproved`` -- Motif's own three-value vocabulary.

    A word with no ``morphemes`` object at all (unanalysed, or punctuation) reports
    ``"unanalysed"`` here rather than ``None``, matching what the absence of that object means.
    """
    morphemes = word.get("morphemes")
    return morphemes.get("analysisStatus", "unanalysed") if morphemes else "unanalysed"


def phrase_translation(phrase: dict) -> str | None:
    """A phrase's free translation (its ``gls`` item -- not to be confused with a morpheme's)."""
    return item_value(phrase.get("item"), "gls")


def morpheme_form(morph: dict) -> str | None:
    """A morpheme's own surface form (its ``txt`` item -- not to be confused with a word's)."""
    return item_value(morph.get("item"), "txt")


def morpheme_gloss(morph: dict) -> str | None:
    """A morpheme's sense gloss (its ``gls`` item -- not to be confused with a phrase's translation)."""
    return item_value(morph.get("item"), "gls")


# ---------------------------------------------------------------------------------------------
# grammar.json
# ---------------------------------------------------------------------------------------------

_IDENTITY_KEYS = ("name", "abbreviation", "citationForm", "form", "symbol", "title", "id", "guid")
"""Field names this file treats as naming an entry, tried in order a person is likely to search by."""


def _walk_dicts(value: JsonValue) -> Iterator[dict]:
    """Yield every dict anywhere inside ``value``, however it is nested."""
    if isinstance(value, dict):
        yield value
        for nested in value.values():
            yield from _walk_dicts(nested)
    elif isinstance(value, list):
        for item in value:
            yield from _walk_dicts(item)


def find_by_name(grammar: JsonValue, name: str) -> list[dict]:
    """Every object anywhere in grammar.json whose name, abbreviation, guid, or similar field matches.

    grammar.json's schema belongs to PanGloss (see the module docstring) and is not fixed here, so
    this searches structurally rather than assuming a specific container key -- it keeps working
    whichever way PanGloss nests rules, parts of speech, phonemes, and lexicon entries.
    """
    return [obj for obj in _walk_dicts(grammar) if any(obj.get(key) == name for key in _IDENTITY_KEYS)]


def find_by_guid(grammar: JsonValue, guid: str) -> list[dict]:
    """Every object anywhere in grammar.json whose ``guid`` field matches exactly."""
    return [obj for obj in _walk_dicts(grammar) if obj.get("guid") == guid]


def find_rule(grammar: JsonValue, name: str) -> list[dict]:
    """Find a phonological, morphological, or compounding rule by name. See ``find_by_name``."""
    return find_by_name(grammar, name)


def find_part_of_speech(grammar: JsonValue, name: str) -> list[dict]:
    """Find a part of speech by its name or abbreviation. See ``find_by_name``."""
    return find_by_name(grammar, name)


def find_phoneme(grammar: JsonValue, symbol: str) -> list[dict]:
    """Find a phoneme or natural class by its symbol or name. See ``find_by_name``."""
    return find_by_name(grammar, symbol)


def find_lexicon_entry(grammar: JsonValue, name_or_guid: str) -> list[dict]:
    """Find a lexicon entry by citation form, form, or guid, whichever one matches first."""
    return find_by_name(grammar, name_or_guid) or find_by_guid(grammar, name_or_guid)


# ---------------------------------------------------------------------------------------------
# Command line
# ---------------------------------------------------------------------------------------------

_DESCRIPTION = """\
A Motif Handoff is five flat files, dragged straight from a folder window: grammar.json,
texts.json, assessment.json (only when someone ran an Assessment), this script, and handoff.md,
which introduces the other four and is the right place to start reading.

  grammar.json      Every rule, part of speech, phoneme, and lexicon entry PanGloss parsed with.
  texts.json        Every selected Text: paragraphs, phrases, words, and each word's morphemes,
                     carried over from FieldWorks' own FLExText structure.
  assessment.json   One record per Selection word: whether PanGloss accepted it (its outcome),
                     how long that took, and -- for words chosen for closer study -- the
                     parser's own derivation trace.
  handoff.md        Orientation for all four of the above; read it first.

Every JSON file is valid JSON with one record per line, so `grep '"some-word"' assessment.json`
returns that word's whole record on one line, and `json.load(open("assessment.json"))` loads the
whole file at once. This script's own loaders accept either input -- a grepped line, trailing
comma and all, or the complete file -- so you can pipe grep's output straight into it or point it
at the file on disk.

Run a subcommand's own --help for its arguments, for example:
  python parse_grammar_texts_assessment.py word --help
"""

_EPILOG = """\
examples:
  python parse_grammar_texts_assessment.py outcome capped
  python parse_grammar_texts_assessment.py word mirusi
  python parse_grammar_texts_assessment.py slowest 10
  python parse_grammar_texts_assessment.py trace mirusi
  python parse_grammar_texts_assessment.py text example-00000000-0000-0000-0000-000000000001
  python parse_grammar_texts_assessment.py words example-00000000-0000-0000-0000-000000000001
  python parse_grammar_texts_assessment.py grammar rule "Rule Name"
"""


def _print_json(value: JsonValue) -> None:
    print(json.dumps(value, indent=2, ensure_ascii=False))


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="parse_grammar_texts_assessment.py",
        description=_DESCRIPTION,
        epilog=_EPILOG,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--assessment", default="assessment.json", help="Path to assessment.json.")
    parser.add_argument("--texts", default="texts.json", help="Path to texts.json.")
    parser.add_argument("--grammar", default="grammar.json", help="Path to grammar.json.")

    subcommands = parser.add_subparsers(dest="command", required=True)

    outcome = subcommands.add_parser(
        "outcome", help="List assessment.json words with a given outcome.")
    outcome.add_argument("outcome", choices=OUTCOMES)

    word = subcommands.add_parser(
        "word", help="Show one assessment.json word's own record by its surface form.")
    word.add_argument("word")

    slowest = subcommands.add_parser(
        "slowest", help="Show the N slowest assessment.json words by elapsedMs.")
    slowest.add_argument("n", type=int)

    trace = subcommands.add_parser(
        "trace",
        help="Show a traced word's derived summary, or its verbatim tree with --tree.")
    trace.add_argument("word")
    trace.add_argument("--tree", action="store_true", help="Print the raw trace tree, not the summary.")

    text = subcommands.add_parser("text", help="Show one texts.json Text by its key.")
    text.add_argument("key")

    words = subcommands.add_parser(
        "words", help="List every word in one texts.json Text: form, part of speech, morphemes.")
    words.add_argument("key")

    grammar = subcommands.add_parser(
        "grammar", help="Find a named rule, part of speech, phoneme, or lexicon entry in grammar.json.")
    grammar.add_argument("kind", choices=("rule", "pos", "phoneme", "lexicon"))
    grammar.add_argument("name")

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_arg_parser()
    args = parser.parse_args(argv)

    if args.command == "outcome":
        _print_json(words_by_outcome(load_assessment(args.assessment), args.outcome))
        return 0

    if args.command == "word":
        _print_json(word_records(load_assessment(args.assessment), args.word))
        return 0

    if args.command == "slowest":
        _print_json(slowest_words(load_assessment(args.assessment), args.n))
        return 0

    if args.command == "trace":
        matches = word_records(load_assessment(args.assessment), args.word)
        if not matches:
            print(f"No assessment record for {args.word!r}.", file=sys.stderr)
            return 1
        record = matches[0]
        if args.tree:
            tree = trace_of(record)
            if tree is None:
                print(f"{args.word!r} has no trace on its assessment record.", file=sys.stderr)
                return 1
            _print_json(tree)
        else:
            summary = trace_summary_of(record)
            if summary is None:
                print(f"{args.word!r} was not traced.", file=sys.stderr)
                return 1
            _print_json(summary)
        return 0

    if args.command == "text":
        record = get_text(load_texts(args.texts), args.key)
        if record is None:
            print(f"No Text with key {args.key!r}.", file=sys.stderr)
            return 1
        _print_json(record)
        return 0

    if args.command == "words":
        texts = load_texts(args.texts)
        record = get_text(texts, args.key)
        if record is None:
            print(f"No Text with key {args.key!r}.", file=sys.stderr)
            return 1
        for _phrase, word in iter_words(record):
            form = surface_form(word) or "<punct>"
            pos = word_pos(word) or ""
            status = analysis_status(word)
            morphemes = ", ".join(
                f"{morpheme_form(morph)}={morpheme_gloss(morph)}" for morph in iter_morphemes(word)
            )
            print(f"{form}\t{pos}\t{status}\t{morphemes}")
        return 0

    if args.command == "grammar":
        finder = {
            "rule": find_rule,
            "pos": find_part_of_speech,
            "phoneme": find_phoneme,
            "lexicon": find_lexicon_entry,
        }[args.kind]
        _print_json(finder(load_grammar(args.grammar), args.name))
        return 0

    parser.error(f"unknown command {args.command!r}")
    return 2


if __name__ == "__main__":
    sys.exit(main())
