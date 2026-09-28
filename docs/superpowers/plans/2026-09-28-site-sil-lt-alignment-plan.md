# Website aligned with SIL Language Technology — plan

**In plain terms:** a visitor to Motif's website will find it laid out and coloured like the other SIL
Language Technology products (FieldWorks, FLExTrans, WeSay): the same menu of Features, Downloads, Learn,
Get Help and About, the same navy-and-blue look, and a home page built around the three things this tech
demo brings: faster parsing, visible grammar health, and the AI handoff. It matters because Motif is an
SIL Language Technology project and should read as one, and because the site may later move into SIL's
WordPress, so its pages are written now in a form that can move without being rewritten.

Two messages survive every change: Motif is a **tech demo** (D15 of
`2026-09-26-site-home-learn-samples-plan.md`, which replaces every "beta"), and all sample language data is
**synthetic**, with the full SYNTHETIC EXAMPLE disclaimer wherever a sample appears. The site stays an
internal developer build, weeks from launch and pending review. Nothing here adds a CI job, a deployment
or any public-launch step. ADR 0047 §4's CI build-and-upload is on hold under D15, and this plan does not
revive it.

**Sources.** LT claims cite the four research notes in `docs/research/sil-lt-site/`, abbreviated
**PP** ([product-pages](../../research/sil-lt-site/product-pages.md)), **SS**
([site-structure](../../research/sil-lt-site/site-structure.md)), **VS**
([visual-style](../../research/sil-lt-site/visual-style.md)) and **WP**
([wordpress-content](../../research/sil-lt-site/wordpress-content.md)), with the section named. Claims about
Motif today come from the tree at `659b9ff3` and from a fixture build (`cd site; npm run build`, 79 pages).
Where this plan conflicts with `design-home/README.md` (Identity, Beta messaging, home order), this plan
wins. That README stays as written and gets a dated note pointing here (see Integration).

## 1. Assessment

