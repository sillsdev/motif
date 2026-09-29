# SIL Language Technology site structure and navigation

Motif can follow a clear SIL pattern: a shared catalog and support hub sit above product-specific areas for downloads, learning, news, and help. FieldWorks is a useful model because its version pages, training, support, and older releases stay connected to the product home and to the shared site footer. [Software Products Catalog — “Search & Filter”](https://software.sil.org/software-products/) · [FieldWorks — product navigation and footer](https://software.sil.org/fieldworks/)

Retrieved from official SIL pages on **2026-09-28**. The review covered the global homepage, catalog, news, support, contact, and legal pages, plus FieldWorks and seven other product areas. Page claims below link to the exact source URL and name the relevant page section or menu label.

## Site map

```text
software.sil.org/
├── /                                  Home: product groupings, recent posts, global footer
├── /software-products/                Searchable and filterable software catalog
├── /news/                             Cross-product software and font news archive
├── /about/                            About Language Technology
│   └── /about/contact/                Contact form and public-help guidance
├── /support/                          Support channels by product family
├── /terms-of-use/                     Site terms
├── /privacy-policy/                   Site privacy policy
├── /language-software-terms-of-use/   SIL Global language-software terms
├── /forums-terms-of-use/              Forum-specific terms and privacy policy
├── /fr/                               French-language site routes
├── /es/software-products/             Spanish catalog route
└── /fieldworks/                       Example product area
    ├── /features/
    ├── /download/
    │   ├── /fw-93/fw-9310/            Current FieldWorks 9.3.10/11 page
    │   └── /previous-releases/         Older version index
    ├── /faq/
    ├── /download/training-videos/
    ├── /help/using-sendreceive/
    ├── /news/                          Product news archive
    ├── /release-notes/                 Stable release notes by version
    ├── /help/release-history/           Cumulative older release history
    ├── /help/                            Help and training hub
    │   ├── /agent/
    │   └── /docs-resources/
    ├── /about/
    │   ├── /developer/
    │   └── /contact/
    └── /fw911/                          Example version announcement
```

The English homepage groups its featured tools under “Document & Translate,” “Publish & Distribute,” and “Write & Communicate,” then shows “Recent Posts.” Its footer is grouped into “SIL Global,” “Language Technology,” “Fonts & Writing Systems,” and “Contact & Support.” [Home — product groupings and footer](https://software.sil.org/)

The main footer routes most useful for Motif are Software Products at `/software-products/`, Software & Font News at `/news/`, About Us at `/about/`, General Software Support at `/support/`, and Contact Us at `/about/contact/`. Product pages retain these parent-site footer groups; FieldWorks additionally has its own Contact, Help and Training, and FLEx Google Group links in its Contact & Support footer group. [Home — “Language Technology” and “Contact & Support”](https://software.sil.org/) · [FieldWorks — footer groups](https://software.sil.org/fieldworks/)

The English homepage’s extracted text exposes the “Language Technology” brand link and a menu control, but not the English primary-navigation labels. The French homepage does expose the labels “Logiciels,” “Polices,” “Apprendre,” “Actualités,” “Assistance,” “Carrières,” and “À propos.” **Inferred:** these correspond to Software, Fonts, Learn, News, Support, Careers, and About in English; the English labels should be confirmed before copying them. [English Home — header and footer](https://software.sil.org/) · [French Home — primary navigation](https://software.sil.org/fr/)

## Catalog, products, and navigation patterns

The catalog at `/software-products/` has search, “Clear All Filters,” operating-system filters (Android, iOS, Linux, Mac, Web, Windows), software-category filters, and support-status filters (Supported, Maintenance only, Discontinued). It offers Card and Table views, and its table shows product name, description, status, and platform availability. The categories are filter choices rather than a set of visible category landing pages. [Software Products Catalog — “Search & Filter,” “Operating Systems,” “Software Categories,” “Supported Status,” and table](https://software.sil.org/software-products/)

The directory below records the recurring product-site menus seen on the sampled pages. FieldWorks, PAWS, Phonology Assistant, FLExTrans, WeSay, Lexique Pro, and Asheninka each have a product-specific menu; the mix of menu labels varies. [FieldWorks — product navigation](https://software.sil.org/fieldworks/) · [PAWS — product navigation](https://software.sil.org/paws/) · [Phonology Assistant — product navigation](https://software.sil.org/phonologyassistant/) · [FLExTrans — product navigation](https://software.sil.org/flextrans/) · [WeSay — product navigation](https://software.sil.org/wesay/) · [Lexique Pro — product navigation](https://software.sil.org/lexiquepro/) · [Asheninka — product navigation](https://software.sil.org/asheninka/)

| Product area | Route and observed structure |
|---|---|
| FieldWorks | `/fieldworks/`: Features; Downloads; Learn; News; Get Help; About. Downloads contains Current Version, Sample Projects, and FLEx Bridge; Learn contains FAQ, Training Videos, and Using Send/Receive; Get Help contains Support Agent, Help and Training, FLEx Google Group, and Documentation and Resources; About contains Developer and Contact. [FieldWorks — product navigation](https://software.sil.org/fieldworks/) |
| PAWS | `/paws/`: Downloads; Learn (How to Install, Tutorial, How to Parse and Write Syntax Using PAWS); News; Get Help (Support Agent, Get Help, FAQ); About (Developer, Contact). [PAWS — product navigation](https://software.sil.org/paws/) |
| Phonology Assistant | `/phonologyassistant/`: Features; Downloads; Learn (How to Install, Getting Started, Tutorials, Help and Documentation); News; Get Help (Support Agent, Get Help, Frequently Asked Questions); About (Developer, Contact). [Phonology Assistant — product navigation](https://software.sil.org/phonologyassistant/) |
| FLExTrans | `/flextrans/`: Downloads; Learn (User Documentation, FLExTrans Podcast, Training Videos, How to Use FlexTrans for Machine Translation); News; Get Help (Support Agent, User Documentation, Common Questions); About (Developer, Contact). Its page links to FieldWorks in the introductory description. [FLExTrans — product navigation and introduction](https://software.sil.org/flextrans/) |
| WeSay | `/wesay/`: Features; Downloads (Linux Packages); Learn (How to Install, Getting Started, Videos); News; Get Help (Frequently Asked Questions); About. The page also links to a WeSay discussion forum. [WeSay — product navigation and “Suggestions”](https://software.sil.org/wesay/) |
| Lexique Pro | `/lexiquepro/`: Features; Downloads; Learn (Published Lexicons); News; Get Help (Frequently Asked Questions); About. Its page says Lexique Pro is discontinued and recommends FieldWorks, Dictionary App Builder, or Webonary. [Lexique Pro — product navigation and discontinuation note](https://software.sil.org/lexiquepro/) |
| Asheninka | `/asheninka/`: Downloads; Learn (How to Install, Tutorial); News; Get Help (FAQ); About (Developer, Contact). Its description links to Paratext and FLEx. [Asheninka — product navigation and introduction](https://software.sil.org/asheninka/) |
| SayMore | `/saymore/`: its page text shows feature content, Recent News Posts, and an Alternatives section; the text extraction did not expose a product menu. Its page links to FLEx and other data tools in the feature descriptions. [SayMore — “Key Features,” “Alternatives,” and “Recent News Posts”](https://software.sil.org/saymore/) |

The catalog includes products with sub-sites under `software.sil.org`, including FieldWorks, PAWS, Phonology Assistant, SayMore, and FLExTrans. It also links Keyman to `keyman.com` and Paratext to `paratext.org`; those separate-domain product sites are exceptions to the SIL product-subdirectory pattern. [Software Products Catalog — product table](https://software.sil.org/software-products/) · [Home — “Document & Translate” and “Write & Communicate”](https://software.sil.org/)

The global parent link back from product pages is the shared footer’s “Software Products” link to `/software-products/`, alongside “Software & Font News” to `/news/`. The FieldWorks menu itself does not list sibling products; sibling links appear contextually in product copy instead. For example, Phonology Assistant names FieldWorks, Toolbox, and Speech Analyzer as data sources, while Lexique Pro recommends FieldWorks, Dictionary App Builder, and Webonary. [FieldWorks — footer groups](https://software.sil.org/fieldworks/) · [Phonology Assistant — introduction and “Common Questions”](https://software.sil.org/phonologyassistant/) · [Lexique Pro — discontinuation note](https://software.sil.org/lexiquepro/)

## FieldWorks product-site tree

The FieldWorks product home is `/fieldworks/`. Its “Features” link resolves to `/fieldworks/features/`; “Downloads” opens `/fieldworks/download/`; “Learn” links to `/fieldworks/faq/`, `/fieldworks/download/training-videos/`, and `/fieldworks/help/using-sendreceive/`; “News” opens `/fieldworks/news/`; “Get Help” links to `/fieldworks/help/`, `/fieldworks/help/agent/`, and `/fieldworks/help/docs-resources/`; and “About” links to `/fieldworks/about/`, `/fieldworks/developer/`, and `/fieldworks/about/contact/`. [FieldWorks — product navigation](https://software.sil.org/fieldworks/) · [FAQ — page title and route](https://software.sil.org/fieldworks/faq/) · [Training Videos — page title and route](https://software.sil.org/fieldworks/download/training-videos/) · [Using Send/Receive — page title and route](https://software.sil.org/fieldworks/help/using-sendreceive/) · [Help and Training — page title and route](https://software.sil.org/fieldworks/help/) · [Support Agent — page title and route](https://software.sil.org/fieldworks/help/agent/) · [Documentation and Resources — page title and route](https://software.sil.org/fieldworks/help/docs-resources/) · [About — page title and route](https://software.sil.org/fieldworks/about/) · [Developer Resources — page title and route](https://software.sil.org/fieldworks/developer/) · [Contact — page title and route](https://software.sil.org/fieldworks/about/contact/)

The Downloads page separates current Windows, Linux-compatible releases, and FieldWorks Lite, then provides Helpful Links for orientation, training, stable release notes, help, and FAQ. Current Version currently resolves to `/fieldworks/download/fw-93/fw-9310/`. “Sample Projects” and “FLEx Bridge” appear in the menu, but their destination paths were not recovered from the text links. [Downloads — “Current Windows,” “Linux-Compatible Releases,” and “Helpful Links”](https://software.sil.org/fieldworks/download/) · [FieldWorks 9.3.10/11 — “Packages For Windows”](https://software.sil.org/fieldworks/download/fw-93/fw-9310/)

## Search, language, and breadcrumbs

The catalog search is labeled “Search” and the News archive search is “Search news by title or software.” The global News archive also filters between Software and Fonts and offers Card or Compact views; the FieldWorks News archive offers Search & Filter, Clear All Filters, and Card or Compact views. These are archive-level controls; no site-wide search item appears in the extracted global or FieldWorks navigation. [Software Products Catalog — “Search & Filter”](https://software.sil.org/software-products/) · [News — “Search & Filter” and “View”](https://software.sil.org/news/) · [FieldWorks News — “Search & Filter” and “View”](https://software.sil.org/fieldworks/news/)

French pages use the `/fr/` prefix, including `/fr/`, `/fr/software-products/`, `/fr/fieldworks/`, and `/fr/fieldworks/download/`; a Spanish catalog is also available at `/es/software-products/`. The text extraction did not expose a language-switch control or show whether changing language preserves the current page, so the locale routes are confirmed but the switch interaction is not. [French Home — primary navigation](https://software.sil.org/fr/) · [French Catalog — catalog title and filters](https://software.sil.org/fr/software-products/) · [French FieldWorks Downloads — page title and route](https://software.sil.org/fr/fieldworks/download/) · [Spanish Catalog — catalog title](https://software.sil.org/es/software-products/)

Breadcrumbs were not consistently exposed in the page-text sources. Direct opening of the FieldWorks 9.3.9 page shows the product menu followed by the page title and version date, with no breadcrumb text; a separate linked capture showed a breadcrumb once, so the visible breadcrumb behavior remains unverified without visual inspection. [FieldWorks 9.3.9 — page title and version date](https://software.sil.org/fieldworks/download/fw-93/fw-939/) · [FieldWorks Downloads — product navigation and “Downloads” heading](https://software.sil.org/fieldworks/download/)

## News and versioned content

The global `/news/` archive presents posts newest first in month groups, with product names, dates, titles, and short summaries. Its entries may lead to different publishing systems: FieldWorks announcements stay under `software.sil.org/fieldworks/`, Bloom release posts link to `community.software.sil.org`, and Keyman posts link to `blog.keyman.com`. SIL Blog and SIL News are separate global links in the shared footer. [News — “September 2026” and post links](https://software.sil.org/news/) · [Home — “Recent Posts” and “SIL Global” footer](https://software.sil.org/)

FieldWorks keeps announcement posts in `/fieldworks/news/` and uses short post paths such as `/fieldworks/fw911/` for the 9.3.11 announcement. Stable notes live separately at `/fieldworks/release-notes/`, with headings comparing each version against the previous stable release. A cumulative release history lives at `/fieldworks/help/release-history/`. [FieldWorks News — month groups and release posts](https://software.sil.org/fieldworks/news/) · [FieldWorks 9.3.11 announcement — “Release Date” and “Release notes”](https://software.sil.org/fieldworks/fw911/) · [Release Notes — version comparison headings](https://software.sil.org/fieldworks/release-notes/) · [Release History — “Release History”](https://software.sil.org/fieldworks/help/release-history/)

Older installers are indexed at `/fieldworks/download/previous-releases/`. The page warns that projects may migrate when the model changes and lists many previous versions; version detail routes are not uniform across generations. Examples include `/fieldworks/download/fw-93/fw-939/` for 9.3.9 and `/fieldworks/download/fw-8010/` for 8.0.10. [Previous Releases — version list and migration note](https://software.sil.org/fieldworks/download/previous-releases/) · [FieldWorks 9.3.9 — page title](https://software.sil.org/fieldworks/download/fw-93/fw-939/) · [FieldWorks 8.0.10 — page title](https://software.sil.org/fieldworks/download/fw-8010/)

## About, support, licensing, and legal pages

`/about/` is titled “About Language Technology” and explains the team’s software, fonts, writing-systems work, standards, open-source approach, and products. Its Contact Us section links to `/about/contact/`. [About Language Technology — “Language Software Development,” “Software Products,” “Standards,” and “Contact Us”](https://software.sil.org/about/) · [Contact — “Send Us a Message”](https://software.sil.org/about/contact/)

The global `/support/` page routes help by product family and links to the Language Software Community at `community.software.sil.org`, Fonts & Writing Systems support, Scripture Software Community, Support.Bible, and Contact Us. The contact page says public help and feature requests should use the community forums, reserves its form for sensitive privacy or security inquiries, and says unsupported or discontinued software does not receive technical support. [Support — “Where to Get Support” and “Additional Support”](https://software.sil.org/support/) · [Contact — “Before using this form”](https://software.sil.org/about/contact/)

The FieldWorks help page offers a support agent, online documentation, videos, courses, and English and French FLEx groups; it gives `flex_Errors@sil.org` for software problems. The community forum groups discussion by product category and has links back to the parent site’s News, Products, and Contact pages. [Get Help and Training — “Need Personal Help?,” “Self-Help Resources,” “FLEx-List Google Group,” and “Additional Help”](https://software.sil.org/fieldworks/help/) · [Language Software Community — category list and top links](https://community.software.sil.org/)

FieldWorks pages state that the product may be used, modified, and redistributed under the GNU Lesser General Public License. Other sampled product pages state their own licenses, including MIT for FLExTrans, SayMore, and WeSay, and LGPL version 2.1 for PAWS and Asheninka; licensing should therefore be read from each product page, not inferred from the site-wide statement. [FieldWorks Downloads — license statement](https://software.sil.org/fieldworks/download/) · [FLExTrans — license statement](https://software.sil.org/flextrans/) · [SayMore — license statement](https://software.sil.org/saymore/) · [WeSay — license statement](https://software.sil.org/wesay/) · [PAWS — license statement](https://software.sil.org/paws/) · [Asheninka — license statement](https://software.sil.org/asheninka/)

The shared footer links to `/terms-of-use/` and `/privacy-policy/`. Separate language-software terms are at `/language-software-terms-of-use/`; forum terms and their privacy/data-retention policy are at `/forums-terms-of-use/`. [Home — legal footer links](https://software.sil.org/) · [Terms of Use — site scope](https://software.sil.org/terms-of-use/) · [Privacy Policy — “Who We Are” and services](https://software.sil.org/privacy-policy/) · [Language Software Terms of Use — “Using our software”](https://software.sil.org/language-software-terms-of-use/) · [Forums Terms of Use — forum scope](https://software.sil.org/forums-terms-of-use/)

One status inconsistency is visible: the catalog marks Lexique Pro “Maintenance only,” while its product page says it is discontinued and no longer supported. [Software Products Catalog — product table](https://software.sil.org/software-products/) · [Lexique Pro — discontinuation note](https://software.sil.org/lexiquepro/)

## Open questions

- Confirm the English primary-navigation labels and whether the language selector preserves the current page; the extracted English header did not expose those controls. [English Home — header](https://software.sil.org/) · [French Home — primary navigation](https://software.sil.org/fr/)
- Confirm whether breadcrumbs render consistently in a normal browser; page-text captures conflict for the FieldWorks 9.3.9 page. [FieldWorks 9.3.9 — page title and version date](https://software.sil.org/fieldworks/download/fw-93/fw-939/)
- Record the URLs produced by catalog/news filter selections and recover the “Sample Projects” and “FLEx Bridge” menu destinations. The controls/menu labels are visible, but those exact target paths were not exposed in the page-text results. [Software Products Catalog — “Search & Filter”](https://software.sil.org/software-products/) · [News — “Search & Filter”](https://software.sil.org/news/) · [FieldWorks Downloads — menu and page content](https://software.sil.org/fieldworks/download/)
- Decide how Motif’s product-status labels will stay consistent between catalog cards and product detail pages; Lexique Pro currently shows different status wording in those places. [Software Products Catalog — product table](https://software.sil.org/software-products/) · [Lexique Pro — discontinuation note](https://software.sil.org/lexiquepro/)

## What Motif's site should do

1. Give Motif a clear product home, a shared catalog/help layer, and a persistent footer link back to the catalog, About, Support, Contact, Terms, and Privacy. Verify these links from the home page and one deep help page. [Home — footer groups](https://software.sil.org/) · [FieldWorks — product navigation and footer](https://software.sil.org/fieldworks/)
2. Group the product menu around concrete user tasks: download, learn, news, get help, and about; make the product home and each menu destination reachable from every product page. [FieldWorks — product navigation](https://software.sil.org/fieldworks/) · [PAWS — product navigation](https://software.sil.org/paws/)
3. Build the catalog with search, platform, product-category, and support-status filters, plus a card/table view. Make each filter state shareable by URL and ensure the shown status matches the product page. [Software Products Catalog — “Search & Filter” and product table](https://software.sil.org/software-products/) · [Lexique Pro — discontinuation note](https://software.sil.org/lexiquepro/)
4. Keep release announcements, current downloads, previous versions, stable release notes, and cumulative history as separate, directly linked destinations; document when version paths change instead of assuming one pattern. [FieldWorks Downloads — “Current Windows” and “Previous Releases”](https://software.sil.org/fieldworks/download/) · [Previous Releases — version list](https://software.sil.org/fieldworks/download/previous-releases/) · [Release Notes — version headings](https://software.sil.org/fieldworks/release-notes/) · [Release History — cumulative versions](https://software.sil.org/fieldworks/help/release-history/)
5. Put public help, private contact, and product-specific support channels on one help page; name the right route for community questions, email problems, and sensitive requests. [Support — support channels](https://software.sil.org/support/) · [Contact — “Before using this form”](https://software.sil.org/about/contact/) · [Get Help and Training — “Additional Help”](https://software.sil.org/fieldworks/help/)
6. Show the product’s license and support status on both the catalog card and product home, and link site-wide and forum-specific legal terms in the footer. [FieldWorks Downloads — license statement](https://software.sil.org/fieldworks/download/) · [Software Products Catalog — product table](https://software.sil.org/software-products/) · [Forums Terms of Use — forum scope](https://software.sil.org/forums-terms-of-use/)
7. Provide a visible language selector and locale-prefixed URLs that preserve the current page when a translation exists; verify the language switch on the catalog, product home, and download page. [French Home — primary navigation](https://software.sil.org/fr/) · [French Catalog — catalog filters](https://software.sil.org/fr/software-products/) · [French FieldWorks Downloads — page title](https://software.sil.org/fr/fieldworks/download/)
