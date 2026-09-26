# Home page design handoff

**In plain terms:** this is the approved look of Motif's website home page, a Learn lesson page and the "How it
fits" diagram, handed from the design canvas to whoever builds the site. Build the pages to match the boards in
`boards/`; the values below are the ones to copy exactly.

Approved by the owner on 2026-09-26 from the design canvas <https://claude.ai/artifact/KnD9nsgWEjptjPBGxjUK1D>
(version 6). The boards here are copies of its artboards: open them as reference HTML, not as site code — they use
the canvas's own template syntax (`{{…}}`, `<sc-for>`), so lift the layout, values and copy, not the markup.

## Identity

Motif is an SIL Language Technology product site, styled after <https://software.sil.org/> and its product pages
(FieldWorks' in particular), not SIL corporate.

| Token | Value | Use |
| --- | --- | --- |
| Font | Noto Sans (400, 600, 700, 800; italic 400) | everything; IBM Plex Mono for code |
| Primary | `#800493` | primary buttons, active states, the marquee band, accents |
| Primary soft | `#800493` at 8% (`#80049314`), line at 25% (`#80049340`) | tinted cards, the AI card |
| Link / download | `#007a32`, hover `#005c25` | links and every Download button |
| Text | `#333333`, secondary `#555555` | body and captions (both pass 4.5:1 on white) |
| Surfaces | `#FFFFFF`; alt `#F5F5F5`; borders `#DDDDDD`, `#CCCCCC`; chips `#EEEEEE` | cards and strips |
| Dark band | `#1B1F24` with `#F7F5F0` / `#D8D2C6` text; code on `#0F1215` | clip frames, "Works with the AI you already use" |
| Motif yellow | `#FBC21D` (marquee separators); tag `#FFF4D1` on `#6B4E00` | the product's own highlight |
| Radii | 8 px buttons, 10–14 px cards | |
| Touch targets | at least 44 px | |

Logos: `sil-glyph.png` (from software.sil.org's header, 139×75) top-left, linking to <https://software.sil.org/>,
then Motif's own logo (`src/SIL.Motif.App/Assets/motif.png`) beside the word "Motif" in weight 800. Footer:
"Motif is part of SIL Language Technology."

## Home page, top to bottom (`boards/Main.dc.html`, phone: `boards/HomePhone.dc.html`)

1. Header: SIL glyph | Motif logo + name; nav Guide, Learn, Samples, Work with AI, Reference, GitHub.
2. Hero. Eyebrow "For anyone getting a FieldWorks parser to work". Headline **"Motif can help you teach a dumb
   computer your language."** Subline: "You know the linguistics. An AI consultant knows the parser, the rules and
   FieldWorks' settings. Motif gives you both the evidence: which words fail, why, and what is slow — and checks
   every fix." Actions: green **Download Motif**, outlined **See how the AI helps**. Beside it, a two-message
   exchange (You / AI consultant, reading Motif's Handoff) — illustrative until replaced with a real one from the
   Turkish sample.
3. **Marquee**, full width, purple band, white text, yellow bar separators, scrolling left continuously (60 s per
   loop), pausing on hover, and still under `prefers-reduced-motion`. The second copy of the phrases is
   `aria-hidden`. The phrases, in order, the first in weight 800:
   1. You know when a word is right. Motif helps the computer learn it too.
   2. The parser isn’t smart. It’s literal. Motif shows you what it took literally.
   3. You’ve described the morphology. Now get the computer to agree.
   4. A grammar that parses right can still parse slowly. Find the one change that fixes it.
   5. The AI doesn’t replace your judgement. It translates it into rules.
4. Works-with strip: FieldWorks and FLExTrans links.
5. "Writing a parser is computer science. You shouldn’t have to do it alone." — cards You bring the language /
   The AI brings the machinery (tinted) / Motif brings the evidence; then the dark "Works with the AI you already
   use" band with `motif help --all --json`, a `handoff` line, and `/llms.txt`.
6. "Who it’s for" — Field linguist, Translator, Native speaker, Language technologist (quote + one line each).
7. "Measure, understand, fix" — three clip slots.
8. "Faster parsing" — text, and before/after bars for the Turkish sample fed from its `expected.json`.
9. "Everything in one window" — seven page cards plus "Read the Guide".
10. "How it fits" — the diagram (`boards/HowItFits.dc.html`): FieldWorks → Motif → You + an AI consultant, with
    the dashed "Save, then Refresh" loop. Export it as `site/src/assets/home/how-it-fits.svg` in both themes.
11. "Practise on a sample language" — three cards, Turkish tagged "Includes a speed lesson".
12. Footer.

All copy stays localizable, as the site already arranges.

## Learn lesson page (`boards/Lesson.dc.html`)

Lesson sidebar with numbered lessons (current one tinted), breadcrumb and duration, title, intro, the download
callout, numbered steps with screenshot slots, a tinted "What you should see" box, previous/next links.

`boards/Taglines.dc.html` records the tagline options considered; the hero uses option 1.