The two sites have the same kinds of content. Motif lays its content out as a documentation site, while LT
lays out each product as a small sub-site with a fixed menu. The biggest gaps are the menu and URLs, the
colours (Motif copied LT's *legacy* purple and green), downloads, and support. The deliberate differences are
the parts LT has no counterpart for, such as the generated command reference.

| Area | SIL LT pattern | Motif today | Verdict |
|---|---|---|---|
| **Scope** | Each product is a sub-site of `software.sil.org` under a shared catalog, news and support layer; the catalog is multi-product (SS §Site map; SS §Catalog, products). | One product site: home, Guide, Learn, Samples, Reference (commands, terms, controls, API), and ~60 Developer pages synchronised from `docs/*.md`, including internal plans, grills and `issues.md`. | **Gap**: the Developer set is far wider than LT's single Developer page (SS §FieldWorks product-site tree); see Q3. Having no catalog is a **deliberate difference**: Motif is one product. |
| **Page set and order** | Product menu: Features · Downloads · Learn · News · Get Help · About, each with children (SS table "Product area", FieldWorks row). | Nav: Guide · Learn · Samples · Work with AI · Reference · GitHub (`HomePrimaryNav.astro`). No Features, Downloads, Get Help or About page. | **Gap.** |
| **Navigation and URLs** | `/fieldworks/{features,download,faq,help,about,developer,news,release-notes}/`; sample projects sit under Downloads; the page tree matches the URL nesting (SS §FieldWorks product-site tree; WP §Page hierarchy). | `/guide/`, `/learn/`, `/samples/`, `/reference/*`, `/developers/*`, `/parser-correctness/`. | **Gap** for the menu pages. `/guide/` and `/reference/` are a **deliberate difference**, since LT has no generated documentation tree. |
| **Product-page anatomy** | Name + purpose tagline → "Why X?" → three feature sections with images → testimonials → Common questions → Recent news (PP §Section placement audit; PP §Shared pattern). | Beta banner → hero → marquee → works-with strip → AI section → Who it's for → clips → "Faster parsing" → Everything in one window → How it fits → samples → Before you start → footer (`index.mdx`). | **Gap**: the three offers are not the spine. Having no testimonials is a **deliberate difference** (D11 stands: no quotes, no usage numbers). The marquee and "Who it's for" are a **deliberate difference** (approved in D9). |
| **Header and footer** | SIL glyph (35 px) \| divider \| site title + subtitle "Language Technology"; product mark in the hero, not the header. Footer: four groups on `#011C2F` with cyan uppercase 13 px headings (VS §Shared structure; VS §Header, logo, and footer). | SIL glyph (139×75 source) \| Motif logo + "Motif"; footer is one credit line plus three links. | **Gap.** |
| **Styling tokens** | Current theme (`html.hv-new`): navy `#002B5C` headings, action `#004789`, links `#0056B3`, cyan `#00ADEF` hover and focus, slate text and borders; purple `#800493` and green `#007A32` survive only in the legacy Bootstrap 3 bundle (VS §Theme, palette, and actual uses). | `--home-*` tokens: purple `#800493` primary, green `#007a32` links and Download (`site.css :root`), taken in D9 from the legacy values. | **Gap**: Motif copied the wrong layer (Q1). |
| **Type** | Noto Sans; h1–h4 31/25/21/17 px in navy, weights 700/700/600/600; body 15 px/1.43, which comes from the Bootstrap 3 base bundle (VS §Type). | Noto Sans; Starlight scale; body 16 px. | Headings are a **gap**. Body size is a **deliberate difference**: 16 px suits long-form docs, and LT's 15 px is the legacy default. |
| **Early-stage presentation** | Status vocabulary in the catalog (Supported / Maintenance only / Discontinued), plus a "(Beta)" label beside the download it qualifies (PP §FieldWorks; PP §Shared pattern). | Beta banner on every page, "Download the beta", beta eyebrow, status line, FAQ and footer. | **Gap**: D15 replaces all of it with "tech demo". LT's advice is to put the caveat next to the decision it qualifies (VS item 6). |
| **Downloads** | A Download page with current version, platform, prerequisites, installer kind, release notes and older versions; sample projects as a child page (PP §Shared pattern; SS §FieldWorks product-site tree). | Hero button to `github.com/…/releases/latest`; `guide/install.md` says to ask an administrator; `/samples/` stands alone. | **Gap.** |
| **Support and community** | A Get Help hub, an FAQ, community forum or email, and a contact page that routes public help to forums (SS §About, support, licensing; PP §Shared pattern). | GitHub issues linked from the banner, FAQ and footer; no help page. | **Gap.** Pointing to GitHub issues instead of an LT forum is a **deliberate difference** until LT hosts Motif. |
| **License** | Stated per product on the product page and footer (SS §About, support, licensing; PP §Shared pattern). | The footer links `…/blob/main/LICENSE`, but **the repository has no LICENSE file**, so the link is broken. | **Gap** (Q2). |
| **Legal footer** | Terms of Use and Privacy Policy links (SS §About, support, licensing). | None. | **Deliberate difference**: LT's terms do not cover a site LT does not host. |
| **News** | Product news archive and "Recent news posts" on the home page (SS §News and versioned content). | None. | **Deliberate difference** until there is a first announcement. The content contract reserves Posts (D-C6). |
| **Search, theme** | Archive-level search only; no dark theme (SS §Search, language; VS §Theme). | Pagefind site search; light and dark themes. | **Deliberate difference**: keep both. |
| **Translation** | Locale-prefixed routes `/fr/…`, `/es/…` (SS §Search, language). | Starlight `root` locale; strings in `src/content/i18n/en.json` and `help/<lang>/`; no Crowdin config yet (ADR 0047 §5: English only before 1.0). | **Aligned in shape**: Starlight's `/fr/` prefix matches LT's. No selector is needed until a second language exists. |
| **Content model** | WordPress multisite: hierarchical Pages (parent, `menu_order`, template), Posts for dated news, menus as separate records, ordinary HTML body, no builder markup (WP §REST API structure; WP §Page hierarchy; WP §Menus, content rendering). | Generated `.md` from `help/` and `docs/`; the home page and Developers landing are MDX with imported components; Walkthrough pages are generated MDX. | **Gap**: no page metadata or menu records, and there are MDX components in authored pages. The generated reference, Walkthroughs, llms.txt and Learn lesson chrome are **deliberate differences**, isolated by D-C5. |
| **Learn lesson chrome** | — | **Defect found:** `HomeLearnSidebar.astro` and `LearnLessonContent.astro` look up `sample-${prefix}`, but sample ids are now `synthetic-*`. The fixture build renders "Learn · " and "Start with the broken  sample" with blanks. The real Learn track (`help/en/guide/learn/`) is also a generic course, not per-sample lessons. | **Gap** (P2). |
| **"Faster parsing" band** | — | **Defect found:** `HomeSpeedBars` draws Text Coverage (62.5 % → 100 %) under a speed heading. The work measure the D10 speed lesson is about (104,147 → 9,116 units) is not shown. | **Gap** (P1, P2). |

## 2. Decisions

### D-P. Page set, order and URLs

Paths are relative to the product root. On `software.sil.org` they would sit under `/motif/`, the way
FieldWorks' sit under `/fieldworks/`. The exporter adds that prefix, so no Astro `base` is set now. The site
has never been public, so a moved URL needs **no redirect** (the no-migration-before-1.0 rule applies to
URLs as well).

| Menu (order) | Page | Path | LT counterpart |
|---|---|---|---|
| — | Home | `/` | product home (PP §Product pages) |
| Features (1) | Features: the three offers in full, plus "Everything in one window" | `/features/` (`#faster-parsing`, `#grammar-health`, `#ai-handoff`) | `/fieldworks/features/` |
| Downloads (2) | Downloads | `/download/` | `/fieldworks/download/` |
| | Sample projects (generated) | `/download/samples/` (was `/samples/`) | FieldWorks "Sample Projects" under Downloads (SS table) |
| Learn (3) | Lessons (generated) | `/learn/`, `/learn/<slug>/` | FLExTrans Learn: "User Documentation", training (SS table) |
| | Guide (generated) | `/guide/…` (unchanged) | deliberate difference |
| Get Help (4) | Get Help | `/help/` | `/fieldworks/help/` |
| | Common questions | `/faq/` | `/fieldworks/faq/` |
| | Reference (generated) | `/reference/…` (unchanged) | FieldWorks "Documentation and Resources" under Get Help (SS table) |
| About (5) | About Motif | `/about/` | `/fieldworks/about/` |
| | Developer | `/developer/`; docs at `/developer/<doc>/` (was `/developers/…`); `/developer/parser-correctness/` (was `/parser-correctness/`) | `/fieldworks/developer/` |
| (reserved) | News | `/news/` — not built | `/fieldworks/news/` |

The primary menu is exactly: **Features · Downloads · Learn · Get Help · About**. "Work with AI" leaves the
top level and becomes Features ▸ AI handoff (the home page still leads with it). GitHub moves to the footer
and the Developer page, as on FieldWorks, where source links sit below the product content (PP §Section
placement audit).

### D-H. Home page, top to bottom

The home page follows LT's order: name and purpose, then "why", then three feature sections, then common
questions (PP §Shared pattern). The three offers from D15 are the spine.

1. **Hero.** Motif's product mark (the `motif.png` logo, 80 px, as LT puts the product mark in hero
   artwork, VS §Shared structure). Eyebrow: "Tech demo · New parsing technology for FieldWorks grammars".
   Headline kept from D9: "Motif can help you teach a dumb computer your language." Subline: "We have some
   new technology and some ideas about it. Motif is a tech demo of three: a parser about ten times faster,
   grammar health you can see, and an AI handoff that gives a chat model the evidence it needs." Actions:
   **Get the tech demo** (to `/download/`) and outlined **See what's in it** (to `#whats-in-it`). The status
   line under it reads "Tech demo for Windows · Free and open source · Not an officially supported SIL
   product". Keep the exchange panel and its synthetic note.
