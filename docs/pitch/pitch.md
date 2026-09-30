---
marp: true
theme: pitch
paginate: true
header: ''
title: A Working Grammar for Every Language
description: HermitCrab, PanGloss and Motif — carrying forty years of SIL parsing work the rest of the way
---

<!-- _class: cover -->
<!-- _paginate: false -->

# A Working Grammar for Every Language

Carrying forty years of SIL's parsing work into the hands of every translation team, and beyond.

<div class="scripts"><span lang="am">ቋንቋ</span><span lang="ar" dir="rtl">لغة</span><span lang="hi">भाषा</span><span lang="my">ဘာသာစကား</span><span lang="en">language</span></div>

###### SIL Global · HermitCrab, PanGloss and Motif · September 2026

---

<!-- _class: toc -->
<!-- _paginate: false -->

# Contents

1. Introduction *3*
2. Groundwork and the next step *5*
3. If all five moved forward *10*
4. Phase 1: Bible translation *15*
5. Phase 2: Every language community *23*
6. Risks, costs and next steps *30*
7. Glossary and sources *34*

---

<!-- _class: cols -->

# Introduction

A **grammar**, in the sense this paper uses, is a description of how the words of a language are built: its roots, its prefixes and suffixes, the order they come in, and the sound changes that happen when they meet. A **parser** runs that description backwards. Give it a word, and it tells you what the word is made of and what each piece means.

SIL has worked toward this for nearly forty years, from AMPLE and PC-KIMMO to Mike Maxwell's **HermitCrab**, which Damien Daspit built into FieldWorks and which John Maxwell and others continue to develop. It can model nearly any language on earth, and grammars are being built on it now, in FLExTrans projects and in parser workshops from Manila to Nairobi.

What those grammars need next is tooling that keeps pace as they grow: faster re-parsing, a clear view of what the grammar is doing, and ways to put a finished grammar to work beyond FieldWorks. This paper sets out a two-phase plan to provide it, building on HermitCrab rather than beside it.

![](figures/tools.dc.html)

<div class="break"></div>

**What you'll find here:**

- Forty years of groundwork, and the five things that decide how far a grammar reaches
- **Phase 1**, built for Bible translation inside SIL, which serves translation from day one
- **Phase 2**, built for every language community, which needs partners

**Who this is for:** SIL leadership deciding whether to fund Phase 1; language technology and Bible translation leaders who would deploy it; the FieldWorks and HermitCrab teams it builds on; and prospective partners for Phase 2.

**Two ways to read this**
In a hurry? Read the next page, then Chapter 3 (*Phase 1*) and Chapter 5 (*Risks, costs and next steps*). A shorter companion, *Motif in Brief*, covers what exists today in eight pages.

> **The one idea** A grammar is software for a language. Once it is fast, easy to build and runs everywhere, every tool that handles text in that language gets better at once.

---

<!-- _class: axes -->
<!-- _header: 'In one page' -->

# The pitch in one page

| | Today | Phase 1 — Bible translation | Phase 2 — Every language community |
|---|---|---|---|
| **Speed** | Slows as a grammar grows: minutes to hours for a text | **50× faster (target)**: PanGloss plus algorithm improvements, shared with HermitCrab's own | **1,000× faster (target)**: grammars compiled to finite-state machines |
| **Easy to build** | A trace per word; needs specialist support and years | Statistics, timing, progress and grammar-health diagnostics, with AI assistance, in FieldWorks | AI proposes grammar changes; statistical harvesting of the lexicon |
| **Deployment** | FieldWorks and FLExTrans | **FieldWorks** and **Paratext**: back-translations and spelling checks where translators work | Firefox, LibreOffice, Wikipedia, Keyman prediction, literacy apps |
| **Languages using it** | A committed few, many more in progress | **Hundreds**: a clear win for every Bible translation project | A **shared library** of grammars anyone can use |
| **Completeness** | Mostly Scripture | Still Scripture | Coverage checks and report cards for news, health, literacy and school |

> > **The invitation** Fund Phase 1 as a Bible translation tool, built together with the FieldWorks and HermitCrab teams. It is justified on that use alone. Treat Phase 2 as the long-term hope, and start the partner conversations now, because it needs them.

---

<!-- _class: chapter c1 -->
<!-- header: Chapter 1 -->
<!-- _paginate: false -->

![](art/chapter-1.svg)

###### Chapter 1

# Groundwork and the next step

---

<!-- _class: cols -->

# What a grammar can do

## What a parser does

In much of the world a single word is a whole sentence. The Swahili word *hawatanipenda* means "they will not love me":

<div class="word"><span><b>ha</b><i>not</i></span><span><b>wa</b><i>they</i></span><span><b>ta</b><i>will</i></span><span><b>ni</b><i>me</i></span><span class="root"><b>pend</b><i>love</i></span><span><b>a</b><i>(ending)</i></span></div>

A verb can have thousands of forms; one study counts 2,253 possible forms of a single Finnish noun. A word list cannot hold them all, so spell checkers for these languages flag correct words and miss wrong ones. A parser knows the pieces and the rules, and works out any word from them.

