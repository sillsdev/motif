# A Working Grammar for Every Language — the pitch

A pitch for the whole HermitCrab → PanGloss → Motif effort, for SIL leadership, the FieldWorks and
HermitCrab teams, and prospective partners. It presents the work as a continuation of forty years of SIL
parsing, with Motif as a proving ground whose Phase 1 tools move into FieldWorks, and sets out a
two-phase plan (Phase 1 for Bible translation, Phase 2 for every language community). The look is
SIL Global's: landscape letter pages, SIL blues, and SIL's own open fonts, with the cover showing one
word in several scripts, each in its SIL font.

There are two decks. **`pitch.md`** is the full paper, *A Working Grammar for Every Language*.
**`pitch-short.md`** is *Motif in Brief*: eight pages on what Motif is today and where it goes.
Everything else is formatting. Edit the words there; the build turns them into a PDF and an HTML page.

## Build

```powershell
./build.ps1                # dist/A-Working-Grammar-for-Every-Language.pdf and .html
./build.ps1 -Format pdf    # one format: pdf, html or pptx
./build.ps1 -Watch         # rebuild HTML on every save (diagrams fall back to system fonts)
./build.ps1 -Source pitch-short.md   # the short deck: dist/Motif-in-Brief.pdf and .html
```

Needs Node.js and Chrome, Edge or Chromium. [Marp CLI](https://github.com/marp-team/marp-cli) is
fetched by `npx` on first run.

## Files

| Path | What it is |
|---|---|
| `pitch.md` | The full paper, one page per `---`. The `<!-- _class: ... -->` line picks a layout |
| `pitch-short.md` | *Motif in Brief*, the short deck, in the same layouts |
| `theme/pitch.css` | Colours, fonts and page layouts; retheme through the `:root` variables |
| `art/` | Chapter illustrations (SVG) |
| `diagrams/` | The four diagrams (SVG), inlined at build time so they use the page's fonts |
| `research/` | The sourced fact sheets the pitch draws on |
| `build.ps1` | Builds PDF, HTML or PPTX into `dist/` |

## Page layouts

| Class | Use |
|---|---|
| `cover` | Title page; `<div class="scripts">` holds the multiscript strip, `######` is the byline |
| `toc` | Contents; `*n*` at the end of an item is its page number |
| `chapter c1` … `c6` | Chapter opener in that colour; `######` is the chapter pill |
| `cols` | Two columns; `<div class="break"></div>` starts the second |
| `cards` / `cards2` | Each list item becomes a card, three or two per row |
| `axes` | A comparison table with tinted Today / Phase 1 / Phase 2 columns |
| `dense` | Smaller text for a busy page |

`> **Label** text` is a tinted callout; `> > **Label** text` a dark one. `header:` sets the chapter
pill on the pages that follow. On the cover, each `<span lang="…">` in the scripts strip picks its SIL
font by language; have a speaker check any word added there. A diagram is a line of its own: `![](diagrams/loop.svg)`, or
`![narrow](diagrams/axes.svg)` for a narrower one.

## Diagrams

The diagrams were tried out on a Claude Design canvas and then written as plain SVG with the same
geometry. To change one, edit its SVG in `diagrams/` directly.