2. Marquee (unchanged phrases, restyled).
3. Works-with strip (unchanged).
4. **"What the tech demo brings"** (`id="whats-in-it"`): three trio cards, the LT `.trio-card` pattern
   (VS §Product marks, cards):
   - **About ten times faster parsing.** "PanGloss, a new parser for FieldWorks grammars, analysed words 12 to
     19 times faster than HermitCrab's rule-by-rule parsing on the grammars we measured, once it had compiled
     the grammar." Plus a "How we measured" link (see *Evidence for the ten-times claim*).
   - **Grammar health you can see.** "Because parsing is fast, Motif can measure again after every change:
     warnings, timing for each word and rule, and statistics over your own texts."
   - **An AI handoff.** "One folder with your measured results and the exact reason each word fails, ready
     to drag into any AI chat."
5. **Three feature sections**, one per card, in the same order (LT's "Analyze Texts / Describe Grammar / Word
   Collection" pattern, PP §FieldWorks):
   - `#faster-parsing`: PanGloss, the measured numbers, and the caveats in one sentence.
   - `#grammar-health`: the three clip slots (unchanged ids), then the synthetic Turkic-style sample's
     **parser work** before and after its speed fix. Read `work` from `expected.json` (broken 104,147, fixed
     9,116), never wall-clock time, which runs the other way here (D10). The bar labels keep "Synthetic
     Turkic-style sample". Then the speed-lesson link.
   - `#work-with-ai`: the existing AI section and dark band, unchanged in words.
6. Who it's for (unchanged).
7. How it fits (unchanged diagram). Its paragraph gains: "The parts that work will likely be integrated
   back into FieldWorks; Motif is a free-wheeling tech demo of what could be possible."
8. Practise on a sample language (unchanged, disclaimers and all).
9. **Common questions** (`id="common-questions"`, LT's heading, PP §FieldWorks): four cards, then "More
   questions →" to `/faq/`:
   - "What is a tech demo?" — "A working look at some new technology and some ideas about what it makes
     possible. It is not a product release, and it may change or stop at any time."
   - "Is it safe to try on my project?" — "Motif reads your project and writes nothing until you press Apply,
     and it checks each change first — but keep a FieldWorks backup, and try it on a copy before your real
     project."
   - "Will this come to FieldWorks?" — "The parts that work will likely be integrated back into FieldWorks.
     Motif is where we try them first."
   - "Something went wrong. What now?" — "Tell us on GitHub: include what you did, what you expected, and what
     happened."

"Everything in one window" moves from the home page to `/features/`. No banner appears anywhere. The tech-demo
caveat sits next to what it qualifies, at the hero action and the Download page (VS item 6), as LT does with
"FieldWorks Lite (Beta)" (PP §FieldWorks).

### D-S. Design tokens

**Adopt LT's current theme, not its legacy bundle** (Q1). Values are measured in VS §Measured styling
inventory. Names are two layers, mirroring the App's rule that views name meanings, not values:
**primitives** `--lt-*` hold raw values and may appear only in the primitives block of `site.css :root`;
**intents** `--motif-*` are the only names any other rule, component stylesheet or inline style may use.

| Primitive | Value | Source (VS) |
|---|---|---|
| `--lt-navy` | `#002B5C` | headings, `.navbar-default` |
| `--lt-nav-blue` | `#003A70` | footer gradient end, search bar |
| `--lt-action` | `#004789` | `.sil-button-primary`, `a.btn-download` |
| `--lt-link` | `#0056B3` | `a`, `a:focus-visible` |
| `--lt-cyan` | `#00ADEF` | `a:hover`, nav underline, card hover |
| `--lt-cyan-alt` | `#00A7E0` | `a.btn-secondary`, `blockquote` |
| `--lt-text` | `#333333` | `body` |
| `--lt-slate` | `#1E293B` | `.software-filter-btn` |
| `--lt-muted` | `#64748B` | `.software-os-header-label` |
| `--lt-border` / `--lt-border-strong` | `#E2E8F0` / `#CBD5E1` | `:root`, `.inner-card` |
| `--lt-surface` / `--lt-surface-soft` / `--lt-surface-alt` | `#FFFFFF` / `#F8FAFC` / `#F1F5F9` | `body`, `.announcement` |
| `--lt-notice-rail` | `#0090C8` | `.announcement:before` |
| `--lt-footer` / `--lt-footer-text` | `#011C2F` / `#8DA3B3` | `.footer-top`, `.footer-bottom` |
| `--lt-footer-gradient-from` / `-to` | `#00224A` / `#003A70` | `html.hv-new .footer-top-bg` |
| `--motif-yellow` | `#FBC21D` | Motif's own (design-home), marquee separators only |
| `--motif-tag-bg` / `--motif-tag-text` | `#FFF4D1` / `#6B4E00` | Motif's own, SYNTHETIC EXAMPLE tag (7.0:1) |

| Intent (light) | → | Notes |
|---|---|---|
| `--motif-color-text`, `-heading`, `-muted` | text, navy, muted | muted is 4.76:1 on white and 4.55:1 on soft |
| `--motif-color-link`, `-link-hover` | link, action | **Cyan is never text**: `#00ADEF` on white is 2.55:1. Hover darkens and underlines instead of LT's cyan text. |
| `--motif-color-action`, `-action-text` | action, surface | 9.28:1 |
| `--motif-color-accent`, `-focus` | cyan | non-text only: underline, card-hover border, focus ring (`0 0 0 3px` cyan at 35 %) |
| `--motif-color-nav-active-bg` | `rgb(0 173 239 / 16%)` | VS: `.menu-item-wrapper` active |
| `--motif-color-surface`, `-surface-soft`, `-surface-alt`, `-border`, `-border-strong` | as primitives | |
| `--motif-color-notice-bg`, `-notice-rail`, `-notice-border` | surface-soft, notice-rail, border | the `.announcement` pattern |
| `--motif-color-band-bg`, `-band-text`, `-band-muted` | footer, surface, footer-text | dark AI band and code |
| `--motif-color-marquee-bg`, `-marquee-text`, `-marquee-separator` | navy, surface, yellow | |
| `--motif-color-footer-bg`, `-footer-text`, `-footer-heading` | footer, footer-text, cyan | cyan on `#011C2F` is 6.81:1, so it is allowed as text here |
| `--motif-color-tag-bg`, `-tag-text` | Motif tag | |

Dark theme: the same intent names under `:root[data-theme='dark']`. Starting values: surface `#011C2F`,
soft `#0B2540`, text `#E2E8F0`, heading `#FFFFFF`, muted `#8DA3B3`, link `#6CC4F0`, action bg `#00ADEF` with
text `#011C2F`. The contrast test settles the final values, and P3 may change any value that fails it.

Also: `--motif-space-1…8` = 4/8/12/16/20/24/28/32 px (VS token table); `--motif-radius-control` 10 px,
`-button` 14 px (`.sil-button`), `-card` 16 px (`.trio-card`), `-notice` 12 px; `--motif-content-max` 1170
px; breakpoints 768/992/1200 px as media-query constants (CSS variables cannot be used in media queries).
Type: Noto Sans (the existing font link); h1–h4 31/25/21/17 px, weights 700/700/600/600, colour
`--motif-color-heading`; hero title `clamp(32px, 5vw, 60px)`/800, and `clamp(28px, 6vw, 42px)` below 768 px;
site title `clamp(16px, 6vw, 24px)`/700; footer headings 13 px, uppercase, 700, `.12em` tracking; body stays
16 px (deliberate difference). Buttons follow `.sil-button`: padding 10/25 px, 17 px text, primary filled
action, secondary white with a 2 px action border, 2 px lift on hover. Notice follows `.announcement`: soft
surface, 1 px border, 12 px radius, 6 px inset rail, padding 18/22/18/28 px. Header: glyph 35 px high, a
divider 24 px high, title "Motif", subtitle "Language Technology", active menu item tinted with a 2 px cyan
underline. **Inferred, check by eye:** the header background is white. The captured CSS leaves
`.navbar-default` navy while `hv-new` makes the title navy, and navy on navy cannot be the rendered state
(VS §Remaining questions).

**Class renames** (the markup and `site.css` change together): `.home-beta-faq` → `.home-faq`,
`.home-beta-faq__card` → `.home-faq__card`. Deleted: `.beta-banner*`, `:root.beta-banner-dismissed`,
`--beta-*`, and `--sl-nav-height`'s banner term.

### D-C. Content contract (WordPress-portable)

An authored page must survive being pasted into a WordPress Page. The body is ordinary CommonMark with no
components, and everything WordPress stores beside the body (parent, order, template) is in front matter or
a menu file. Everything that cannot move that way is generated from structured sources and regenerated by
an exporter, never hand-copied.

1. **One page per file** under `site/src/content/docs/`: `features.md`, `download.md`, `help.md`, `faq.md`,
   `about.md`, `developer.md`, `reference/index.md`, and `parser-correctness.md` with
   `slug: developer/parser-correctness`. Astro's
   glob loader honours `slug`. Authored pages are `.md`, never `.mdx`. **The home page `index.mdx` is the one
   exception**: it is layout, maps to LT's own hero-plus-trio-cards theme template (VS §Product marks,
   cards), and keeps every word in `en.json`.
2. **Front matter**, closed, validated by extending `docsSchema` in `content.config.ts`:
   `title` (WordPress title), `description` (excerpt), optional `slug` (full product-relative path),
   `menuOrder` (integer, WordPress `menu_order`), `layout` (`page` | `download` | `faq`, WordPress template).
   The parent is **derived from the path** (`download/samples` → `download`), as FieldWorks' page tree matches
   its URLs (WP §Page hierarchy), so it is never stored twice.
3. **Menus are records, not page order** (WP item 7): `site/src/chrome/en.json` holds the primary and
   footer menus (label, href, children, in array order) and the chrome strings, as one Crowdin-ready file.
   Shape:
   `{ "menus": { "primary": [{ "label", "href", "children": [{ "label", "href" }] }], "footer": [{ "heading",
   "links": [{ "label", "href" }] }] }, "strings": { "siteSubtitle", "footerCredit", "reportProblem", … } }`.
4. **Links** in authored pages are product-root-relative (`/guide/timing/`), `#anchor`, or absolute
   `https:`/`mailto:`. Help pages keep their `cmd:`/`term:`/`ui:`/`shot:` codes, which sync resolves (WP §What
   Motif content will not migrate, item 2). No relative `../` paths and no `.mdx` targets.
5. **Images**: `![alt](shot:<walkthrough>/<step>)` in help pages, or `![alt](/images/<name>.png)` in authored
   pages, with non-empty alt text (WordPress attachment alt, WP §Menus, content rendering). There is no raw
   HTML, no `:::` asides (not CommonMark), no `import`/`export`, no `{…}` expressions. A caveat is a blockquote
   (`> **Tech demo.** …`), which renders as the notice and migrates as `<blockquote>`.
6. **Won't migrate unchanged, and is isolated**: the home layout (becomes a theme template; words in
   `en.json`); generated reference, Guide, Learn, Samples and Walkthrough pages (regenerated by the exporter
   from `help/`, `samples/` and Walkthrough manifests); the Learn lesson chrome and llms.txt (Astro-only,
   dropped); `.fwbackup` binaries (linked artifacts with label, version, size and date, WP item 5). **News**,
   when it exists, is `src/content/docs/news/<yyyy-mm-dd>-<slug>.md` with a `date` field, which is a
   WordPress Post (WP item 4).
