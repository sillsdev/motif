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