## HermitCrab

HermitCrab is SIL's rule-based morphological parser. It models what linguists describe: roots and affixes, templates of slots, and sound changes at the boundaries. It ships in **FieldWorks Language Explorer (FLEx)** alongside the older XAMPLE parser, and **FLExTrans** builds machine translation on FLEx grammars. In principle it can parse nearly every word in every language. The work of this paper is helping it do so in practice.

<div class="break"></div>

## Why a grammar is worth having

A working grammar is not an end in itself. It is an engine that other tools switch on:

- **Spell checking** that understands words it has never seen
- **Word prediction** on phone keyboards, which matters most where typing is hardest
- **Glossing and back-translation**, so a consultant can read a draft they don't speak
- **Search** that finds every form of a word, not just the one typed
- **AI**, which is weakest exactly where text is scarce, and gains most from structured knowledge of a language

![](figures/forms.dc.html)

> **The opportunity** Every one of these tools depends on the same thing: a working grammar. For most of the world's languages that grammar is still being built, or not yet begun.

---

<!-- _class: cols dense -->

# Forty years of groundwork

Every grammar in FieldWorks today stands on work that began in the 1980s: David Weber, Andy Black and Stephen McConnel's AMPLE; Evan Antworth's PC-KIMMO; Mike Maxwell's Hermit Crab; the FLEx parsing Andy Black and Gary Simons described; the HermitCrab Damien Daspit built into FieldWorks; Ron Lockwood's FLExTrans; and HermitCrab's continuing life in SIL.Machine with Damien Daspit and John Maxwell.

![wide](figures/timeline.dc.html)

### Grammars being built now

- **FLExTrans** helps draft Scripture for language communities in the Philippines and Peru.
- **Parser workshops** train linguists to build grammars for their own languages: Manila in 2025, Nairobi in 2026.
- **Many more grammars** sit in FieldWorks projects at every stage. A few are mature; many are "getting there", built patiently over years.

<div class="break"></div>

### Work continuing today

HermitCrab's maintainers are making it faster. Algorithm work in SIL.Machine is delivering 5–10× speedups, and FieldWorks can now limit runaway parses and parse only the words that still need an analysis.

> **Picking up the baton** This paper does not propose starting over. It proposes carrying this work the rest of the way: keeping every grammar already built, and giving the people building them tools that keep pace as their grammars grow.

---

<!-- _class: cards -->

# Where grammar-building stands

Five things decide how far a grammar can reach. Today's tools have carried grammars a long way on each; the next step on each is within reach.

- **1 · Speed** As a grammar grows, some combinations of rules become expensive to search. On one Amharic grammar a list of 7,000 words takes about half an hour, and one word in six hits a five-second limit. When re-parsing is slow it happens less often, and the grammar improves more slowly.
- **2 · Easy to build** Try a Word shows how the parser handled one word, but nothing shows, across a whole text, where the time goes or which rules never fire. Building a grammar still takes specialist support and patient years.
- **3 · Deployment** Grammars serve FieldWorks and FLExTrans well. The places people type, read and publish, from Paratext to word processors, browsers and keyboards, cannot yet use them.
- **4 · Languages using it** A committed community of grammar builders, a handful of mature grammars and many more in progress. Ethnologue counts 7,159 living languages.
- **5 · Completeness** Grammars are mostly built and tested against Scripture, the text translation teams work with most, rather than the everyday language of news, health or school.

> **Why this matters** These five reinforce each other. Faster parsing makes grammars easier to build; easier grammars mean more languages; more languages give other tools a reason to use them; wider use gives a reason to make grammars complete. **Move one and the others start to move.**

![narrow](figures/reinforce.dc.html)

---

<!-- _class: cols -->

# The foundation is sound; the next step is tooling

HermitCrab's model of language is sound, and decades of grammars show it. What grammar builders need next lies in the engineering and tooling around it, which is exactly the part that has become cheaper to build.

### Slow in places because of how it searches

HermitCrab tries every way a word *could* have been built, running rules backwards, and checks each against the lexicon. As a grammar grows, much of that work is repeated or cannot succeed. Better algorithms can skip it without changing a single answer, and HermitCrab's maintainers are already showing it, with 5–10× speedups in SIL.Machine.

### Hard to build because so much is hidden

Try a Word shows the parser's reasoning for one word. What is missing is the view across a whole text: which rules are expensive, which never fire, which let in forms the language does not have. Without it, improving a grammar relies on long experience reading traces.

<div class="break"></div>

![](figures/search.dc.html)


### In FieldWorks because of its runtime

HermitCrab is a C# library that runs inside FieldWorks, reading FieldWorks' own data. That is the right home for building a grammar, but Paratext, a browser or a keyboard app cannot load it.

### Few languages, and Scripture first, because of the other three

A grammar is a long commitment, and today it pays back in one application. FieldWorks serves more than 1,300 language communities; far fewer have taken a grammar to the point where the parser does daily work.

