import csv
import hashlib
import importlib.util
import json
import re
import tempfile
import unittest
import zipfile
from pathlib import Path
from urllib.parse import unquote, urlsplit


ROOT = Path(__file__).resolve().parent.parent
SKILLS = ROOT / "plugin/skills"
EXPERTS = ["fieldworks-expert", "fieldworks-parsing-expert"]
SPEC = importlib.util.spec_from_file_location("help_index", SKILLS / EXPERTS[0] / "scripts/index_help.py")
INDEX = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(INDEX)


class IndexTests(unittest.TestCase):
    def test_metadata_only_with_unicode_and_related_links(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Area").mkdir()
            (root / "FieldWorks_Language_Explorer_Help.hhc").write_text(
                '<ul><li><object type="text/sitemap"><param name="Name" value="Grammar"></object>'
                '<ul><li><object type="text/sitemap"><param name="Name" value="A &amp; B">'
                '<param name="Local" value="Area/a.htm"></object></li></ul></li></ul>'
            )
            (root / "Area/a.htm").write_bytes(
                ('<title>A &amp; B</title><meta name="rh-index-keywords" content="café,\nphonemes">'
                 '<p>PRIVATE_BODY_SHOULD_NOT_SHIP</p><a href="../b%20c.htm#anchor">Next</a>'
                 '<a href="https://example.org/secret">External</a><a href="../../outside.htm">Outside</a>'
                 '<a href="#local">Self</a>').encode("cp1252")
            )
            (root / "b c.htm").write_text('<title>Related\n topic</title>')
            rows = list(INDEX.index_topics(root))
            self.assertEqual(len(rows), 2)
            row = next(r for r in rows if r[0] == "Area/a.htm")
            self.assertEqual(row[1], "A & B")
            self.assertEqual(row[2], "Grammar > A & B")
            self.assertEqual(row[3], "café, phonemes")
            self.assertEqual(row[4], "b c.htm")
            self.assertNotIn("PRIVATE_BODY_SHOULD_NOT_SHIP", str(rows))
            self.assertFalse(any("\n" in cell or "\t" in cell for r in rows for cell in r))
            related = next(r for r in rows if r[0] == "b c.htm")
            self.assertTrue(related[-1].endswith("b%20c.htm"))

    def test_missing_title_refuses_a_silent_partial_index(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "FieldWorks_Language_Explorer_Help.hhc").write_text("<ul></ul>")
            (root / "bad.htm").write_text("<p>Not a usable topic</p>")
            with self.assertRaisesRegex(ValueError, "no title"):
                list(INDEX.index_topics(root))


class PackageTests(unittest.TestCase):
    def test_standalone_zip_links_and_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            for name in EXPERTS:
                folder = SKILLS / name
                archive = Path(directory) / (name + ".zip")
                with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
                    for path in sorted(folder.rglob("*")):
                        if path.is_file() and "__pycache__" not in path.parts:
                            output.write(path, Path(name) / path.relative_to(folder))
                isolated = Path(directory) / "isolated" / name
                with zipfile.ZipFile(archive) as package:
                    self.assertIsNone(package.testzip())
                    package.extractall(isolated)
                package_root = isolated / name
                entry = (package_root / "SKILL.md").read_text()
                self.assertTrue(entry.startswith("---\nname: " + name + "\n"))
                self.assertRegex(entry, r"(?m)^description: .+")
                self.assertLess(len(entry.split()), 500)
                for path in package_root.rglob("*.md"):
                    content = path.read_text()
                    self.assertNotRegex(content, r"\bmotif_[a-z_]+\b")
                    if path.parent.name != name:
                        self.assertIn("FieldWorks 9.3.11", content)
                        self.assertIn("11.0.0-beta0182", content)
                    for link in re.findall(r"\[[^\]]*\]\(([^\s)]+)\)", content):
                        target = urlsplit(link)
                        if target.scheme or not target.path:
                            continue
                        local = (path.parent / unquote(target.path)).resolve()
                        self.assertTrue(local.is_relative_to(package_root.resolve()), str(path) + ": " + link)
                        self.assertTrue(local.is_file(), str(path) + ": " + link)

    def test_help_index_is_complete_and_uses_official_topics(self):
        source = next(s for s in json.loads((ROOT / "docs/references/sources.json").read_text())["sources"] if s["id"] == "F08")
        path = SKILLS / EXPERTS[0] / "references/help-index.tsv"
        with path.open(encoding="utf-8", newline="") as stream:
            rows = list(csv.DictReader(stream, delimiter="\t", quoting=csv.QUOTE_NONE, escapechar="\\"))
        self.assertEqual(len(rows), source["topic_count"])
        self.assertEqual(len(rows), len({r["topic"] for r in rows}))
        self.assertEqual(len(path.read_text().splitlines()), len(rows) + 1)
        topics = {r["topic"] for r in rows}
        for row in rows:
            self.assertTrue(row["title"])
            self.assertTrue(row["breadcrumb"])
            self.assertTrue(row["source_url"].startswith(source["official_html_root"]))
            self.assertEqual(unquote(row["source_url"][len(source["official_html_root"]):]), row["topic"])
            for target in row["related"].split(";"):
                if target:
                    self.assertIn(target, topics)

    def test_corpus_coverage_and_one_crosswalk_source(self):
        parser = SKILLS / EXPERTS[1] / "references"
        self.assertEqual(len(list((parser / "broken").glob("*.md"))) - 1, 15)
        self.assertEqual(len(list((parser / "speed").glob("*.md"))) - 1, 10)
        canonical = (ROOT / "plugin/references/crosswalk.md").read_bytes()
        for name in ["linguistic-consultant"] + EXPERTS:
            self.assertEqual((SKILLS / name / "references/crosswalk.md").read_bytes(), canonical)

    def test_prepared_oracle_hashes_remain_valid(self):
        manifest = json.loads((ROOT / "evals/oracle/v1/manifest.json").read_text())
        self.assertEqual(manifest["state"], "prepared")
        self.assertEqual(manifest["live_runs"], 0)
        for record in manifest["content"]:
            self.assertEqual(hashlib.sha256((ROOT / record["path"]).read_bytes()).hexdigest(), record["sha256"], record["path"])


if __name__ == "__main__":
    unittest.main()
