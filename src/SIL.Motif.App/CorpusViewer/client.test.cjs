const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

function viewer() {
  const dom = new JSDOM('<!doctype html><div id="before" tabindex="0"></div><div id="document"></div><div id="after" tabindex="0"></div>',
    { runScripts: 'outside-only', pretendToBeVisual: true });
  dom.window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
  dom.window.eval(fs.readFileSync(path.join(__dirname, 'client.bundle.js'), 'utf8'));
  return dom;
}

test('source offsets preserve CRLF, mixed line endings and non-ASCII spans', () => {
  const dom = viewer();
  const text = 'é\r\n😀word\rthird\nlast';
  const start = text.indexOf('word');
  dom.window.showCorpusDocument({ text, spans: [{ utf16Start: start, utf16Length: 4 }], sourceName: 'Source', phrases: {} });
  assert.equal(dom.window.document.querySelector('.corpus-word').textContent, 'word');
  dom.window.showCorpusOccurrence(start, 4);
  const selected = dom.window.getSelection();
  assert.equal(selected.toString(), 'word');
  dom.window.clearCorpusDocument();
  dom.window.close();
});

test('source is focusable, named and localizes the search panel', () => {
  const dom = viewer();
  dom.window.showCorpusDocument({ text: 'one two', spans: [], sourceName: 'Texte source', phrases: { Find: 'Chercher', next: 'Suivant' } });
  const content = dom.window.document.querySelector('.cm-content');
  assert.equal(content.getAttribute('tabindex'), '0');
  assert.equal(content.getAttribute('role'), 'textbox');
  assert.equal(content.getAttribute('aria-readonly'), 'true');
  assert.equal(content.getAttribute('aria-label'), 'Texte source');
  content.focus();
  assert.equal(dom.window.document.activeElement, content);
  const copy = new dom.window.KeyboardEvent('keydown', { key: 'c', ctrlKey: true, bubbles: true, cancelable: true });
  content.dispatchEvent(copy);
  assert.equal(copy.defaultPrevented, false);
  content.dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true }));
  assert.equal(dom.window.document.querySelector('.cm-search input').getAttribute('aria-label'), 'Chercher');
  assert.match(dom.window.document.querySelector('.cm-search').textContent, /Suivant/);
  dom.window.document.getElementById('after').focus();
  assert.equal(dom.window.document.activeElement.id, 'after');
  dom.window.clearCorpusDocument();
  dom.window.close();
});

test('clearing a failed document switch removes source and decorations', () => {
  const dom = viewer();
  dom.window.showCorpusDocument({ text: 'document A', spans: [{ utf16Start: 0, utf16Length: 8 }], sourceName: 'Source', phrases: {} });
  assert.match(dom.window.document.getElementById('document').textContent, /document A/);
  dom.window.clearCorpusDocument();
  assert.equal(dom.window.document.getElementById('document').textContent, '');
  assert.equal(dom.window.document.querySelector('.corpus-word'), null);
  dom.window.close();
});
