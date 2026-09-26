# Website home page, Learn track and sample languages — plan

**In plain terms:** the website gets a proper front page that says what Motif is and shows it working, a
"Learn" track that teaches how to read Motif's analysis and fix a grammar in FieldWorks, and three small
downloadable sample languages to practise on. Each sample comes in a broken and a fixed version: the broken
one has a few realistic grammar mistakes planted in it, and each Learn lesson finds and fixes one. Motif does
not replace FieldWorks; it measures the grammar, explains why words fail, and the fix is made in FieldWorks.

Companion to ADR 0047 and `2026-09-26-docs-content-plan.md`. Owner decisions of 2026-09-26: the home page
shape below, "works with FieldWorks and FLExTrans" with links, three sample languages simplified from real
languages and clearly labelled as teaching grammars, planted bugs, and the conformance grammars kept for the
developer section only.

## Home page (`site/`, the landing page)

Top to bottom:

1. One sentence on what Motif does, and two actions: download (placeholder until a release exists) and the
   repository, <https://github.com/sillsdev/motif>.
2. "Works on the same FieldWorks projects FLExTrans uses", linking <https://software.sil.org/fieldworks/> and
   <https://software.sil.org/flextrans/>. Motif reads and writes FieldWorks projects; it does not integrate
   with FLExTrans directly, so the wording must not claim it does.
3. Three clip slots for the core loop — see where the grammar stands, find out why a word fails, fix and check
   before applying — using the `Walkthrough` component. Until Walkthroughs are built they show a still or a
   plain description, never a broken player.
4. A features grid, one card per window page, each linking its Guide page.
5. "How it fits": FieldWorks owns the data; Motif measures and explains; the fix happens in FieldWorks or is
   applied from Motif after review. A small diagram.
6. "Try it with a sample language", linking the Samples page.
7. "For AI agents and scripts": `motif help --all --json` and `llms.txt`.

Home page words are translatable like every other page: they live in `help/en/guide/` or the site's i18n
files, not hard-coded in a component.

## Samples

| Id | Teaches | Based on |
| --- | --- | --- |
| `sample-turkish` | suffix slots in order; vowel harmony as a phonological rule | Turkish, simplified |
| `sample-swahili` | noun-class prefixes and agreement; affix templates | Swahili, simplified |
| `sample-tagalog` | infixation and reduplication | Tagalog, simplified |

Each sample:

- is described by a checked-in, reviewable spec under `samples/<id>/` — lexicon, parts of speech, affixes and
  slots, allomorphs and environments, phonological rules, and two or three short Texts of constructed
  sentences — and a builder turns the spec into a FieldWorks project with LibLCM. No generated `.fwdata` is
  committed; the build produces it.
- has a **fixed** variant that parses every word in its Texts, and a **broken** variant that differs by three
  or four planted mistakes, each listed in `samples/<id>/bugs.md`: what is wrong, what Motif shows (which words
  fail and the reason Try a Word gives), and the exact FieldWorks steps that fix it.
- uses a few dozen genuine, common words of the language, and says on every page and in the project's own
  description: "A simplified teaching grammar for learning Motif; not a description of <language>."
- has a test, gated like `RealParserFactAttribute`, that runs the real parser over both variants and pins the
  expected failures of the broken one and zero failures of the fixed one, so the lessons cannot rot.
- is offered on the site as a download of each variant.

Never use a real person's FieldWorks project as a sample. The conformance fixtures stay in the Developers
section, described as how parser correctness is checked.

## Learn track (after the samples exist)

One lesson per planted bug, grouped by sample: open the broken sample, read the Overview and Text Coverage,
use Try a Word to find the failing word and its reason, fix it in FieldWorks, Refresh, see the number rise.
Written for linguists new to parsers: what the computer needs to be told about the language, and how Motif
shows what it was not told. Link Andy Black's published parser guidance where it helps; do not copy it.

## Order

1. In parallel: the sample builder with `sample-turkish`; the home page and site structure; small code fixes.
2. Then, in parallel: `sample-swahili` and `sample-tagalog` on the builder, and the Learn lessons.