> **What changed** Three things. HermitCrab's maintainers have shown how much speed better algorithms can find. **PanGloss**, a faithful port of HermitCrab that runs anywhere, is under way. And AI tools have made this kind of software cheaper to build, and can help a linguist read a trace and propose a fix. **What remains is careful work, and it can now be done faster than before.**

---

<!-- _class: chapter c2 -->
<!-- header: Chapter 2 -->
<!-- _paginate: false -->

![](art/chapter-2.svg)

###### Chapter 2

# If all five moved forward

---

<!-- _class: cards -->

# If all five moved forward

- **1 · Instant** Parsing is faster than typing. A whole Bible re-parses in the time it takes to save, and a keyboard can analyse each word as it is typed.
- **2 · Weeks, not years** Someone who knows their language well can make steady progress on a grammar in weeks, with statistics, progress bars and an AI assistant that explains what went wrong.
- **3 · Everywhere** In Paratext, Firefox and LibreOffice, on language-analysis websites, in spelling prediction and correction, and in instant back-translations for people and for AI.
- **4 · Every language** Every language community that wants a working grammar can have one.
- **5 · Full content** Grammars that handle news, health information, literacy materials and primary school textbooks, not only Scripture.

![narrow](figures/seconds.dc.html)

> > **The picture** A grammar becomes a gift a community gives to every tool that serves its language: built once, maintained by that community, and used wherever the language is written.

---

<!-- _class: cols -->

# A day in that world

### A translator in Paratext

She finishes a draft of Mark 4. Before she saves, every word has been parsed. Three are flagged: two are typos, one is a new word the grammar has not seen. A literal back-translation appears beside her text, word by word, for the consultant who arrives next month and does not speak the language.

### A primary school teacher

A teacher prepares a reading book on the class laptop. LibreOffice underlines words that are misspelled, and not the correct ones that happen to be long. His pupils practise spelling on a phone app built from the same grammar.

![](figures/interlinear.dc.html)

<div class="break"></div>

### A Wikipedia editor

An editor writes a health article in her own language. The browser suggests corrections, and the keyboard predicts the next word from forms it has never seen, because it builds them from their parts.

### An AI system

A translation model working in a low-resource language asks the grammar what an unfamiliar word means, and gets its root and a gloss for each piece. The model's weakest languages now come with a precise, human-checked description.

> **Within reach** Each scenario uses tools that exist today: Paratext, LibreOffice, Firefox, Keyman, Wikipedia. What is missing is a fast, portable grammar for them to call.

---

<!-- _class: cols -->

# Why now

### The foundation exists

HermitCrab, and the grammars built with it over years, give a proven model and real data to work from. Its maintainers are making it faster.

### The engine exists

**PanGloss** is a port of HermitCrab to Rust. It follows the same algorithms, with some speedups of its own, and dozens of grammars that trace the contours of the parser hold it to the original's answers. It reads FieldWorks projects directly and builds as a native program, a C library or WebAssembly for the browser.

### A proving ground exists

**Motif** is a desktop application and command-line tool that shows a grammar author what their grammar is doing: which words parse, which fail, where the time goes, and what to fix. It is where these ideas are tried on real grammars before they move into FieldWorks.

![](figures/stack.dc.html)

<div class="break"></div>

### The data exists

Translation work is in progress in 4,457 languages, and many teams keep lexicons and interlinear texts in FieldWorks: much of a grammar's raw material.

### Building software has become cheaper

PanGloss and Motif were begun in the summer of 2026 by a small team working with AI coding agents. The constraint is no longer engineering capacity but deciding what to build, with the people who know these grammars best.

### AI makes grammar-building more accessible

An AI assistant given a parser trace, the grammar and example texts can explain in plain language why a word failed and propose a fix, which a person then reviews.

> **Phase 1 serves translation from day one** Bible translation already needs faster checking, back-translations and consistent spelling. Everything Phase 2 needs is built on the same foundation.

---

<!-- _class: cards2 dense -->

# Two phases

- **Phase 1 · Usable for Bible translation within SIL** Built with Bible translation in mind, so its value is easy to judge, and delivered through SIL's own software: FieldWorks, where grammars are built, and Paratext, where translators work. PanGloss makes grammars faster; the tools proved in Motif make them easier to build. **Justified by what it gives translation.**
- **Phase 2 · Usable for the flourishing of all language communities** Grammars leave the translation office: into browsers, word processors, keyboards, encyclopedias, literacy apps and a shared library of grammars. Coverage expands from Scripture to everyday language. Its work over large bodies of parallel text may live in a companion app rather than in FieldWorks. **Needs external partners, if only for broader acceptance.**

![narrow](diagrams/axes.svg)

> **How the phases connect** Phase 2 needs no new theory, only more reach. Every Phase 1 grammar is a Phase 2 grammar waiting to be published, and every Phase 1 tool is the foundation Phase 2 extends.

---

<!-- _class: chapter c3 -->
<!-- header: Chapter 3 -->
<!-- _paginate: false -->

![](art/chapter-3.svg)

###### Chapter 3

