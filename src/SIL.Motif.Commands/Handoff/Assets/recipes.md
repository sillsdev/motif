# Recipes

Worked examples for asking a question over this folder. Each one names the call to make and the
question it answers. Read `instructions.md` first if you have not already — in particular, the
data-sensitivity warning and what `analysisStatus` does and does not tell you.

## If your chat model can run code against the uploaded files

This is the fastest path, and the one to prefer whenever it is available: have the model import
`read_handoff.py` directly and call its functions, rather than have it read the raw JSON itself.
A `.flextext.json` file for even a short text runs to hundreds of lines of nested structure —
the same reliability problem as asking a model to eyeball a large XML file by hand. Every
function below returns plain data; nothing here prints anything itself.

```python
import importlib.util

spec = importlib.util.spec_from_file_location("read_handoff", "read_handoff.py")
read_handoff = importlib.util.module_from_spec(spec)
spec.loader.exec_module(read_handoff)

handoff = read_handoff.load_handoff(".")
print(read_handoff.summarize_counts(handoff))
```

**"Is this folder well-formed before I trust anything in it?"**

```python
errors = read_handoff.validate_handoff(".")
```

An empty list means every required file is present and every JSON file parses and has the
expected shape. A non-empty list names the file and the location inside it for each problem.

**"What rules does this grammar have, in the order they apply?"**

```python
grammar = handoff["grammar"]
rules = read_handoff.list_rules(grammar)
```

Order is never re-sorted — see `reference/hc-mechanics.md` for why rule order is semantic.

**"Does this grammar have an entry with the form `X`, or glossed `Y`?"**

```python
read_handoff.find_entries(grammar, form="miru")
read_handoff.find_entries(grammar, gloss="run")
```

**"What does this GUID I found in a Text refer to, inside the grammar?"**

```python
index = read_handoff.index_grammar(grammar)
read_handoff.resolve_guid(index, "00000000-0000-0000-0000-000000000005")
```

**"List this Text's words, and tell me which analyses are confirmed versus guessed."**

```python
text = handoff["texts"]["example-00000000000000000000000000000001.flextext.json"]
words = read_handoff.list_text_words(text)
approved = [w for w in words if w.get("analysis_status") == "approved"]
```

**"How many attempts on `word`-level statistics failed, and why?"**

```python
for row in read_handoff.read_statistics(".", "word"):
    ...  # a generator: this streams the file rather than loading it whole
```

## If your chat model cannot load an uploaded module at all

Some sandboxes can execute code but cannot `import` a file you attached — only code you typed or
pasted directly. Two ways around that, in order of preference:

1. **Paste `read_handoff.py`'s text into a code cell yourself**, then call its functions exactly
   as above. The whole point of writing it against nothing but the standard library is that
   pasting its source is always enough — there is no install step to fail.
2. **Ask the model to reimplement one function from its docstring**, if you would rather not
   paste the whole file. Every function's docstring states its contract completely enough to
   reproduce: what it takes, what it returns, and what it does not do (for example,
   `read_statistics` streams rather than loading a whole file, and `list_rules` never reorders
   what it finds). Point the model at the specific function's docstring rather than the whole
   module if the file is large enough that pasting all of it is inconvenient.

Either way, the underlying JSON files (`grammar.json`, `texts/*.flextext.json`,
`statistics/*.jsonl`) are ordinary JSON and JSON Lines — nothing about reading them requires
`read_handoff.py` specifically, only understanding the shape `reference/grammar-format.md` and
`reference/flextext-json-format.md` document.