7. **Translation**: English is the source. `en.json`, `chrome/en.json`, the authored pages and `help/en/`
   are the Crowdin inputs, and each language arrives as a sibling (`fr.json`, `chrome/fr.json`,
   `src/content/docs/fr/…`, `help/fr/`), which produces LT-shaped `/fr/…` routes (SS §Search, language).
   No English string lives in a component.

### Evidence for the ten-times claim

The source is the addendum of
[`docs/research/2026-08-06-parser-timing-measured.md`](../../research/2026-08-06-parser-timing-measured.md).
It measured PanGloss's `foma` engine (FST proposes, HermitCrab confirms, which is the mode Motif runs)
against HermitCrab alone: **12.6×** per word on the fast-parsing sample grammar and **19.2×** on an Amharic
grammar, with no words abandoned, against 7 of 40 abandoned at a 5 s cap. The outcomes agreed exactly (40 of
40). **Its limits, which the copy must respect:** the figure is per word after a one-off compile (0.13–12.1
s), so HermitCrab is faster for small batches (crossover at about 90 words on one grammar and 4 on the
other); a simple grammar (Indonesian) showed no measurable gain; it compares engines inside PanGloss, **not
FieldWorks' own parser**; and it covers three grammars. So "about ten times faster" is backed as
"12 to 19 times faster than HermitCrab's rule-by-rule parsing, on the grammars we measured". **A claim
against FieldWorks' parser itself needs a new measurement** before public launch (Q4). The "How we measured"
link points at that note on GitHub (`https://github.com/sillsdev/motif/blob/main/docs/research/2026-08-06-parser-timing-measured.md`).
The synthetic sample's 104,147 → 9,116 work ratio is a **grammar fix**, not the parser, and must never be
quoted as the ten-times figure.