# Phase 1: Bible translation

---

<!-- _class: axes -->

# Phase 1 at a glance

Usable for Bible translation within SIL.

| | Today | Phase 1 | How |
|---|---|---|---|
| **Speed** | Slows as a grammar grows | **50× faster (target)** | PanGloss, a port of HermitCrab, plus algorithm improvements that skip work without changing answers, shared with HermitCrab's own |
| **Easy to build** | A trace per word; specialist support and years | **Stats, timing, progress and health diagnostics, with AI help** | PanGloss measures; Motif proves the views on real grammars, and they move into FieldWorks |
| **Deployment** | FieldWorks and FLExTrans | **FieldWorks and Paratext**: faster parsing where grammars are built; back-translations and spelling detection where translators work | PanGloss runs as a single native program or library, with no FieldWorks dependency |
| **Languages** | A committed few, many in progress | **Hundreds (goal)** | A clear win for any translation team that already has a FieldWorks lexicon |
| **Completeness** | Mostly Scripture | Scripture | By design: Phase 1 stays focused on the use that justifies it |

> **What Phase 1 does not try to do** Reach beyond Bible translation, publish grammars publicly, or compile finite-state machines. Those are Phase 2. Keeping Phase 1 narrow is what makes it quick to judge.

![narrow](figures/phase1-path.dc.html)

---

<!-- _class: cols dense -->
<!-- header: Chapter 3 · PanGloss -->

# PanGloss: the engine

**PanGloss** carries HermitCrab into a form that runs anywhere. *Words in, morphemes out.*

### Faithful where it matters

PanGloss ports HermitCrab's algorithms and is held to its answers. It opens a FieldWorks project directly, with no export step, and reads the same FLEx grammar as both FieldWorks parsers, HermitCrab and **XAMPLE**; a small differential test checks it against XAMPLE too.

![](figures/conformance.dc.html)

### Faster by skipping work, not by guessing

The rule is strict: **an optimisation may cost memory or compile time, never a correct answer.** Each one is measured on real grammars and kept only with identical results.

- **Pruning** abandons paths that could never rebuild the word; on one test grammar it cut the search dramatically.
- **Smarter bookkeeping** stops comparing every candidate with every other; on the hardest words tested, the port keeps pace with the original.
- **Parallel batches** spread a word list across every core.
- **Shared with HermitCrab.** A speedup found on either side can move to the other.

<div class="break"></div>

### Deployable anywhere

Rust builds a single native program for Windows, macOS and Linux, with no runtime to install. The engine is built to be used as:

| Surface | For |
|---|---|
| `pangloss` command line | Scripts, CI, AI agents, Motif |
| C library interface | FieldWorks, Paratext, other native hosts |
| WebAssembly | Browsers and web pages |
| Language Pack (`.pgpack`) | One data-only file holding a compiled grammar: no code, so it is safe to share |

All four exist in source today. Packaged, signed releases are Phase 1 work.

### No finite-state compiler in Phase 1

A finite-state compiler makes parsing near-instant, and research on one is well advanced. Phase 1 does not depend on it. It is the centrepiece of Phase 2.

> **Why a port, not a new parser** Every existing FLEx grammar keeps working, every hour already spent on one is kept, and every answer can be checked against the original. Nobody has to trust a new engine on faith.

---

<!-- _class: cols dense -->
<!-- header: Chapter 3 · PanGloss -->

# Stats, health and timing

A grammar author cannot fix what they cannot see. Try a Word shows how one word was parsed; PanGloss adds three measures across a whole word list.

### Timing

Every word is timed. The slowest words are listed, and a runaway word is stopped at a fixed step limit and reported, instead of hanging the batch.

### Statistics

Run over a representative word list, the statistics answer the questions an author actually asks:

```text
pangloss stats grammar.fwdata --group group
    where did the time go, by kind of rule?
pangloss stats grammar.fwdata --group never-fires
    which rules never produced anything?
pangloss stats grammar.fwdata --sort no-root
    which rules build forms no root can finish?
```

<div class="break"></div>

### Grammar health

`grammar-health` reads the grammar itself and lists problems to fix in FieldWorks, graded as errors, warnings or information. Two examples of what it catches:

![](figures/health.dc.html)

- **A partial morpheme.** A stem with no category, or an affix with no slot, can attach almost anywhere. The parser then explores far more paths, and **one such entry can slow down every word**, not just the words that use it.
- **Two phonemes that look identical** once Unicode is normalised, so one is silently skipped or the two are confused.

> **The principle** Reporting never changes a parse. PanGloss keeps or drops exactly what HermitCrab would; the report only decides how loudly to say so.

> > **Why it matters** Statistics turn grammar-building from long experience into measured steps. An author can make one change, re-run, and see whether it helped, in seconds rather than overnight.

---

<!-- _class: cols dense -->
<!-- header: Chapter 3 · Motif -->

# Motif: a proving ground for FieldWorks

**Motif** is where these ideas are tried on real grammars before they move into FieldWorks. It is a desktop application and a command-line tool with the same abilities, working on the FieldWorks project the translation team already uses.

