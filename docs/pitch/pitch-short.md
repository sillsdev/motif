---
marp: true
theme: pitch
paginate: true
header: ''
title: Motif in Brief
description: What the Motif demonstration shows today, and how it carries SIL's parsing work toward FieldWorks
---

<!-- _class: cover -->
<!-- _paginate: false -->

# Motif in Brief

A working demonstration of tools for FieldWorks grammars, built on forty years of SIL parsing work.

<div class="scripts"><span lang="am">ቋንቋ</span><span lang="ar" dir="rtl">لغة</span><span lang="hi">भाषा</span><span lang="my">ဘာသာစကား</span><span lang="en">language</span></div>

###### SIL Global · HermitCrab, PanGloss and Motif · September 2026

---

<!-- _class: cols dense -->
<!-- header: Where this comes from -->

# Forty years toward this

A **grammar** describes how the words of a language are built. A **parser** runs it backwards: give it a word, and it tells you what the word is made of. SIL has been building both for decades.

![wide](figures/timeline.dc.html)

HermitCrab's maintainers continue its development today, with 5–10× speedups from algorithm work in SIL.Machine.

<div class="break"></div>

### Grammars being built now

FLExTrans helps draft Scripture in the Philippines and Peru. Parser workshops in Manila (2025) and Nairobi (2026) train linguists to build grammars for their own languages. Many more grammars sit in FieldWorks projects, some mature and many "getting there", built patiently over years.

> **Picking up the baton** Motif does not start over. It keeps every grammar already built, and tries out tools that help the people building them go further.

---

<!-- _class: cards -->
<!-- header: The need -->

# What builders meet as a grammar grows

The linguistics is sound. What gets harder as a grammar grows is everything around it.

- **Waiting** Some combinations of rules become expensive to search. On one Amharic grammar, 7,000 words take about half an hour, and one word in six hits a five-second limit. When re-parsing is slow, it happens less, and the grammar improves more slowly.
- **Seeing** Try a Word shows how one word was parsed. Nothing shows, across a whole text, where the time goes, which rules never fire, or which let in forms the language does not have.
- **Reaching** A finished grammar already does real work in FieldWorks and FLExTrans, and people publish with it. Paratext, word processors and keyboards cannot yet use it for back-translations or spelling.

![narrow](figures/seconds.dc.html)

> **What would help** Faster re-parsing, a clear view of what the grammar is doing, and a safe way to try a change and see whether it helped.

---

<!-- _class: cols dense -->
<!-- header: Motif today -->

# Motif today: a working demonstration

**Motif** is a demonstration, not a finished product: a desktop application and command-line tool that shows, on the FieldWorks project a team already uses, what these tools could be. It runs on Windows, macOS and Linux.

### Seven pages in the window

![](figures/window.dc.html)

- **Overview**: how much of the chosen texts the grammar parses, how accurately, and how fast
- **Texts**: every word in context, parsed or not
- **Try a Word**: the parser's reasoning for any word, step by step, including where it gave up
- **Timing**: the slowest words and the rules that cost the most
- **Warnings**: grammar-health problems to fix in FieldWorks
- **Review changes**: check marked changes, then apply them to the FieldWorks project
- **AI Handoff**: the whole picture, packaged for an AI assistant

<div class="break"></div>

### Changes checked before they land

An author marks analysis changes in Texts. Motif tries them on a copy of the project (a **Dry Run**), re-measures the grammar, and applies them to the FieldWorks project in one step, with a receipt. It will not apply while FieldWorks has the project open.

### The AI Handoff

One click writes five small files: grammar, texts, the latest results with traces, a helper script and a short guide. The author drags them into any AI chat, which explains in plain language why a word failed and what to change. The full project never leaves the machine.

> **Not yet** Support for a team's daily work, Proposals across the whole grammar, reviewed in the window, and a finished way back from the assistant's advice to a reviewed change.

---

