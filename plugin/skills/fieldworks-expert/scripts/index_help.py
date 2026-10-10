import argparse
import csv
import posixpath
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit


HELP_URL = "https://downloads.languagetechnology.org/fieldworks/Documentation/en/"


class Topic(HTMLParser):
    def __init__(self):
        super().__init__()
        self.in_title = False
        self.title = []
        self.keywords = ""
        self.links = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "title":
            self.in_title = True
        elif tag == "meta" and attrs.get("name", "").lower() == "rh-index-keywords":
            self.keywords = attrs.get("content", "")
        elif tag == "a" and "href" in attrs:
            self.links.append(attrs["href"])

    def handle_endtag(self, tag):
        if tag == "title":
            self.in_title = False

    def handle_data(self, data):
        if self.in_title:
            self.title.append(data)


class Contents(HTMLParser):
    def __init__(self):
        super().__init__()
        self.stack = []
        self.pending = ""
        self.params = None
        self.paths = {}

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "ul":
            self.stack.append(self.pending)
            self.pending = ""
        elif tag == "object" and attrs.get("type") == "text/sitemap":
            self.params = {}
        elif tag == "param" and self.params is not None:
            self.params[attrs.get("name", "").lower()] = attrs.get("value", "")

    def handle_endtag(self, tag):
        if tag == "object" and self.params is not None:
            self.pending = self.params.get("name", "")
            local = self.params.get("local", "")
            if local:
                key = unquote(urlsplit(local).path).replace("\\", "/")
                self.paths[key] = " > ".join(x for x in self.stack + [self.pending] if x)
            self.params = None
        elif tag == "ul" and self.stack:
            self.stack.pop()


def clean(value):
    return " ".join(value.split())


def read_html(path):
    data = path.read_bytes()
    try:
        return data.decode("utf-8-sig")
    except UnicodeDecodeError:
        return data.decode("cp1252", errors="replace")


def index_topics(root):
    contents = Contents()
    contents.feed(read_html(root / "FieldWorks_Language_Explorer_Help.hhc"))
    paths = sorted(p for p in root.rglob("*") if p.suffix.lower() in {".htm", ".html"})
    known = {p.relative_to(root).as_posix() for p in paths}
    for path in paths:
        key = path.relative_to(root).as_posix()
        topic = Topic()
        topic.feed(read_html(path))
        title = clean("".join(topic.title))
        if not title:
            raise ValueError(f"Topic has no title: {key}")
        related = set()
        for href in topic.links:
            link = urlsplit(href.replace("\\", "/"))
            if link.scheme or link.netloc or not link.path:
                continue
            target = posixpath.normpath(posixpath.join(posixpath.dirname(key), unquote(link.path)))
            if target in known and target != key:
                related.add(target)
        breadcrumb = contents.paths.get(key, " / ".join(key.split("/")[:-1]) + " > " + title)
        yield [key, title, clean(breadcrumb), clean(topic.keywords), ";".join(sorted(related)), HELP_URL + quote(key, safe="/")]


def main():
    parser = argparse.ArgumentParser(description="Index metadata from extracted official FLEx 9.3 help.")
    parser.add_argument("extracted_help", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    rows = list(index_topics(args.extracted_help))
    with args.output.open("w", encoding="utf-8", newline="") as output:
        writer = csv.writer(output, delimiter="\t", lineterminator="\n", quoting=csv.QUOTE_NONE, escapechar="\\")
        writer.writerow(["topic", "title", "breadcrumb", "keywords", "related", "source_url"])
        writer.writerows(rows)
    print(f"Indexed {len(rows)} topics")


if __name__ == "__main__":
    main()