### What the window shows today

- **Overview**: how much of the chosen texts the grammar parses, how accurately, and how fast
- **Texts**: every word in context, parsed or not
- **Try a Word**: the parser's reasoning for one word, step by step, including where it gave up
- **Timing**: the slowest words and the rules that cost the most
- **Warnings**: the grammar-health report, ready to act on
- **Review changes**: check marked changes, then apply them to the FieldWorks project
- **AI Handoff**: the whole picture, packaged for an AI assistant

Motif runs PanGloss as a separate process, so a slow word never freezes the window.

> **Built for review** A change is checked before it lands, not cleaned up after. That is what makes it safe to let an AI suggest changes to a grammar a translation team depends on.

<div class="break"></div>

### The AI Handoff

One click writes five small files: the grammar, the texts, the latest Assessment with traces for the words in question, a helper script, and a short guide. The author drags them into any AI chat and pastes a one-paragraph header. The assistant explains, in plain language, why a word failed and what to change. The full project never leaves the machine.

![](figures/handoff.dc.html)

### Changes, checked before they land

An author marks analysis changes in Texts: approve this one, reject that one. Motif tries them on a copy of the project (a **Dry Run**), re-measures the grammar, and then applies them to the FieldWorks project in one step, with a receipt. It will not apply while FieldWorks has the project open. Larger changes come as **Proposals**: named operations such as *set this gloss* or *create this affix rule*, not a patch to a file. Proposals across the whole grammar, reviewed in the window, are Phase 1 work.

---

<!-- _class: cols dense -->
<!-- header: Chapter 3 · FieldWorks -->

# From proving ground to FieldWorks

Motif is not meant to become one more program for a translation team to install. What proves itself there is meant to move into FieldWorks, where grammars are already built.

### Phase 1: into FieldWorks

![](figures/moves-home.dc.html)

### How the move stays small

Motif is built on .NET 10 and Avalonia, the same move FieldWorks' own interface work is making, and keeps its views and command core separable, so that a later merge is a move, not a rewrite. Until then, the two are designed to meet at one point: at a save, FieldWorks releases the project, asks Motif to apply the pending changes, and reloads.

<div class="break"></div>

### Phase 2: perhaps its own app

Phase 2 works with parallel texts and statistical word harvesting: tens to hundreds of megabytes of outside text that does not belong in a FieldWorks project. Whether that work lives in FieldWorks or in a companion app is still open, and is a decision to make with the FieldWorks team.

### Built with the people who know these grammars

HermitCrab's maintainers and FieldWorks' developers know these grammars, and the people who build them, better than anyone. Speedups, diagnostics and interface work should flow both ways.

> > **A testing ground, not a new product** Motif lets new ideas meet real grammars quickly, without putting a stable, widely used FieldWorks at risk. What works goes home to FieldWorks.

---

<!-- _class: dense -->
<!-- header: Chapter 3 · The loop -->

# The grammar-improvement loop

This is how a grammar gets better each week instead of each year. PanGloss measures, Motif shows, an AI assistant drafts, and a person decides. Steps 1 to 3 work in Motif today, and so do review and apply for analysis changes. Phase 1 finishes the return path from the assistant's advice to a reviewed Proposal across the whole grammar.

![](diagrams/loop.svg)

<div class="trio">

**What the author needs** A good understanding of their language, and the judgement to say whether a proposed change is right. *Not* years of training in computational linguistics.

**What makes it fast** Assessments that finish in seconds; diagnostics that point at the problem; an assistant that reads traces so the author does not have to; changes that are safe to try.

**What keeps it honest** The grammar is tuned, never the parser. Every change has an author, a reason and a record, and is dry-run before it lands.

</div>

---

<!-- _class: cards2 dense -->
<!-- header: Chapter 3 · Deployment -->

# Putting grammars to work in Paratext

Paratext is where Bible translation happens: more than 15,000 users in nearly 550 organisations. It already has a word list, a spelling-status workflow, a prefix-and-suffix morphology check and a word-for-word interlinearizer. Phase 1 puts a real grammar behind them, as two features translators feel on the first day.

- **Automatic back-translations** Each verse gets a literal, word-by-word gloss built from the grammar's analysis: root meaning plus what each affix contributes. Consultants read a draft in a language they don't speak; teams spot where a word does not say what they meant. Later work turns morpheme glosses into natural phrases.
- **Spelling detection** A word the grammar cannot build is flagged. Unlike a word list, the grammar accepts the thousands of correct forms no list contains, and catches the misspellings a list would miss. Inconsistent spellings of the same word across a book are grouped for the team to resolve.

![narrow](figures/verse-checked.dc.html)

### Why hundreds of languages, not a handful

Any translation team with a FieldWorks lexicon and some interlinear text already has most of a grammar's raw material, and many already have a grammar under way. With better diagnostics and an AI assistant, turning that into a working grammar becomes a project of weeks. The reward, back-translations and spelling checks, is something every team wants. **For Bible translation, this is a clear win.**