## 3. Work packages

The packages are file-disjoint and may be dispatched in any order once **P0** has merged. Each works in its
own worktree off `gh/integrate`, verified independently before merging. A link that points into another
package's page may 404 in one branch, which is expected; the Integration step checks every link after the
merges. Suggested merge order: P0 → P3 → P2 → P1 → P4 → P5 → Integration.

**Gates for every package:** `cd site; npm ci; npm test; npm run build` green. After P0, `npm test` builds
first. Add `./build.ps1` for P4, which edits `help/`; in a sandbox, set `MSBUILDDISABLENODEREUSE=1`,
`UseSharedCompilation=false` and `AVALONIA_TELEMETRY_OPTOUT=1` first. No package adds a CI workflow, a deploy
script or any publishing step. No package touches `src/`, `tests/`, or `help/en/{ui,commands,terms}.json`,
where another worker is changing the window's and the CLI's words.

### P0 — Foundations (sequential, small, first)

**Owns:** `site/package.json`, `site/tests/beta-messaging.test.mjs` (deleted), `site/tests/home.test.mjs`
and `site/tests/chrome.test.mjs` (new), and the top of `site/src/styles/site.css` (a new block only).

- Split `beta-messaging.test.mjs` without changing any assertion. Hero, audience, FAQ, sample-card and
  synthetic-fixture tests go to `home.test.mjs`. Banner-on-every-page, banner dismissal and the footer copy
  test go to `chrome.test.mjs`.