<!-- _class: dense -->
<!-- header: How it works -->

# The grammar-improvement loop

PanGloss measures, Motif shows, an AI assistant drafts, and a person decides. Steps 1 to 3 can be seen working in the demonstration today, and so can review and apply for analysis changes.

![](diagrams/loop.svg)

<div class="trio">

**What the author needs** A good understanding of their language, and the judgement to say whether a proposed change is right.

**What makes it fast** Results in seconds; diagnostics that point at the problem; an assistant that reads traces so the author does not have to.

**What keeps it honest** The grammar is tuned, never the parser. Every change has an author and a reason, and is dry-run before it lands.

</div>

---

<!-- _class: cols dense -->
<!-- header: Underneath -->

# PanGloss underneath

**PanGloss** is a port of HermitCrab to Rust. It follows the same algorithms and shares their improvements, and it is what Motif runs.

### Faithful to HermitCrab

Dozens of grammars that trace the contours of the parser hold PanGloss to HermitCrab's answers. Where the two differ, the difference is written up and tracked. It reads FieldWorks projects directly, so every existing FLEx grammar keeps working.

### Faster, never by guessing

Better algorithms are built in HermitCrab and SIL.Machine and shared both ways. PanGloss's own long-term gains come from Rust: careful memory use, every processor core at work, and an engine that runs almost anywhere.

<div class="break"></div>

### Measures across a whole word list

- **Timing** for every word, with runaway words stopped and reported
- **Statistics**: where the time goes, and which rules never fire
- **Grammar health**: problems such as an affix with no slot, which can slow every word

### Runs anywhere

![](figures/surfaces.dc.html)

> **Why a port** Every hour already spent on a FLEx grammar is kept, and every answer can be checked against the original.

---

<!-- _class: cols dense -->
<!-- header: Where it goes -->

# From demonstration to FieldWorks

Motif is not meant to be one more program to install. It is a demonstration where new ideas meet real grammars quickly, without putting a stable, widely used FieldWorks at risk. What works goes home to FieldWorks.

### Phase 1: into FieldWorks

- PanGloss as a parser FieldWorks can call
- Assessment, the Overview, Try a Word, Timing and Warnings
- Checking and applying changes, with a Dry Run first
- The AI Handoff

Motif is built on .NET 10 and Avalonia, the same move FieldWorks' own interface work is making, so a later merge is a move, not a rewrite.

<div class="break"></div>

![](figures/nursery.dc.html)

### Phase 2: perhaps its own app

Learning from parallel texts and harvesting words statistically needs large bodies of outside text that do not belong in a FieldWorks project. Whether that lives in FieldWorks or in a companion app is still open.

### Beyond FieldWorks

Once grammars are fast and easy to build, they can work in **Paratext**, for back-translations and spelling checks, and later in keyboards, browsers and word processors. The longer paper, *A Working Grammar for Every Language*, sets out that path.

---

<!-- _class: cols -->
<!-- header: Next -->

# What we would like to do next

1. **Plan together** with the FieldWorks and HermitCrab maintainers, so speedups and tools flow both ways.
2. **Choose pilot teams**: five to ten translation projects with a FieldWorks lexicon, interlinear texts and a grammar under way.
3. **Publish a baseline** of today's parse speed and coverage on their data.
4. **Finish the loop**, from the assistant's advice to a reviewed change, on real projects.
5. **Bring it home**: move what Motif has demonstrated into FieldWorks.

<div class="break"></div>

### With thanks

This work stands on that of David Weber, Andy Black, Stephen McConnel, Evan Antworth, Mike Maxwell, Gary Simons, Damien Daspit, John Maxwell and Ron Lockwood, and on every linguist who has built a grammar in FieldWorks.

![](figures/baton.dc.html)

> > **The invitation** Help carry forty years of SIL's parsing work the rest of the way: into FieldWorks first, then to every translation team, and one day to every language community.