### Why it serves translation from day one

Consultant checking, back-translation and spelling consistency already cost translation projects real time and money. Phase 1 reduces all three, using software SIL already stewards, on content SIL already works with.

---

<!-- _class: chapter c4 -->
<!-- header: Chapter 4 -->
<!-- _paginate: false -->

![](art/chapter-4.svg)

###### Chapter 4

# Phase 2: Every language community

---

<!-- _class: axes -->

# Phase 2 at a glance

Usable for the flourishing of all language communities.

| | Phase 1 | Phase 2 | How |
|---|---|---|---|
| **Speed** | 50× faster (target) | **1,000× faster (target)** | PanGloss 2.0 compiles a grammar into a finite-state machine that proposes answers in microseconds; the exact engine confirms them |
| **Easy to build** | Diagnostics and AI help | **AI generates changes; the lexicon is harvested statistically** | Motif 2.0, perhaps as a companion app, learns from parallel texts across many domains and integrates statistical tools |
| **Deployment** | FieldWorks and Paratext | **Wikipedia, Firefox, LibreOffice, Keyman prediction, literacy apps** | Language Packs that any host can load, published through a shared library |
| **Languages** | Hundreds | **Every community that wants one** | A shared library where communities publish grammars for spell checkers, predictors and more |
| **Completeness** | Scripture | **News, health, literacy, primary education** | Coverage checks and report cards that show where a grammar is thin |

![narrow](figures/circles.dc.html)

---

<!-- _class: cols dense -->
<!-- header: Chapter 4 · PanGloss 2.0 -->

# PanGloss 2.0: instant

### Compiling grammars to finite-state machines

A **finite-state transducer (FST)** reads a word one letter at a time and emits its analysis. It is how the fastest morphological analysers in the world work: microseconds per word, small enough for a phone.

The risk with an FST is that it can be subtly wrong, because some grammar rules are hard to express in one. PanGloss 2.0 avoids that risk with **propose-and-confirm**: the FST *proposes* candidate analyses almost instantly, and the exact HermitCrab engine *confirms* each one.

The FST is held to never miss an analysis the full engine would find, so **it can only ever cost speed, never correctness.** Where a rule cannot be compiled safely, PanGloss says so and uses the exact engine for it.

> **Early experiments** point to an order-of-magnitude gain over the full engine, with matching answers on the words compared. The path is not yet certified: today's safety checks refuse some grammar constructs, and those take the exact engine.

<div class="break"></div>

![](diagrams/propose-confirm.svg)

---

<!-- _class: cols dense -->
<!-- header: Chapter 4 · PanGloss 2.0 -->

# PanGloss 2.0: spelling and prediction

### From detection to correction

Detection says a word is wrong. Correction says what was meant. The plan layers four signals, each cheap enough to run as someone types:

1. **Edit candidates**: a precompiled index maps likely typos straight to real words in one pass
2. **Keyboard distance**: nearby keys on the actual layout make a slip more likely
3. **Sound-alikes**: spelling-by-ear errors, matched through the language's own sound rules
4. **Context**: word pairs and triples from real text rank the candidates

### Built from lots of text

PanGloss 2.0 builds caches of known words, their analyses and N-gram statistics from large text collections, so correction and prediction reflect how the language is really written.

<div class="break"></div>

### Prediction

A keyboard that predicts words needs to know which words exist. Keyman's predictive text today is built from word lists with frequencies. A grammar can generate those lists, including the valid forms a collected list would miss, and a later integration can ask the grammar directly.

### Hosted

The same engine in WebAssembly powers language-analysis websites: paste text, see every word analysed, no installation.

![](figures/correction.dc.html)

> **Why this matters for small languages** Where little text has been published, there is not enough data to learn spelling statistically. A grammar supplies what the data cannot.

---

<!-- _class: cols dense -->
<!-- header: Chapter 4 · Motif 2.0 -->

# Motif 2.0 and a shared library

Phase 2's work over large bodies of text may live in its own app rather than in FieldWorks. *Motif 2.0* names that work wherever it lands.

### AI that proposes the grammar

In Phase 1, the AI explains and drafts one change at a time. In Motif 2.0 it works from **parallel text**: translations of the same content in the language and in a language of wider communication, across many domains. From them it proposes new lexical entries and rules as Proposals, which people review exactly as before.

![](figures/parallel.dc.html)

### Statistical harvesting of the lexicon

Words the grammar cannot yet parse are the work queue. Statistical tools cluster them, suggest likely roots and affixes, and rank them by how often they occur, so authors spend their time where it matters most.

<div class="break"></div>

### Publish to the library

When a grammar is good enough, its community can publish it as a Language Pack, with its report card attached.

### A shared library of grammars

A public home for grammars, looked after by SIL or a partner such as the Wikimedia Foundation, where language communities publish grammars and any tool can use them:

- **Spell checkers** in Firefox, LibreOffice and other editors
- **Predictors** in Keyman keyboards and on phones
- **Glossers** for websites, publishers and AI systems

