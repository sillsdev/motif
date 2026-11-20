# Group words by stem

Run `motif group suffix --project <fwdata> --ending <text>` to list the word types of the project's Texts that end in a string, grouped by the stem left when the ending is removed. `motif group prefix --project <fwdata> --start <text>` does the same for a start. The MCP tool `motif_group` takes `project`, `kind` (suffix or prefix), `ending` or `start` or `pattern`, `parseState`, `limit`, `page`, `asOf` and `fold`. It reads a saved copy of the project and never writes.

Each stem is marked one of three ways. **known**: the stem is a stem form in the lexicon. **fitting**: it is not in the lexicon, but the bare stem is itself a word of the Texts, or other words share the stem, which is evidence the stem is real. **not-fitting**: only the words you asked for attest it, so the string may be part of the word rather than an affix. Each stem lists its matching words with their parse state and token count, and up to five other words that share the stem.

- `--pattern <regex>` replaces `--ending` or `--start`. It is anchored to the whole folded word, needs one named group `(?<stem>...)` for the stem, is at most 256 characters and cannot backtrack, so it cannot run away. Write it in the folded spelling (lower case by default).
- `--parse-state parsed|unparsed|approved|all` keeps the matching words in that state: approved means a human approved a parse, parsed means a parse exists and none is approved, unparsed means none. The default is all. The known and fitting marks always look at every word.
- `--min-stem N` is the fewest graphemes a stem may have: 2 by default, so one-letter stems do not swamp the list; 1 lists them too.
- `--limit N` is the number of stems on a page: 50 by default, at most 200. A larger value is reduced and the response says so. `--page N` reads the next page; when more stems exist the output says how to reach them.
- `--format table|json` (or `--json`) chooses the output.
- `--fold <set>` chooses how spellings are compared: `nfc,case` by default, a named set such as `c~k`, or `project` for the project's own fold rules. Endings, starts and stems are cut between grapheme clusters, so a combining mark never separates from its letter. A leading or trailing hyphen on an affix is ignored.
- `--as-of baseline` reads the current Baseline and `--as-of <proposalId>` reads that Proposal's latest Trial copy, so a before and after can be compared. Other revisions cannot be named yet; they refuse with `group.as-of-unavailable`.

Words come from the Texts' analysed occurrences. Paragraphs without a current word analysis, and wordforms marked incorrectly spelled, are left out, and the response says how many.
