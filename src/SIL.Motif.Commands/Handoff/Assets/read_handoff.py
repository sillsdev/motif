#!/usr/bin/env python3
"""Read a Motif AI Handoff folder using only the Python standard library.

A Handoff folder is what `motif handoff` writes: a grammar snapshot, one JSON mirror of
FLExText per chosen Text, a word selection, statistics, and this file. This module loads and
validates that folder, indexes grammar objects by GUID, resolves references, lists rules in
declared order, finds lexicon entries by form or gloss, lists a Text's words with their
analyses, reads statistics rows, and summarises counts.

Every function below returns plain Python data (dicts, lists, strings, generators) and never
prints. Printing is the job of the command-line entry point at the bottom of this file, which
exposes one subcommand per function so this module is usable two ways:

- **Imported**, in a sandbox that can load an uploaded module:
  `import read_handoff; handoff = read_handoff.load_handoff(".")`.
- **Read and reimplemented**, in a sandbox that cannot import an uploaded file at all: paste
  this file's text into a code cell, or ask the model to reproduce a function from its
  docstring and this module's behaviour. Nothing here reaches outside the standard library,
  so either path works without a package installer or network access.

Statistics files (`statistics/<group>.jsonl`) are read one line at a time rather than loaded
whole, because a statistics file can be large; `read_statistics` returns a generator for this
reason. Every other file here is small enough to read whole.

Validation errors from `validate_handoff` name the file they came from and, for a structural
problem in a parsed JSON file, a JSON-path-shaped location within it (for example
`texts/example.flextext.json: $.document.interlinear-text[0].guid`), so a reader can act on the
error without re-deriving where it came from.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Iterable, Iterator

STATISTICS_GROUPS = ("word", "object", "allomorph", "morpheme", "group", "never-fires")
"""The six groups `motif handoff` writes one statistics JSONL file per, and the only values
`read_statistics` accepts. Confirmed against PanGloss's own `stats --group` argument parser;
`never-fires` is the only hyphenated one."""

REQUIRED_TOP_LEVEL_FILES = (
    "instructions.md",
    "starter-prompt.md",
    "grammar.json",
    "selection.txt",
    "statistics.md",
    "recipes.md",
    "read_handoff.py",
)
REQUIRED_REFERENCE_FILES = ("grammar-format.md", "flextext-json-format.md", "hc-mechanics.md")


# ---------------------------------------------------------------------------
# Loading
# ---------------------------------------------------------------------------


def load_handoff(root: str | Path) -> dict[str, Any]:
    """Load a Handoff folder's grammar, Texts, selection, and statistics summary into memory.

    Returns a dict with keys ``root``, ``grammar`` (the parsed ``grammar.json``, or ``None`` if
    absent), ``texts`` (a dict from filename to parsed ``texts/*.flextext.json`` content),
    ``selection`` (the text of ``selection.txt``, or ``None``), and ``statistics_summary`` (the
    text of ``statistics.md``, or ``None``). Statistics JSONL rows are not loaded here — read
    them with `read_statistics`, which streams rather than slurps.
    """
    root = Path(root)

    grammar_path = root / "grammar.json"
    grammar = _read_json(grammar_path) if grammar_path.is_file() else None

    texts: dict[str, Any] = {}
    texts_dir = root / "texts"
    if texts_dir.is_dir():
        for path in sorted(texts_dir.glob("*.flextext.json")):
            texts[path.name] = _read_json(path)

    selection_path = root / "selection.txt"
    selection = selection_path.read_text(encoding="utf-8") if selection_path.is_file() else None

    statistics_summary_path = root / "statistics.md"
    statistics_summary = (
        statistics_summary_path.read_text(encoding="utf-8") if statistics_summary_path.is_file() else None
    )

    return {
        "root": str(root),
        "grammar": grammar,
        "texts": texts,
        "selection": selection,
        "statistics_summary": statistics_summary,
    }


def validate_handoff(root: str | Path) -> list[str]:
    """Check a Handoff folder's required files and the shape of its JSON, without loading everything.

    Returns a list of error strings, each naming the offending file and, where the file parsed
    as JSON, a JSON-path-shaped location inside it. An empty list means the folder is well
    formed. This never raises for an ordinary malformed folder; it only raises if `root` itself
    is not readable as a directory.
    """
    root = Path(root)
    errors: list[str] = []

    for name in REQUIRED_TOP_LEVEL_FILES:
        if not (root / name).is_file():
            errors.append(f"{name}: required file is missing")
    for name in REQUIRED_REFERENCE_FILES:
        if not (root / "reference" / name).is_file():
            errors.append(f"reference/{name}: required file is missing")

    grammar_path = root / "grammar.json"
    if grammar_path.is_file():
        grammar, parse_errors = _try_read_json(grammar_path)
        errors.extend(parse_errors)
        if grammar is not None and not isinstance(grammar, dict):
            errors.append(f"{grammar_path.name}: $: expected a JSON object")

    texts_dir = root / "texts"
    if texts_dir.is_dir():
        for path in sorted(texts_dir.glob("*.flextext.json")):
            errors.extend(_validate_flextext_json(path))

    statistics_dir = root / "statistics"
    if statistics_dir.is_dir():
        for path in sorted(statistics_dir.glob("*.jsonl")):
            errors.extend(_validate_jsonl_file(path))

    return errors


def _read_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def _try_read_json(path: Path) -> tuple[Any, list[str]]:
    try:
        return _read_json(path), []
    except json.JSONDecodeError as error:
        return None, [f"{path.name}: line {error.lineno} column {error.colno}: {error.msg}"]


def _validate_jsonl_file(path: Path) -> list[str]:
    errors = []
    with path.open("r", encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, start=1):
            if not line.strip():
                continue
            try:
                json.loads(line)
            except json.JSONDecodeError as error:
                errors.append(f"{path.name}: line {line_number}: {error.msg}")
    return errors


def _validate_flextext_json(path: Path) -> list[str]:
    root, parse_errors = _try_read_json(path)
    if parse_errors:
        return parse_errors
    errors: list[str] = []

    def fail(json_path: str, message: str) -> None:
        errors.append(f"{path.name}: {json_path}: {message}")

    if not isinstance(root, dict):
        fail("$", "expected a JSON object")
        return errors

    document = root.get("document")
    if not isinstance(document, dict):
        fail("$.document", "expected an object")
        return errors

    interlinear_texts = document.get("interlinear-text")
    if not isinstance(interlinear_texts, list):
        fail("$.document.interlinear-text", "expected an array")
        return errors

    for index, itext in enumerate(interlinear_texts):
        base = f"$.document.interlinear-text[{index}]"
        if not isinstance(itext, dict):
            fail(base, "expected an object")
            continue
        if not isinstance(itext.get("guid"), str):
            fail(f"{base}.guid", "expected a string")
        for item in itext.get("item") or []:
            _validate_item(item, f"{base}.item", fail)

    return errors


def _validate_item(item: Any, json_path: str, fail: Any) -> None:
    if not isinstance(item, dict):
        fail(json_path, "expected an item object")
        return
    for key in ("type", "lang", "value"):
        if key not in item:
            fail(json_path, f"expected a '{key}' field")


# ---------------------------------------------------------------------------
# Grammar
# ---------------------------------------------------------------------------


def index_grammar(grammar: dict[str, Any]) -> dict[str, Any]:
    """Index every object in a parsed ``grammar.json`` by its ``guid``, at any nesting depth.

    Grammar objects are identified the same way FLExText objects are: a ``guid`` field on the
    object itself. This walk does not assume any other PanGloss field name, so it indexes
    whatever the current snapshot contains without needing to know its full shape in advance.
    """
    index: dict[str, Any] = {}

    def walk(node: Any) -> None:
        if isinstance(node, dict):
            guid = node.get("guid")
            if isinstance(guid, str):
                index[guid] = node
            for value in node.values():
                walk(value)
        elif isinstance(node, list):
            for item in node:
                walk(item)

    walk(grammar)
    return index


def resolve_guid(index: dict[str, Any], guid: str) -> Any:
    """Look up one object an earlier `index_grammar` call indexed, or ``None`` if it is absent."""
    return index.get(guid)


_RULE_LIST_KEYS = ("rules", "phonologicalRules", "morphologicalRules")


def list_rules(grammar: dict[str, Any]) -> list[Any]:
    """List a grammar's rules in the declared order `grammar.json` stores them in.

    Rule order is semantic in HermitCrab (feeding and bleeding depend on it), so this never
    reorders what it finds. PanGloss's exact top-level key for the rule list may vary by
    snapshot version; the first of `_RULE_LIST_KEYS` present is used, and an absent list
    yields an empty result rather than a guess.
    """
    for key in _RULE_LIST_KEYS:
        value = grammar.get(key)
        if isinstance(value, list):
            return value
    return []


_ENTRY_LIST_KEYS = ("entries", "lexicalEntries")
_ENTRY_CONTAINER_KEYS = ("lexicon",)
_FORM_KEYS = ("form", "lexemeForm", "citationForm")
_GLOSS_KEYS = ("gloss", "glosses")


def find_entries(
    grammar: dict[str, Any], *, form: str | None = None, gloss: str | None = None
) -> list[Any]:
    """Find lexicon entries by form or gloss, matched case-insensitively as a substring.

    With neither `form` nor `gloss` given, every entry is returned. An entry's form and gloss
    are read through a short list of candidate field names, since PanGloss's exact naming for a
    given snapshot version is not assumed.
    """
    entries = _find_entry_list(grammar)

    def matches(entry: Any) -> bool:
        if not isinstance(entry, dict):
            return False
        if form is not None and not _field_contains(entry, _FORM_KEYS, form):
            return False
        if gloss is not None and not _field_contains(entry, _GLOSS_KEYS, gloss):
            return False
        return True

    return [entry for entry in entries if matches(entry)]


def _find_entry_list(grammar: dict[str, Any]) -> list[Any]:
    for key in _ENTRY_LIST_KEYS:
        value = grammar.get(key)
        if isinstance(value, list):
            return value
    for container_key in _ENTRY_CONTAINER_KEYS:
        container = grammar.get(container_key)
        if isinstance(container, dict):
            for key in _ENTRY_LIST_KEYS:
                value = container.get(key)
                if isinstance(value, list):
                    return value
    return []


def _field_contains(entry: dict[str, Any], candidate_keys: Iterable[str], needle: str) -> bool:
    needle_lower = needle.lower()
    for key in candidate_keys:
        value = entry.get(key)
        for candidate in value if isinstance(value, list) else [value]:
            if isinstance(candidate, str) and needle_lower in candidate.lower():
                return True
    return False


# ---------------------------------------------------------------------------
# Texts
# ---------------------------------------------------------------------------


def list_text_words(text: dict[str, Any], *, include_analyses: bool = True) -> list[dict[str, Any]]:
    """List one parsed ``texts/*.flextext.json`` Text's words, in document order.

    Each returned word is a dict with ``guid`` (``None`` for punctuation) and ``items`` (its
    `type`/`lang`/`value` items — surface form, and category when an analysis chose one). With
    `include_analyses` true (the default), each word also carries ``analysis_status`` — one of
    ``"unanalysed"``, ``"approved"``, or ``"unapproved"``, Motif's own three-value vocabulary,
    not FLExText's XML enumeration — and ``morphemes``, its morph breakdown in order.
    """
    words: list[dict[str, Any]] = []
    interlinear_texts = text.get("document", {}).get("interlinear-text", [])

    for itext in interlinear_texts:
        for paragraph in itext.get("paragraphs", {}).get("paragraph", []):
            for phrase in paragraph.get("phrases", {}).get("phrase", []):
                for word in phrase.get("words", {}).get("word", []):
                    entry: dict[str, Any] = {
                        "guid": word.get("guid"),
                        "items": word.get("item", []),
                    }
                    if include_analyses:
                        morphemes = word.get("morphemes")
                        entry["analysis_status"] = (
                            morphemes.get("analysisStatus") if morphemes else "unanalysed"
                        )
                        entry["morphemes"] = morphemes.get("morph", []) if morphemes else []
                    words.append(entry)

    return words


# ---------------------------------------------------------------------------
# Statistics
# ---------------------------------------------------------------------------


def read_statistics(root: str | Path, group: str) -> Iterator[dict[str, Any]]:
    """Stream one row at a time from ``statistics/<group>.jsonl``, never loading the whole file.

    `group` must be one of `STATISTICS_GROUPS`. This is a generator: a caller that wants a list
    must build one explicitly (``list(read_statistics(root, "word"))``), which keeps the choice
    to hold a large statistics file entirely in memory with the caller, not this function.
    """
    if group not in STATISTICS_GROUPS:
        raise ValueError(f"Unknown statistics group {group!r}; expected one of {STATISTICS_GROUPS}.")

    path = Path(root) / "statistics" / f"{group}.jsonl"
    with path.open("r", encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, start=1):
            stripped = line.strip()
            if not stripped:
                continue
            try:
                yield json.loads(stripped)
            except json.JSONDecodeError as error:
                raise ValueError(f"{path.name}: line {line_number}: {error.msg}") from error


# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------


def summarize_counts(handoff: dict[str, Any]) -> dict[str, Any]:
    """Summarise a loaded Handoff's shape: how many Texts, words, rules, and lexicon entries.

    Takes the dict `load_handoff` returns. Statistics files are not counted here since they are
    not loaded by `load_handoff` — read them with `read_statistics` if a count from them is
    needed.
    """
    grammar = handoff.get("grammar") or {}
    texts: dict[str, Any] = handoff.get("texts") or {}

    words_by_text = {name: len(list_text_words(text, include_analyses=False)) for name, text in texts.items()}

    return {
        "text_count": len(texts),
        "word_count": sum(words_by_text.values()),
        "words_by_text": words_by_text,
        "rule_count": len(list_rules(grammar)),
        "entry_count": len(find_entries(grammar)),
    }


# ---------------------------------------------------------------------------
# Command-line entry point — printing lives only here
# ---------------------------------------------------------------------------


def _print_json(value: Any) -> None:
    print(json.dumps(value, indent=2, ensure_ascii=False, sort_keys=False))


def _cmd_load_handoff(args: argparse.Namespace) -> None:
    _print_json(load_handoff(args.root))


def _cmd_validate_handoff(args: argparse.Namespace) -> None:
    errors = validate_handoff(args.root)
    _print_json(errors)
    if errors:
        sys.exit(1)


def _cmd_index_grammar(args: argparse.Namespace) -> None:
    grammar = _read_json(Path(args.root) / "grammar.json")
    _print_json(sorted(index_grammar(grammar).keys()))


def _cmd_resolve_guid(args: argparse.Namespace) -> None:
    grammar = _read_json(Path(args.root) / "grammar.json")
    _print_json(resolve_guid(index_grammar(grammar), args.guid))


def _cmd_list_rules(args: argparse.Namespace) -> None:
    grammar = _read_json(Path(args.root) / "grammar.json")
    _print_json(list_rules(grammar))


def _cmd_find_entries(args: argparse.Namespace) -> None:
    grammar = _read_json(Path(args.root) / "grammar.json")
    _print_json(find_entries(grammar, form=args.form, gloss=args.gloss))


def _cmd_list_text_words(args: argparse.Namespace) -> None:
    text = _read_json(Path(args.root) / "texts" / args.text_file)
    _print_json(list_text_words(text, include_analyses=not args.no_analyses))


def _cmd_read_statistics(args: argparse.Namespace) -> None:
    for row in read_statistics(args.root, args.group):
        print(json.dumps(row, ensure_ascii=False))


def _cmd_summarize_counts(args: argparse.Namespace) -> None:
    _print_json(summarize_counts(load_handoff(args.root)))


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Read a Motif AI Handoff folder using only the Python standard library.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    def add(name: str, handler: Any, configure: Any = None) -> None:
        sub = subparsers.add_parser(name)
        sub.add_argument("root", help="Path to the Handoff folder.")
        if configure is not None:
            configure(sub)
        sub.set_defaults(handler=handler)

    add("load-handoff", _cmd_load_handoff)
    add("validate-handoff", _cmd_validate_handoff)
    add("index-grammar", _cmd_index_grammar)
    add("resolve-guid", _cmd_resolve_guid, lambda sub: sub.add_argument("guid"))
    add("list-rules", _cmd_list_rules)
    add(
        "find-entries",
        _cmd_find_entries,
        lambda sub: (sub.add_argument("--form"), sub.add_argument("--gloss")),
    )
    add(
        "list-text-words",
        _cmd_list_text_words,
        lambda sub: (
            sub.add_argument("text_file", help="Filename under texts/, e.g. example-<guid>.flextext.json"),
            sub.add_argument("--no-analyses", action="store_true"),
        ),
    )
    add("read-statistics", _cmd_read_statistics, lambda sub: sub.add_argument("group", choices=STATISTICS_GROUPS))
    add("summarize-counts", _cmd_summarize_counts)

    return parser


def main(argv: list[str] | None = None) -> None:
    parser = _build_parser()
    args = parser.parse_args(argv)
    args.handler(args)


if __name__ == "__main__":
    main()
