# Parser help — HermitCrab and FLExText reference for LLMs

This directory is a copy of FieldWorks' `Docs/ai-parser-help/`, moved here ahead of FieldWorks
being stripped of its own handoff material. It is a living reference for asking an LLM (ChatGPT,
Claude, or otherwise) questions about **HermitCrab**, the rule-based morphological
parser/generator (`SIL.Machine.Morphology.HermitCrab`, in `sillsdev/machine`) that both FieldWorks'
own interactive parser and PanGloss are built on, and about **FLExText**, FieldWorks' interlinear-
text interchange format for connected corpus texts. Every claim in it is grounded in that engine's
actual source, cited file by file inside each topic page.

**This is one of the Handoff's pinned links.** Motif's Handoff pastes a short header ahead of the
five files it drags into a chat, and that header's links point here for the deeper HermitCrab and
FLExText explanations a linguist's question may need — the same way it points at PanGloss's
`docs/formats/` for the grammar, trace, and mechanics documents PanGloss itself owns. Nothing in
this directory travels inside the five-file folder; it is read by following a link, not by being
dragged along.

## What the four subdirectories cover

- **[`broken/`](broken/README.md)** — "why is this wrong, missing, or crashing?" Sixteen files:
  wrong parses, missing parses, crashes, and silent misconfigurations, each citing the specific
  `machine` (and, where the trap is in how FieldWorks compiles a grammar into HermitCrab's XML,
  `FieldWorks`) source that produces the behavior.
- **[`speed/`](speed/README.md)** — "why is this slow?" Ten files on performance: combinatorial
  blowups and other parse-time costs, each with the asymptotic shape and the source it's grounded
  in.
- **[`workflow/`](workflow/README.md)** — "how should I approach modeling this?" Eight files of
  authoring guidance for building a grammar well in the first place, grounded in HermitCrab's
  actual mechanics and in H. Andrew Black's FLEx parsing methodology; `workflow/sources/` carries
  Black's original text verbatim for anyone who wants it directly, rather than through this
  reference's own summaries.
- **[`texts/`](texts/README.md)** — "I also have real corpus texts, not just a grammar." Five
  files on FLExText: what it is, how to extract it, and how to get an LLM to reason over it
  correctly — including the `analysisStatus` ground-truth caveat and which AI products can
  actually run code against an uploaded file.

Two files sit at the top level: this README, and [`getting-started.md`](getting-started.md), the
human-facing walkthrough for someone who has a FieldWorks grammar and a question — send that
person `getting-started.md` directly rather than this README, which is written for the LLM to
read, not for the person to read first.

## How to use this with an LLM

Paste the **raw** URL of the relevant topic file into the chat — `raw.githubusercontent.com`, not
a `github.com/.../blob/...` page, since the raw URL returns plain Markdown with no site chrome and
fetches cleanly without JS rendering or auth. If you're not sure which topic file is relevant,
paste this README's raw URL first, or whichever of `broken/README.md`, `speed/README.md`,
`workflow/README.md`, `texts/README.md` best matches the question — an LLM that can follow links
will use it as an index; otherwise, browse the lists directly.

## What belongs here, still

This directory keeps FieldWorks' own privacy discipline: general HermitCrab engine mechanics,
FLEx/HermitCrab grammar-authoring methodology, FLExText format documentation, and synthetic or toy
examples are all fine. Real grammar or text data for any specific language project is not, and
never has been — see each subdirectory's own README for the fuller statement of that rule. Motif's
own rule is the same rule, just stated once more for anyone landing on this directory without
having read the rest of Motif's documentation: no real project data belongs in this repository, in
this directory or any other.