- Add `"pretest": "npm run build"` and remove the in-test `npm run build`, so two test files never build into
  one `dist` at once.
- Prepend to `site.css` a `/* primitives */` `:root` block with every `--lt-*` primitive, then an intents
  block defining every `--motif-*` name in D-S for light and dark. **No existing rule changes.**

**Accept:** `npm test` passes with the same assertions as before, now in two files;
`beta-messaging.test.mjs` is gone; one new test in `chrome.test.mjs` reads `site.css` and finds every D-S
intent name defined under both `:root` and `:root[data-theme='dark']`; the built home page looks unchanged.

### P1 — Home page: the tech demo and its three offers

**Owns:** `site/src/content/docs/index.mdx`; `site/src/components/home/{HomeHero, HomeSection,
HomeFeatureGrid, HomeClipSlots, HomeSpeedBars, HomeMarquee, HowItFits, HomeSampleCards,
HomeLocalizedText}.astro`; `HomeBetaFaq.astro` → renamed `HomeCommonQuestions.astro`; new
`HomeTrio.astro`; new `site/src/styles/home.css`, imported by the home components (new rules only, tokens
only); `site/src/content/i18n/en.json`; `site/tests/home.test.mjs`.

- Build D-H exactly. Every visible string goes in `en.json` under `home.*`; rename the `home.beta*` keys to
  `home.techDemo*` or `home.faq*`. No key or class contains "beta".
- The home page's links use the new paths (`/download/`, `/download/samples/#<id>`, `/features/`, `/faq/`).
  `HomeSampleCards` links `/download/samples/#<id>`.
- `HomeSpeedBars` becomes a work-measure chart. It reads `work` from `src/data/synthetic-turkic-performance.json`
  (P2 adds the field), renders nothing when `work` is missing, and its accessible label names both
  variants, the work units and "Synthetic Turkic-style sample".
- The trio card's "How we measured" link goes to the research note on GitHub.

**Accept (`home.test.mjs`, against `dist/index.html`):** it contains "Tech demo · New parsing technology for
FieldWorks grammars", ">Get the tech demo<", the D-H status line, the three card titles in order, section ids
`whats-in-it`, `faster-parsing`, `grammar-health`, `work-with-ai`, `common-questions` in that order, the
four questions, and the safety sentence "writes nothing until you press Apply" with "keep a FieldWorks
backup". The trio card says "12 to 19 times" and "on the grammars we measured", and links the research
note. Every sample card shows SYNTHETIC EXAMPLE and its own disclaimer (existing assertions kept). The home
page, `en.json` and `home.css` match no `/\bbeta\b/i`. No `home.css` rule contains a colour literal.
**Visible:** at 375 px wide, no horizontal scroll and the hero actions stack; the audience situations are
still without quotation marks.

### P2 — Chrome, navigation and URLs

**Owns:** `site/astro.config.mjs`; `site/src/components/home/{HomeSiteTitle, HomePrimaryNav, HomeFooter,
HomeLearnSidebar, LearnLessonContent, HomePageTitle}.astro`; deletes `BetaBanner.astro`, `BetaHeader.astro`,
`site/src/scripts/beta-banner.mjs`; new `site/src/chrome/en.json`; new `site/src/styles/chrome.css`, imported
by the chrome components (tokens only); `site/scripts/sync-core.mjs`, `site/scripts/sync.mjs`;
`site/tests/chrome.test.mjs`, `site/tests/sync.test.mjs`.

- Remove the `Header` override and the banner. Header: SIL glyph (35 px, linking to software.sil.org) |
  divider | "Motif" + subtitle "Language Technology", both from `chrome/en.json`. Primary menu from
  `chrome/en.json` with the D-P children as disclosure menus (keyboard-operable `<details>` or buttons with
  `aria-expanded`), and reachable in Starlight's mobile menu.