A Language Pack contains data only, never executable code, so a host can load one from a stranger without running a stranger's program.

> **Ownership** A language's grammar belongs to its community. The library makes sharing easy; it must also make it the community's choice. Licensing and consent are part of the design, not an afterthought.

---

<!-- _class: cards dense -->
<!-- header: Chapter 4 · Deployment -->

# Where the grammars land

- **Firefox and LibreOffice** Spell checking that understands how words are built, through the same engine in WebAssembly and as a native library.
- **Keyman prediction** SIL's keyboard platform, which supports typing in more than 2,500 languages, suggesting whole words built from their parts.
- **Wikipedia** Editing help across 364 language editions; in a small language, each article is a real share of everything written in it.
- **Literacy and spelling app** Helps learners practise the standard spelling of their language, and helps publishers apply it consistently.
- **Language-analysis websites** Paste a text and see every word analysed, for teachers, researchers and curious speakers.
- **AI back-translation** Instant, structured back-translations for people and for AI systems working in languages they barely know.

![](diagrams/hosts.svg)

---

<!-- _class: cols dense -->
<!-- header: Chapter 4 · Completeness -->

# Report cards for completeness

A grammar built on Scripture knows Scripture's words. A health leaflet or a maths textbook will use many it has never seen. Phase 2 measures this directly, so a community knows what its grammar is ready for.

### Coverage checks

- **Text coverage** (in Motif today): what share of the words in the chosen texts the grammar parses
- **Grammar coverage**: the same measure over a named collection of texts, so a health leaflet and a gospel can be scored apart
- **Feature coverage** (designed): which combinations of affixes and slots the texts actually use, and which the grammar allows that no text shows. Each leftover is either a rule that is too broad or a word nobody has collected yet, and a person decides which

<div class="break"></div>

### A report card

![](figures/report-card.dc.html)

Report cards travel with the Language Pack, so a host can decide whether a grammar is good enough for its use, and a community can see where to work next.

> **Broader adoption** Report cards turn "is this grammar any good?" into a question with a published answer, which is what partners outside SIL will need before they ship it.

---

<!-- _class: chapter c5 -->
<!-- header: Chapter 5 -->
<!-- _paginate: false -->

![](art/chapter-5.svg)

###### Chapter 5

# Risks, costs and next steps

---

<!-- _class: cards dense -->

# What could go wrong

- **Grammar-building is still skilled work** AI and diagnostics lower the bar; they do not remove it. **Mitigation:** Phase 1 starts with teams that already have FieldWorks data, a grammar under way and linguistic support, and measures how long a grammar really takes.
- **Speed targets are targets** 50× and 1,000× are goals, not measurements across real grammars, and a few HermitCrab constructs cannot be compiled safely. **Mitigation:** publish a benchmark on real translation grammars at each milestone; rules that resist compilation take the exact engine, costing speed, never answers.
- **Working apart** Building beside HermitCrab instead of with it would split effort and lose decades of knowledge. **Mitigation:** plan with the SIL.Machine and FieldWorks maintainers from the start; conformance keeps PanGloss answering to HermitCrab, and improvements flow both ways.
- **Adoption outside SIL** Browser makers and encyclopedias will not ship tools they cannot evaluate. **Mitigation:** report cards, data-only Language Packs, open formats, and early partner conversations.
- **Prior art** Giellatekno and Divvun have built finite-state language tools for minority languages since 2001, now across more than 100 languages. **Mitigation:** PanGloss is complementary: it starts from grammars FLEx users already have, and confirms every FST answer against the exact engine. We learn from their best techniques.
- **Community ownership** Publishing a grammar is publishing a community's knowledge. **Mitigation:** consent and licensing built into the shared library from the start.

---

<!-- _class: cols -->

# What it costs, and who pays

### Phase 1: SIL, justified by translation

Phase 1 builds on work already under way. PanGloss and Motif exist and run today; what remains is finishing them, moving what Motif has proved into FieldWorks, integrating with Paratext, and supporting the first translation teams as they build grammars.

With modern tools the engineering is a small team working alongside the FieldWorks and HermitCrab maintainers, not a department. The larger cost is linguistic: supporting teams as they take their grammars further.

**Its return** is measured in consultant time, faster checking and better spelling in translation projects, which SIL already pays for today.

![](figures/phases-carried.dc.html)

<div class="break"></div>

### Phase 2: SIL with partners

Phase 2 reaches beyond SIL's own software, so it needs others:

- **Wikimedia Foundation** for Wikipedia, and possibly as a neutral home for the shared library
- **Mozilla** and **The Document Foundation** for Firefox and LibreOffice
- **Literacy and education organisations** for the spelling app and school content
- **Funders of language technology and development**, since the benefits are in literacy, health and education, not only in translation

> **If only for acceptance** Even where SIL could build a piece alone, a partner's name is what makes a browser, a ministry of education or a publisher trust it.

---

<!-- _class: cols -->

# Next steps

![wide](figures/road.dc.html)

### For Phase 1

