# Record a judgement call in a Grammar Guide section

`guide decide --project <fwdata> <GDR-n> --choosing <A> --over <B> --because <reason> [--draft <name>]` stages one judgement call under the section's **Decisions** heading, written the way the Guide template asks:

```text
- A or B, I am choosing A, because the reason.
```

The heading `### Decisions` is added at the end of the section when it has none, and a later call is added to the same list. Each part is one line and none may be blank; a part is written as text, so `*`, `_`, `[` and backslash are escaped, and a reason that already ends in a full stop keeps it. The verb records a call; the grammar lead or linguist makes it (ADR 0004). Like `guide edit`, it stages into the pending changes, or into the named existing `--draft`, and a Guide-only Proposal is exempt from the Autonomous Trial gate.

```text
motif guide decide --project Koro.fwdata GDR-4 --choosing "plural -ng" --over "plural -ngo" --because "-ngo only appears after vowels in the Gospel of Mark" --autonomous
```

`guide decide` is an Autonomous command: pass `--autonomous` or set MOTIF_AUTONOMOUS=1. It does not imply Advanced AI mode, which `guide edit` and `guide add` need (`motif settings advanced-ai on`); `guide show` needs neither. An agent that edits sections and records decisions needs both. A value flag left without a value is refused. The section must already exist and the project's styles must include Heading 3 and Bulleted List, which a FieldWorks project has.

## Record many decisions at once

`guide decide --project <fwdata> --from <file.jsonl | -> [--dry-run] [--draft <name>]` stages a batch, so a lane records its decisions with one command and no script. The file holds one JSON object per line, with the string keys `section`, `choosing`, `over` and `because`, optionally `rung` and `tried`, and no others. The Guide template has no field for a rung or for what was tried, so `tried` takes the place of `over` (or, when `over` is also given, is added to it as `(tried: X)`) and `rung` is added to the end of `because` as `(rung: X)`; blank lines are skipped:

```text
{"section":"GDR-4","choosing":"plural -ng","over":"plural -ngo","because":"-ngo only appears after vowels"}
{"section":"GDR-7","choosing":"prefix ka-","over":"prefix ki-","because":"ka- is the Mark 1 form"}
```

Each row is written as the single form is, and rows for one section are added to its Decisions list in file order. Every row is read the way the Guide reads it before anything is staged, so a bad row refuses the whole batch with `guide.decisions-invalid`, names its line in the message and in the `row` fact, and stages nothing. A bare `@` or `<` that opens nothing (`a @ b`, `mail@host`, `a<b`) is plain text. A citation (`@smith`), an inline reference (`@{`) or a tag (`<NP>`) is refused before anything is staged, as `guide.decision-invalid` (batch: `guide.decisions-invalid`) naming the field and character offset; write `\@` or `\<` to keep it as text. A `*`, `_`, `[` or backtick that the Guide cannot read as Markdown is refused the same way; escape it with a backslash. `--dry-run` runs that check, prints each line's entry and stages nothing; run the same command without it to stage. A section that does not exist, or a Draft that does not, refuses the batch the same way. The whole batch is then staged in one Draft save, into the pending changes or the named existing `--draft`, so a failure at that step also leaves nothing staged.
