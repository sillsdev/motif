# A Working Grammar for Every Language — the pitch

A pitch for the whole HermitCrab → PanGloss → Motif effort, for SIL leadership, the FieldWorks and
HermitCrab teams, and prospective partners. It presents the work as a continuation of forty years of SIL
parsing, with Motif as a working demonstration whose Phase 1 tools move into FieldWorks, and sets out a
two-phase plan (Phase 1 for Bible translation, Phase 2 for every language community). The look is
SIL Global's: landscape letter pages, SIL blues, and SIL's own open fonts, with the cover showing one
word in several scripts, each in its SIL font.

There are two decks. **`pitch.md`** is the full paper, *A Working Grammar for Every Language*.
**`pitch-short.md`** is *Motif in Brief*: eight pages on what the Motif demonstration shows today and where it goes.
Everything else is formatting. Edit the words there; the build turns them into a PDF and an HTML page.

## Build

```powershell
./build.ps1                # both decks, PDF and HTML: A-Working-Grammar-for-Every-Language, Motif-in-Brief
./build.ps1 -Format pdf    # one format: pdf, html or pptx
./build.ps1 -Source pitch-short.md   # one deck only
./build.ps1 -Watch         # rebuild the full deck's HTML on every save (diagrams fall back to system fonts)
```

Needs Node.js and Chrome, Edge or Chromium. [Marp CLI](https://github.com/marp-team/marp-cli) is
fetched by `npx` on first run.

## Files

| Path | What it is |
|---|---|
| `pitch.md` | The full paper, one page per `---`. The `<!-- _class: ... -->` line picks a layout |
| `pitch-short.md` | *Motif in Brief*, the short deck, in the same layouts |
| `theme/pitch.css` | Colours, fonts and page layouts; retheme through the `:root` variables |
| `art/` | Chapter-opener maps (SVG), one region of the world each, made by `maps.py` from Natural Earth |
| `maps.py` | Rewrites the chapter maps; each chapter's region, colours and framing are in its `regions` table |
| `diagrams/` | The four hand-written diagrams (SVG), inlined at build time so they use the page's fonts |
| `figures/` | The page figures: Claude Design artboards (`.dc.html`), each drawn at its slot's exact size |
| `research/` | The sourced fact sheets the pitch draws on |
| `build.ps1` | Builds both decks as PDF, HTML or PPTX into `dist/` |

## Page layouts

| Class | Use |
|---|---|
| `cover` | Title page; `<div class="scripts">` holds the multiscript strip, `######` is the byline |
| `toc` | Contents; `*n*` at the end of an item is its page number |
| `chapter c1` … `c6` | Chapter opener on white, its map tinted in that colour; `######` is the chapter pill |
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

## Figures

Most pages carry one figure from `figures/`. Each is a Claude Design artboard, kept verbatim from the canvas
[Pitch image options](https://claude.ai/artifact/EjB9HVrNXq69gK6djdRjra), page *Deck figures (in use)*, where
`figures/NAME.dc.html` is the artboard `fig-NAME`. The canvas's other pages hold the options that were not chosen. One exception: `figures/flourishing.dc.html`
was written by hand in the same artboard shape and is not on the canvas yet.

A figure is a line of its own: `![](figures/tools.dc.html)`, with `wide` to span both columns of a `cols` page
or `narrow` on a full-width page. `build.ps1` inlines the artboard's markup unscaled, so it is drawn at the size
its slot has: 436 px wide in one column of a `cols` page, 912 px across a page, 711 px for `narrow`. Its height
must fit the space the page has left; a page that overflows loses its last lines off the bottom or pushes a
column off the right edge, so look at every page after changing a figure or the words around it.

To change a figure, edit its artboard on the canvas, copy the artboard's file over `figures/NAME.dc.html`, and
rebuild. A new artboard needs its `<head>`, `<helmet>` and `<x-dc>` shape unchanged: the build takes only the
markup between `</helmet>` and `</x-dc>`, and no `{{holes}}`, because nothing fills them at build time.