1. **Plan together.** Agree a shared plan with the FieldWorks and HermitCrab maintainers, so speedups and tools flow both ways.
2. **Choose pilot projects.** Five to ten Bible translation teams with a FieldWorks lexicon, interlinear texts, a grammar under way and a willing consultant.
3. **Publish a baseline.** Measure today's parse speed and coverage on their data, so every later claim has a before.
4. **Finish the loop.** Complete Assessment, Handoff and Proposals end to end on real projects.
5. **Bring it home.** Move what Motif has proved into FieldWorks, and ship back-translations and spelling detection in Paratext to the pilot teams.
6. **Report.** Time to a working grammar, speedup achieved, and what translators and consultants say.

<div class="break"></div>

### For Phase 2

1. **Open the conversations now.** Wikimedia, Mozilla, The Document Foundation, Keyman, and one literacy partner.
2. **Agree the shared library's steward** and its licensing and consent model.
3. **Prove one grammar end to end**: from FieldWorks to a spell checker in LibreOffice and a Keyman predictor, with a report card.

> > **The decision in front of us** Phase 1 is a Bible translation tool that serves translation from day one and builds on decades of SIL's work. Say yes to it, and the foundation for a working grammar for every language gets built along the way.

---

<!-- _class: cols dense -->
<!-- header: Appendix -->

# Glossary

**Affix** A piece added to a root: a prefix, suffix, infix or circumfix.

**AMPLE** SIL's morphological parser from 1988, and the basis of XAMPLE.

**Assessment** A PanGloss run over a set of words: every word parsed and timed.

**Dry Run** Trying a change against a copy of the project to see its effect before it is applied.

**FieldWorks (FLEx)** SIL's desktop software for lexicons, texts and grammars.

**FLExTrans** A machine-translation system built on FieldWorks data.

**Finite-state transducer (FST)** A compact machine that reads a word letter by letter and emits its analysis, in microseconds.

**Grammar** A description of how the words of a language are built.

**Handoff** Motif's package of grammar, words, texts and traces for an AI assistant.

**HermitCrab** SIL's rule-based morphological parser, designed by Mike Maxwell and built into FieldWorks by Damien Daspit; now part of SIL.Machine.

<div class="break"></div>

**Keyman** SIL's keyboard platform, with predictive text.

**Language Pack** One data-only file holding a compiled grammar, loadable by any PanGloss host.

**Morpheme** The smallest piece of a word that carries meaning.

**Motif** The proving ground for building, measuring and changing grammars; what works moves into FieldWorks.

**PanGloss** A port of HermitCrab to Rust: the same algorithms, portable and measurable.

**Paratext** SIL and the United Bible Societies' software for Bible translation.

**Parser** Software that works out what a word is made of.

**Proposal** A reviewed set of named changes to a project, applied as one unit.

**XAMPLE** FieldWorks' AMPLE-based morphological parser.

---

<!-- _class: cols dense -->
<!-- header: Appendix -->

# Sources and further reading

### History

- Weber, Black and McConnel, *AMPLE: A Tool for Exploring Morphology*, SIL, 1988
- Antworth, *PC-KIMMO: a two-level processor for morphological analysis*, SIL, 1990
- Maxwell, "Two Theories of Morphology, One Implementation", SIL Electronic Working Papers 1998-001
- Black and Simons, "The SIL FieldWorks Language Explorer Approach to Morphological Parsing", 2006
- FieldWorks 6.0 announcement, FLEx list, May 2009
- [FLExTrans](https://software.sil.org/flextrans/) · HermitCrab in [SIL.Machine](https://github.com/sillsdev/machine)

### The projects

- PanGloss: README, the grammar diagnostics guide and the spell-checking plan
- Motif: README, the Motif plan, the user guide and the Handoff format documents

### Measurements quoted

- Half an hour for 7,000 Amharic words, one in six stopped at five seconds: a Motif cross-repository measurement, August 2026
- 5–10× from algorithm work: HermitCrab development in SIL.Machine, 2026

<div class="break"></div>

### Background figures

- 7,159 living languages: [Ethnologue, 28th edition](https://www.ethnologue.com/) (2025)
- 4,457 languages with translation in progress: [Wycliffe Global Alliance](https://wycliffe.net/global-scripture-access/) (August 2025)
- 15,000+ Paratext users, ~550 organisations: [paratext.org](https://paratext.org/about/who-uses-paratext/)
- 2,500+ languages in Keyman: [keyman.com](https://keyman.com/)
- 364 Wikipedia editions: [Wikimedia Meta](https://meta.wikimedia.org/wiki/Wikipedia) (September 2026)
- 2,253 forms of a Finnish noun: [COLING 2018 paper](https://aclanthology.org/C18-1137.pdf)

### Background

- [FieldWorks](https://software.sil.org/fieldworks/) · [Paratext](https://paratext.org/) · [Keyman](https://keyman.com/)
- [Divvun](https://divvun.org/) and [Giellatekno](https://giellalt.github.io/): finite-state language technology for minority languages
- Chapter maps: [Natural Earth](https://www.naturalearthdata.com/) (public domain)