- Footer, four groups from `chrome/en.json`: **Motif** (Features, Downloads, Learn, Get Help, About);
  **Language Technology** (Software Products, Software & Font News, About Us, General Software Support, all
  `https://software.sil.org/…`, as LT's footer, SS §Site map); **Related software** (FieldWorks, FLExTrans);
  **Contact & Support** (Report a problem → GitHub issues; Source code → GitHub). Credit line: "Motif is a
  tech demo within SIL Language Technology." No license link until Q2 is answered, and no LT terms or privacy
  links.
- `astro.config.mjs`: the sidebar follows the menu order (Features, Downloads ▸ Sample projects, Learn ▸
  Lessons, Guide, Get Help ▸ Help, Common questions, Reference, About ▸ About Motif, Developer). Add
  `<meta name="robots" content="noindex, nofollow">` to `head` while the site is an internal build.
- `sync-core.mjs`: Samples are generated at `download/samples/`; developer docs at `developer/` (the Q3
  allowlist, or all of `docs/*.md` if Q3 says keep everything); `samples.json` entries gain `learnPrefix`;
  the performance JSON gains `work` and `steps`, each validated as a non-negative integer.
- Fix the lesson chrome. A lesson resolves its sample through `learnPrefix` (`turkish-plural-harmony` →
  `synthetic-turkic`); a lesson with no sample (the generic course in `help/en/guide/learn/`) shows the
  heading "Learn" and no download callout, never a blank language name.

**Accept:** `chrome.test.mjs` checks every built page: no `data-beta-banner`; `class="home-brand` present;
"Language Technology" subtitle present; primary menu labels exactly `Features, Downloads, Learn, Get Help,
About` in order; the four footer headings; the credit line; `name="robots" content="noindex`; no
`/\bbeta\b/i` in chrome output. `dist/learn/turkish-plural-harmony/index.html` names "Turkish" in the
sidebar heading and the download callout (fixture build); a generic lesson page contains no "Learn · <" and
no "broken  sample". `sync.test.mjs` asserts `download/samples/index.md`, `developer/<doc>.md`, `learnPrefix`,
and rejects a non-integer `work`. **Visible:** at 375 px, the menu opens from the mobile menu and every
item is reachable by keyboard.

### P3 — SIL LT styling

**Owns:** `site/src/styles/site.css` (everything except P0's block, which P3 may now edit); new
`site/tests/tokens.test.mjs`.

- Re-point every existing rule from `--home-*` and literals to D-S intents; delete `--home-*` and all beta
  rules and variables (D-S list); apply the class renames; set the Starlight variables (`--sl-color-accent`
  → action, `--sl-color-accent-low` → nav-active tint, `--sl-color-text-accent` → link, headings navy) in
  both themes; the heading scale, buttons, cards (16 px trio, 10 px catalog-style sample cards), notice
  styling for `.sl-markdown-content blockquote`, and `.download`-style tables (13 px uppercase headers, 12/16
  px cells, VS §Product marks, cards).
- The marquee band becomes navy with yellow separators; the dark AI band uses the footer navy.

**Accept (`tokens.test.mjs`):** (1) no `#hex`, `rgb(`, `hsl(` or named colour in `site/src/**/*.{css,astro,mdx,md}`
outside the primitives block (`src/assets/` excluded); (2) every `var(--motif-*)` and `var(--lt-*)` used
anywhere is defined; `--lt-*` appears only inside `site.css`; (3) in both themes, each text intent against
its surface is ≥ 4.5:1 and accent/focus against surface ≥ 3:1, computed from the parsed values; (4) no
selector or variable matches `/beta/i`. **Visible:** headings navy, links `#0056B3`, primary buttons
`#004789`, focus ring cyan, footer `#011C2F`, all checked by eye against `software.sil.org/fieldworks/` in
both themes and at 375 px.

### P4 — Site pages and the content contract

**Owns:** `site/src/content/docs/{features,download,help,faq,about,developer}.md` (new);
`parser-correctness.md` (gains `slug`, `menuOrder`, `layout`); `developers.mdx` (deleted);
`site/src/content/docs/reference/index.md` (front matter, and its links made root-relative);
`site/src/content.config.ts`; new
`site/tests/content-contract.test.mjs`; `help/en/guide/what-is-motif.md` and `help/en/guide/install.md`.

- Write the pages per D-P and D-C. Every statement about the window or CLI must match the code.
  - **Download**: a tech-demo notice blockquote first; "Current version" states plainly that there is no
    public download yet and that reviewers build from source (link the README); requirements as the README
    states them (Windows, .NET 10 to build, the `pangloss` executable to parse); the two safety lines; Sample
    projects → `/download/samples/`; release notes → GitHub Releases. Model the version row (label, version,
    platform, format, size, date, notes, checksum) as a table, so a future build only fills it in (WP item 5).
  - **Features**: the three offers in depth (same claims and evidence link as P1), then "Everything in one
    window", taking over the seven cards' titles and links from the home page.
  - **Get Help**: report a problem (GitHub issues; what to include; link `/guide/when-something-goes-wrong/`),
    documentation routes (Learn, Guide, Reference, `/llms.txt` for agents), and a line saying Motif has no
    email or forum support as a tech demo.
  - **Common questions**: the four home questions **verbatim**, plus "Which languages are in the samples?"
    (synthetic, with the disclaimer), "Does Motif change my FieldWorks project?", "Which parser does Motif
    use?". Each question is an `##` heading.
  - **About**: what a tech demo is; the three offers; "will likely be integrated back into FieldWorks";
    within SIL Language Technology; the synthetic-data policy; the license line once Q2 is answered.
  - **Developer**: source on GitHub, build from source, links to the allowlisted developer docs and
    `/developer/parser-correctness/`, the command API, `/llms.txt`.
- `what-is-motif.md` gains one opening sentence: Motif is a tech demo of new parsing technology for
  FieldWorks grammars, and what works will likely move into FieldWorks. `install.md` is rewritten to state
  what exists: a Windows tech demo with no public installer yet, built from source, and PanGloss needed to
  parse.

**Accept (`content-contract.test.mjs`):** for every authored page (the list above; `index.mdx` exempt by
name): `.md` extension; front matter keys ⊆ {`title`, `description`, `slug`, `menuOrder`, `layout`,
`sidebar`}; `menuOrder` an integer and `layout` in the enum; outside fenced code, the body has no line
starting `import ` or `export `, no `:::`, no `{`, and no raw HTML tag (CommonMark autolinks `<https://…>` are
allowed); every link root-relative, `#…`, `https:` or `mailto:`; every image has non-empty
alt; `/\bbeta\b/i` absent. The fixture build emits `dist/{features,download,help,faq,about,developer}/index.html`
and `dist/developer/parser-correctness/index.html`, and not `dist/parser-correctness/`. The Download page
contains the blockquote notice, "writes nothing until you press Apply" and "keep a FieldWorks backup". The FAQ
contains each of the four home questions. `./build.ps1` passes, including the help gate.

### P5 — WordPress export check

**Owns:** new `site/scripts/export-wordpress.mjs`, new `site/tests/export-wordpress.test.mjs`, and the one
line `/site/wp-export/` in the root `.gitignore`.

- After `npm run build`, `node scripts/export-wordpress.mjs` writes `site/wp-export/pages.json` (per page:
  `path`, `slug` = last segment, `parent` = parent path, `menu_order`, `template` = `layout`, `title`,
  `excerpt`, `content` = the HTML of `.sl-markdown-content` from `dist/<path>/index.html`, `source` = the
  file it came from, `generated` true/false), `menus.json` (from `chrome/en.json`), and `media.json` (every
  image and `.fwbackup` link with alt or label). Internal links are rewritten to `<base>/<path>` from
  `--base` (default `/motif`). It **writes files only**: no network, no credentials, no upload.
- It exits non-zero, naming the file, when an authored page breaks D-C, a link resolves to no built page,
  or an image lacks alt.

**Accept:** the test runs the exporter over a temp fixture (`dist` fragment + content + chrome file) and
asserts the field mapping, the parent derived from `download/samples`, the link rewrite under `--base /motif`,
and a non-zero exit for an `.mdx` authored page, a dangling link, and an alt-less image. Run against the real
build, it exits 0 and lists every D-P page.

### Integration (after all merges; the lead or one Luna worker)

**Owns:** new `site/tests/site-integrity.test.mjs`; orphan removals in `en.json` and `site.css`;
`docs/superpowers/plans/design-home/README.md` (one appended dated note only).

- The test checks that every internal `href`/`src` in `dist` resolves to a built file; no built page and no
  file under `site/src` or `site/tests` matches `/\bbeta\b/i` (the D15 sweep); every `en.json` key is
  referenced by some component; the four home questions appear verbatim in `faq.md`; every sample card and
  the samples page carry SYNTHETIC EXAMPLE.
- Delete orphan keys and dead CSS rules the test or a class search finds.
- Append to `design-home/README.md`: "**Superseded 2026-09-28** for identity, beta messaging and home order
  by `2026-09-28-site-sil-lt-alignment-plan.md`."
- Build once more with a real help export (`./build.ps1`, `motif help --all --json` to `bin\help-export.json`,
  `MOTIF_HELP_EXPORT` set), because the default build uses `site/fixtures/help`, not `help/en`.

## 4. Questions for the owner

Each question blocks the package named. The recommendation is what that package does if you say "go".

1. **Palette (blocks P3, and P0's values).** D9 and the design canvas took purple `#800493` and green
   `#007A32` as software.sil.org's identity. The measured CSS shows those are leftovers in the old Bootstrap 3
   bundle; the current LT theme is navy `#002B5C`, blue `#004789`/`#0056B3` and cyan `#00ADEF` (VS §Theme,
   palette). Switch to the current theme? **Recommend yes**, with Motif yellow kept only for the marquee
   separators and the SYNTHETIC EXAMPLE tag.
2. **License (blocks the license line in P2's footer and P4's About page).** The repository has no LICENSE
   file, so today's footer link is broken. LT names a license on every product page (SS §About, support,
   licensing). Which license? **Recommend MIT**, as FLExTrans, SayMore, WeSay and Phonology Assistant use,
   unless a dependency requires otherwise. Until you answer, P2 and P4 show no license link.
3. **Developer pages (blocks P2's sync change).** The site publishes all ~60 `docs/*.md` files, including
   plans, grills and `issues.md`; LT products have one Developer page. Publish only an allowlist?
   **Recommend yes:** `architecture.md`, `cli-api.md`, `change-set-contract.md`, `known-issues.md`, plus the
   authored parser-correctness page; everything else stays on GitHub, like the ADRs.
4. **The ten-times claim (blocks P1's and P4's wording).** The evidence compares PanGloss's engines, not
   PanGloss against FieldWorks' own parser. **Recommend** the qualified wording in D-H ("12 to 19 times
   faster than HermitCrab's rule-by-rule parsing, on the grammars we measured") for this internal build, and
   a measurement against FieldWorks' parser before any public launch.
