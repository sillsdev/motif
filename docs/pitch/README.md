# A Working Grammar for Every Language — the pitch

A pitch for the whole HermitCrab → PanGloss → Motif effort, for SIL leadership and prospective
partners: why a fast, buildable, portable grammar matters, and a two-phase plan to get there
(Phase 1 for Bible translation, Phase 2 for every language community). The page layout follows
Anthropic's long-form guides: landscape letter pages, a two-column serif body and coloured chapter
openers.

**`pitch.md` is the content.** Everything else is formatting. Edit the words there; the build turns
them into a PDF and an HTML page.

## Build

```powershell
./build.ps1                # dist/A-Working-Grammar-for-Every-Language.pdf and .html
./build.ps1 -Format pdf    # one format: pdf, html or pptx
./build.ps1 -Watch         # rebuild HTML on every save (diagrams fall back to system fonts)
```

Needs Node.js and Chrome, Edge or Chromium. [Marp CLI](https://github.com/marp-team/marp-cli) is
fetched by `npx` on first run.

## Files

| Path | What it is |
|---|---|
| `pitch.md` | All the words, one page per `---`. The `<!-- _class: ... -->` line picks a layout |
| `theme/pitch.css` | Colours, fonts and page layouts; retheme through the `:root` variables |
| `art/` | Cover and chapter illustrations (SVG) |
| `diagrams/` | The four diagrams (SVG), inlined at build time so they use the page's fonts |
| `research/` | The sourced fact sheets the pitch draws on |
| `build.ps1` | Builds PDF, HTML or PPTX into `dist/` |

## Page layouts

| Class | Use |
|---|---|
| `cover` | Title page; the image is the illustration, `######` is the byline |
| `toc` | Contents; `*n*` at the end of an item is its page number |
| `chapter c1` … `c6` | Chapter opener in that colour; `######` is the chapter pill |
| `cols` | Two columns; `<div class="break"></div>` starts the second |
| `cards` / `cards2` | Each list item becomes a card, three or two per row |
| `axes` | A comparison table with tinted Today / Phase 1 / Phase 2 columns |
| `dense` | Smaller text for a busy page |

`> **Label** text` is a tinted callout; `> > **Label** text` a dark one. `header:` sets the chapter
pill on the pages that follow. A diagram is a line of its own: `![](diagrams/loop.svg)`, or
`![narrow](diagrams/axes.svg)` for a narrower one.

## Diagrams

The diagrams were tried out on a Claude Design canvas and then written as plain SVG with the same
geometry. To change one, edit its SVG in `diagrams/` directly.
